import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { BpmnEditor } from '../bpmnEditor';
import { NEW_ERROR } from '../bpmnEditor';
import { readElementProperties } from '../elementProperties';
import type { DiagramElement, ModdleElement } from '../moddle';
import { ErrorSection } from './ElementSections';

/** Ein Error-Ereignis samt der Dokumentkette, in der die bpmn:Error-Wurzelelemente liegen. */
function errorEvent(options: {
  type?: string;
  errorRef?: ModdleElement;
  rootErrors?: ModdleElement[];
}): DiagramElement {
  const definitions = { $type: 'bpmn:Definitions', rootElements: [] as ModdleElement[] } as ModdleElement;
  const process = { $type: 'bpmn:Process', id: 'Process_1', $parent: definitions } as ModdleElement;
  definitions.rootElements = [process, ...(options.rootErrors ?? [])];

  const definition = { $type: 'bpmn:ErrorEventDefinition' } as ModdleElement;
  if (options.errorRef) definition.errorRef = options.errorRef;

  const businessObject = {
    $type: options.type ?? 'bpmn:EndEvent',
    id: 'Event_1',
    $parent: process,
    eventDefinitions: [definition],
  } as ModdleElement;

  return { id: 'Event_1', type: businessObject.$type, businessObject };
}

function editorDouble() {
  return { setErrorReference: vi.fn() } as unknown as BpmnEditor;
}

function properties(element: DiagramElement) {
  return readElementProperties(element);
}

describe('ErrorSection', () => {
  // Testzweck: Die Auswahlliste zeigt die vorhandenen bpmn:Error des Dokuments, damit ein
  // werfendes und ein fangendes Ereignis auf denselben Fehler zeigen koennen.
  it('bietet die vorhandenen Fehler des Dokuments zur Auswahl an', () => {
    const existing = { $type: 'bpmn:Error', id: 'Error_1', name: 'Antrag unvollständig', errorCode: 'ANTRAG' } as ModdleElement;
    const element = errorEvent({ rootErrors: [existing] });

    render(<ErrorSection properties={properties(element)} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByRole('option', { name: 'Antrag unvollständig (ANTRAG)' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Neuen Fehler anlegen …' })).toBeInTheDocument();
  });

  // Testzweck: Die Auswahl eines vorhandenen Fehlers schreibt nur dessen Kennung; Name und
  // Code des gemeinsamen Wurzelelements bleiben unberührt.
  it('meldet die Auswahl eines vorhandenen Fehlers als Kennung', async () => {
    const existing = { $type: 'bpmn:Error', id: 'Error_1', name: 'Antrag unvollständig', errorCode: 'ANTRAG' } as ModdleElement;
    const editor = editorDouble();
    const element = errorEvent({ type: 'bpmn:BoundaryEvent', rootErrors: [existing] });

    render(<ErrorSection properties={properties(element)} editor={editor} readOnly={false} />);
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Fehler/ }), 'Error_1');

    expect(editor.setErrorReference).toHaveBeenCalledWith('Event_1', { errorId: 'Error_1' });
  });

  // Testzweck: „Neuen Fehler anlegen" ist ein eigener Auftrag an den Adapter und nicht die
  // Auswahl einer Kennung — sonst benenne es den zuletzt gewählten Fehler um.
  it('meldet das Anlegen eines neuen Fehlers als eigenen Auftrag', async () => {
    const editor = editorDouble();
    const element = errorEvent({});

    render(<ErrorSection properties={properties(element)} editor={editor} readOnly={false} />);
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Fehler/ }), NEW_ERROR);

    expect(editor.setErrorReference).toHaveBeenCalledWith('Event_1', { errorId: NEW_ERROR });
  });

  // Testzweck: Name und Code erscheinen nur, wenn das Ereignis auf einen Fehler zeigt — ohne
  // Bezug gäbe es nichts zu benennen.
  it('zeigt Name und Code des referenzierten Fehlers', () => {
    const existing = { $type: 'bpmn:Error', id: 'Error_1', name: 'Antrag unvollständig', errorCode: 'ANTRAG' } as ModdleElement;
    const element = errorEvent({ errorRef: existing, rootErrors: [existing] });

    render(<ErrorSection properties={properties(element)} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByLabelText('Name')).toHaveValue('Antrag unvollständig');
    expect(screen.getByLabelText('Fehlercode')).toHaveValue('ANTRAG');
  });

  // Testzweck: Ein Error-Boundary ohne errorRef faengt jeden Fehler; das Panel sagt das, statt
  // eine leere Auswahl unerklärt zu lassen.
  it('erklärt ein Boundary ohne ausgewählten Fehler', () => {
    const element = errorEvent({ type: 'bpmn:BoundaryEvent' });

    render(<ErrorSection properties={properties(element)} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByText(/fängt dieses Ereignis jeden Fehler/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Fehlercode')).not.toBeInTheDocument();
  });
});
