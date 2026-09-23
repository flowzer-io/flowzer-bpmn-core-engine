import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { BpmnEditor } from '../bpmnEditor';
import { NEW_ESCALATION } from '../bpmnEditor';
import { readElementProperties } from '../elementProperties';
import type { DiagramElement, ModdleElement } from '../moddle';
import { EscalationSection } from './ElementSections';

/** Ein Eskalationsereignis samt der Dokumentkette, in der die bpmn:Escalation-Wurzelelemente liegen. */
function escalationEvent(options: {
  type?: string;
  escalationRef?: ModdleElement;
  rootEscalations?: ModdleElement[];
}): DiagramElement {
  const definitions = { $type: 'bpmn:Definitions', rootElements: [] as ModdleElement[] } as ModdleElement;
  const process = { $type: 'bpmn:Process', id: 'Process_1', $parent: definitions } as ModdleElement;
  definitions.rootElements = [process, ...(options.rootEscalations ?? [])];

  const definition = { $type: 'bpmn:EscalationEventDefinition' } as ModdleElement;
  if (options.escalationRef) definition.escalationRef = options.escalationRef;

  const businessObject = {
    $type: options.type ?? 'bpmn:IntermediateThrowEvent',
    id: 'Event_1',
    $parent: process,
    eventDefinitions: [definition],
  } as ModdleElement;

  return { id: 'Event_1', type: businessObject.$type, businessObject };
}

function editorDouble() {
  return { setEscalationReference: vi.fn() } as unknown as BpmnEditor;
}

function properties(element: DiagramElement) {
  return readElementProperties(element);
}

describe('EscalationSection', () => {
  // Testzweck: Die Auswahlliste zeigt die vorhandenen bpmn:Escalation des Dokuments, damit ein
  // meldendes und ein fangendes Ereignis auf dieselbe Eskalation zeigen koennen.
  it('bietet die vorhandenen Eskalationen des Dokuments zur Auswahl an', () => {
    const existing = {
      $type: 'bpmn:Escalation',
      id: 'Escalation_1',
      name: 'Freigabe durch die Leitung',
      escalationCode: 'FREIGABE',
    } as ModdleElement;
    const element = escalationEvent({ rootEscalations: [existing] });

    render(<EscalationSection properties={properties(element)} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByRole('option', { name: 'Freigabe durch die Leitung (FREIGABE)' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Neue Eskalation anlegen …' })).toBeInTheDocument();
  });

  // Testzweck: Die Auswahl einer vorhandenen Eskalation schreibt nur deren Kennung; Name und
  // Code des gemeinsamen Wurzelelements bleiben unberührt.
  it('meldet die Auswahl einer vorhandenen Eskalation als Kennung', async () => {
    const existing = {
      $type: 'bpmn:Escalation',
      id: 'Escalation_1',
      name: 'Freigabe durch die Leitung',
      escalationCode: 'FREIGABE',
    } as ModdleElement;
    const editor = editorDouble();
    const element = escalationEvent({ type: 'bpmn:BoundaryEvent', rootEscalations: [existing] });

    render(<EscalationSection properties={properties(element)} editor={editor} readOnly={false} />);
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Eskalation/ }), 'Escalation_1');

    expect(editor.setEscalationReference).toHaveBeenCalledWith('Event_1', { escalationId: 'Escalation_1' });
  });

  // Testzweck: „Neue Eskalation anlegen" ist ein eigener Auftrag an den Adapter und nicht die
  // Auswahl einer Kennung — sonst benenne es die zuletzt gewählte Eskalation um.
  it('meldet das Anlegen einer neuen Eskalation als eigenen Auftrag', async () => {
    const editor = editorDouble();
    const element = escalationEvent({ type: 'bpmn:EndEvent' });

    render(<EscalationSection properties={properties(element)} editor={editor} readOnly={false} />);
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Eskalation/ }), NEW_ESCALATION);

    expect(editor.setEscalationReference).toHaveBeenCalledWith('Event_1', { escalationId: NEW_ESCALATION });
  });

  // Testzweck: Name und Code erscheinen nur, wenn das Ereignis auf eine Eskalation zeigt — ohne
  // Bezug gäbe es nichts zu benennen.
  it('zeigt Name und Code der referenzierten Eskalation', () => {
    const existing = {
      $type: 'bpmn:Escalation',
      id: 'Escalation_1',
      name: 'Freigabe durch die Leitung',
      escalationCode: 'FREIGABE',
    } as ModdleElement;
    const element = escalationEvent({ escalationRef: existing, rootEscalations: [existing] });

    render(<EscalationSection properties={properties(element)} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByLabelText('Name')).toHaveValue('Freigabe durch die Leitung');
    expect(screen.getByLabelText('Eskalationscode')).toHaveValue('FREIGABE');
  });

  // Testzweck: Ein Eskalations-Boundary ohne escalationRef faengt jede Eskalation; das Panel
  // sagt das, statt eine leere Auswahl unerklärt zu lassen.
  it('erklärt ein Boundary ohne ausgewählte Eskalation', () => {
    const element = escalationEvent({ type: 'bpmn:BoundaryEvent' });

    render(<EscalationSection properties={properties(element)} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByText(/fängt dieses Ereignis jede Eskalation/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Eskalationscode')).not.toBeInTheDocument();
  });

  // Testzweck: Der Start eines Ereignis-Subprozesses faengt ebenso wie ein Boundary — die
  // Auswahl darf dort nicht „Ohne Eskalationscode" heissen, als wuerde er selbst melden.
  it('behandelt den Start eines Ereignis-Subprozesses als fangendes Ereignis', () => {
    const element = escalationEvent({ type: 'bpmn:StartEvent' });

    render(<EscalationSection properties={properties(element)} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByRole('option', { name: 'Jede Eskalation' })).toBeInTheDocument();
    expect(screen.getByText(/fängt dieses Ereignis jede Eskalation/)).toBeInTheDocument();
  });

  // Testzweck: An einem meldenden Ereignis erklärt das Panel den Unterschied zum Fehler —
  // die Eskalation bricht den Weg nicht ab und verfällt ohne Fänger.
  it('erklärt am meldenden Ereignis den Unterschied zum Fehler', () => {
    const element = escalationEvent({ type: 'bpmn:IntermediateThrowEvent' });

    render(<EscalationSection properties={properties(element)} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByText(/bricht eine Eskalation den laufenden Weg nicht ab/)).toBeInTheDocument();
    expect(screen.getByText(/verfällt sie/)).toBeInTheDocument();
  });
});
