import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { BpmnEditor } from '../bpmnEditor';
import { readElementProperties } from '../elementProperties';
import type { DiagramElement, ModdleElement } from '../moddle';
import { CallActivitySection } from './ElementSections';

/** Eine Call Activity mit der Erweiterung, in der die Prozesskennung steht. */
function callActivity(processId: string): DiagramElement {
  const businessObject = {
    $type: 'bpmn:CallActivity',
    id: 'CallActivity_1',
    extensionElements: {
      $type: 'bpmn:ExtensionElements',
      values: [{ $type: 'zeebe:CalledElement', processId } as ModdleElement],
    } as ModdleElement,
  } as ModdleElement;

  return { id: 'CallActivity_1', type: businessObject.$type, businessObject };
}

function editorDouble() {
  return { setCalledProcess: vi.fn() } as unknown as BpmnEditor;
}

function section(element: DiagramElement) {
  return (
    <CallActivitySection
      properties={readElementProperties(element)}
      editor={editorDouble()}
      readOnly={false}
    />
  );
}

describe('CallActivitySection', () => {
  // Testzweck: Die Engine schlägt die Kennung in dieser Stufe wörtlich nach. Ein Wert mit
  // führendem `=` ist ein FEEL-Ausdruck und wird beim Veröffentlichen abgelehnt — das gehört
  // an die Zeile und nicht erst in die Fehlermeldung des Deployments.
  it('warnt, sobald die Prozesskennung als Ausdruck geschrieben ist', () => {
    render(section(callActivity('=zielProzess')));

    expect(screen.getByText(/beim Veröffentlichen abgelehnt/)).toBeInTheDocument();
    expect(screen.getByLabelText('Prozesskennung')).toHaveValue('=zielProzess');
  });

  // Testzweck: Bei einer literalen Kennung bleibt derselbe Sachverhalt ein ruhiger Hinweis an
  // der Zeile — eine Warnung am korrekt gefüllten Feld wäre ein Fehlalarm.
  it('erklärt die Literalregel bei einer gültigen Kennung ohne Warnung', () => {
    render(section(callActivity('Process_Urlaubsantrag')));

    expect(screen.getByText(/feste Kennung/i)).toBeInTheDocument();
    expect(screen.queryByText(/beim Veröffentlichen abgelehnt/)).not.toBeInTheDocument();
  });

  // Testzweck: Die leere Kennung hat ihre eigene, ältere Warnung. Der Literalhinweis darf sie
  // nicht verdrängen — ohne Kennung gibt es noch gar nichts, was ein Ausdruck sein könnte.
  it('meldet die fehlende Kennung weiterhin als eigene Warnung', () => {
    render(section(callActivity('')));

    expect(screen.getByText(/lässt sich der Workflow nicht speichern/)).toBeInTheDocument();
    expect(screen.queryByText(/beim Veröffentlichen abgelehnt/)).not.toBeInTheDocument();
  });
});
