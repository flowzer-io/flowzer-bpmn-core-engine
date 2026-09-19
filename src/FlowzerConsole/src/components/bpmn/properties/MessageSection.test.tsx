import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { BpmnEditor } from '../bpmnEditor';
import { readElementProperties } from '../elementProperties';
import type { DiagramElement, ModdleElement } from '../moddle';
import { JobSection, MessageSection } from './ElementSections';

function messageRoot(name = 'Freigabe erteilt', correlationKey = '=antragsnummer'): ModdleElement {
  return {
    $type: 'bpmn:Message',
    id: 'Message_1',
    name,
    extensionElements: {
      $type: 'bpmn:ExtensionElements',
      values: [{ $type: 'zeebe:Subscription', correlationKey } as ModdleElement],
    } as ModdleElement,
  } as ModdleElement;
}

/** Ein sendendes Element: Throw-Event, Message-Ende oder Send-Task. */
function sendingElement(options: { type: string; message?: ModdleElement; jobType?: string }): DiagramElement {
  const businessObject = { $type: options.type, id: 'Element_1' } as ModdleElement;

  if (options.type === 'bpmn:SendTask') {
    if (options.message) businessObject.messageRef = options.message;
  } else {
    const definition = { $type: 'bpmn:MessageEventDefinition' } as ModdleElement;
    if (options.message) definition.messageRef = options.message;
    businessObject.eventDefinitions = [definition];
  }

  if (options.jobType) {
    businessObject.extensionElements = {
      $type: 'bpmn:ExtensionElements',
      values: [{ $type: 'zeebe:TaskDefinition', type: options.jobType } as ModdleElement],
    } as ModdleElement;
  }

  return { id: 'Element_1', type: options.type, businessObject };
}

function editorDouble() {
  return { setMessage: vi.fn(), setJob: vi.fn() } as unknown as BpmnEditor;
}

describe('Nachrichtenabschnitt an sendenden Elementen', () => {
  // Testzweck: Throw-Event, Message-Ende und Send-Task zeigen denselben Abschnitt wie die
  // fangende Seite — sonst liesse sich die Nachricht, die die Engine aussendet, nicht benennen.
  it.each([
    ['bpmn:IntermediateThrowEvent'],
    ['bpmn:EndEvent'],
    ['bpmn:SendTask'],
  ])('zeigt Name und Korrelationsschlüssel an %s', (type) => {
    const properties = readElementProperties(sendingElement({ type, message: messageRoot() }));

    render(<MessageSection properties={properties} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByLabelText('Name')).toHaveValue('Freigabe erteilt');
    expect(screen.getByLabelText('Korrelationsschlüssel')).toHaveValue('=antragsnummer');
    expect(properties.sendsMessage).toBe(true);
  });

  // Testzweck: Eine Änderung am sendenden Element erreicht denselben Adapterweg wie am
  // Catch-Ereignis; nur so entsteht die gemeinsame bpmn:Message des Dokuments.
  it('meldet den geänderten Namen an den Adapter', async () => {
    const editor = editorDouble();
    const properties = readElementProperties(
      sendingElement({ type: 'bpmn:IntermediateThrowEvent', message: messageRoot() }),
    );

    render(<MessageSection properties={properties} editor={editor} readOnly={false} />);
    await userEvent.clear(screen.getByLabelText('Name'));
    await userEvent.type(screen.getByLabelText('Name'), 'Antrag abgelehnt');
    await userEvent.tab();

    expect(editor.setMessage).toHaveBeenCalledWith('Element_1', { name: 'Antrag abgelehnt' });
  });

  // Testzweck: Der Abschnitt sagt an der sendenden Seite, dass der Schritt nicht wartet und
  // dass nur die zugeordneten Eingabewerte mitgehen — beides unterscheidet ihn vom Warten.
  it('erklärt Nichtwarten und Datensparsamkeit beim Senden', () => {
    const properties = readElementProperties(
      sendingElement({ type: 'bpmn:IntermediateThrowEvent', message: messageRoot() }),
    );

    render(<MessageSection properties={properties} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByText(/läuft sofort weiter/)).toBeInTheDocument();
    expect(screen.getByText(/nur die Eingabewerte/)).toBeInTheDocument();
  });

  // Testzweck: Am wartenden Ereignis bleibt die bisherige Erklärung stehen; die sendende darf
  // sie nicht verdrängen.
  it('erklärt am wartenden Ereignis weiterhin das Warten', () => {
    const businessObject = {
      $type: 'bpmn:IntermediateCatchEvent',
      id: 'Element_1',
      eventDefinitions: [{ $type: 'bpmn:MessageEventDefinition', messageRef: messageRoot() } as ModdleElement],
    } as ModdleElement;
    const properties = readElementProperties({ id: 'Element_1', type: businessObject.$type, businessObject });

    render(<MessageSection properties={properties} editor={editorDouble()} readOnly={false} />);

    expect(properties.sendsMessage).toBe(false);
    expect(screen.getByText(/wartet, bis eine Nachricht/)).toBeInTheDocument();
  });

  // Testzweck: Der Auftragstyp ist am sendenden Element freiwillig. Die alte Warnung „ohne
  // Auftragstyp bleibt die Instanz stehen" wäre dort schlicht falsch.
  it('nennt den Auftragstyp am sendenden Element optional', () => {
    const properties = readElementProperties(
      sendingElement({ type: 'bpmn:SendTask', message: messageRoot() }),
    );

    render(<JobSection properties={properties} editor={editorDouble()} readOnly={false} />);

    expect(properties.jobTypeOptional).toBe(true);
    expect(screen.getByText('Auftrag (optional)')).toBeInTheDocument();
    expect(screen.queryByText(/bliebe hier stehen/)).not.toBeInTheDocument();
  });

  // Testzweck: Ist ein Auftragstyp gesetzt, sagt der Abschnitt, dass er die interne Zustellung
  // ersetzt — sonst erwartete jemand beides zugleich.
  it('weist auf den Vorrang des Auftragstyps hin', () => {
    const properties = readElementProperties(
      sendingElement({ type: 'bpmn:SendTask', message: messageRoot(), jobType: 'mail-versenden' }),
    );

    render(<JobSection properties={properties} editor={editorDouble()} readOnly={false} />);

    expect(screen.getByText(/ersetzt die interne Zustellung/)).toBeInTheDocument();
  });
});
