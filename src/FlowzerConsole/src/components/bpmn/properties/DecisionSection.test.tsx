import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { BpmnEditor } from '../bpmnEditor';
import { readElementProperties } from '../elementProperties';
import type { DiagramElement, ModdleElement } from '../moddle';
import { DecisionSection } from './ElementSections';

const CATALOG = [
  { decisionId: 'dish', name: 'Gericht' },
  { decisionId: 'season', name: 'Saison' },
];

/** Ein Business-Rule-Task mit den angegebenen Erweiterungen. */
function businessRuleTask(...extensions: ModdleElement[]): DiagramElement {
  const businessObject = {
    $type: 'bpmn:BusinessRuleTask',
    id: 'Rule_1',
    ...(extensions.length > 0
      ? { extensionElements: { $type: 'bpmn:ExtensionElements', values: extensions } as ModdleElement }
      : {}),
  } as ModdleElement;

  return { id: 'Rule_1', type: businessObject.$type, businessObject };
}

function calledDecision(decisionId: string, resultVariable: string): ModdleElement {
  return { $type: 'zeebe:CalledDecision', decisionId, resultVariable } as ModdleElement;
}

function taskDefinition(type: string): ModdleElement {
  return { $type: 'zeebe:TaskDefinition', type } as ModdleElement;
}

function renderSection(element: DiagramElement, unavailable = false) {
  const editor = {
    setCalledDecision: vi.fn(),
    setBusinessRuleMode: vi.fn(),
  } as unknown as BpmnEditor;

  render(
    <DecisionSection
      properties={readElementProperties(element)}
      editor={editor}
      readOnly={false}
      decisions={CATALOG}
      decisionsUnavailable={unavailable}
    />,
  );

  return editor as unknown as {
    setCalledDecision: ReturnType<typeof vi.fn>;
    setBusinessRuleMode: ReturnType<typeof vi.fn>;
  };
}

describe('DecisionSection', () => {
  // Testzweck: Die Auswahl aus dem Katalog schreibt die decisionId in zeebe:calledDecision —
  // genau das Attribut, das die Engine liest.
  it('schreibt die ausgewaehlte Entscheidung', () => {
    const editor = renderSection(businessRuleTask(calledDecision('', '')));

    fireEvent.change(screen.getByLabelText('Aufgerufene Entscheidung'), { target: { value: 'dish' } });

    expect(editor.setCalledDecision).toHaveBeenCalledWith('Rule_1', { decisionId: 'dish' });
    expect(screen.getByRole('option', { name: 'Gericht · dish' })).toBeInTheDocument();
  });

  // Testzweck: Die Ergebnisvariable ist keine Zierde — ohne sie kann niemand das Ergebnis
  // lesen, und das Veröffentlichen lehnt den Workflow ab.
  it('schreibt die Ergebnisvariable und warnt, solange sie fehlt', () => {
    const editor = renderSection(businessRuleTask(calledDecision('dish', '')));

    expect(screen.getByText(/niemand könnte ihr Ergebnis lesen/)).toBeInTheDocument();

    const field = screen.getByLabelText('Ergebnisvariable');
    fireEvent.change(field, { target: { value: 'gericht' } });
    fireEvent.blur(field);

    expect(editor.setCalledDecision).toHaveBeenCalledWith('Rule_1', { resultVariable: 'gericht' });
  });

  // Testzweck: Das Umschalten auf „Als Auftrag" entfernt die Entscheidung; bliebe sie stehen,
  // hinge am Element eine Konfiguration, die das Panel nicht mehr zeigt.
  it('schaltet auf „Als Auftrag" um', () => {
    const editor = renderSection(businessRuleTask(calledDecision('dish', 'gericht')));

    fireEvent.click(screen.getByRole('tab', { name: 'Als Auftrag' }));

    expect(editor.setBusinessRuleMode).toHaveBeenCalledWith('Rule_1', 'job');
  });

  // Testzweck: Ein gesetzter Auftragstyp schlaegt die Entscheidung — genauso liest die Engine
  // es. Die Entscheidungsfelder duerfen dann gar nicht erst zu sehen sein.
  it('zeigt bei gesetztem Auftragstyp keine Entscheidungsfelder', () => {
    renderSection(businessRuleTask(taskDefinition('regel-pruefen')));

    expect(screen.queryByLabelText('Aufgerufene Entscheidung')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Ergebnisvariable')).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Als Auftrag' })).toHaveAttribute('aria-selected', 'true');
  });

  // Testzweck: Eine Entscheidung, die der Katalog nicht kennt, darf nicht still aus der
  // Auswahl fallen — sie wuerde sonst beim naechsten Klick ueberschrieben.
  it('haelt eine unbekannte Entscheidung in der Auswahl', () => {
    renderSection(businessRuleTask(calledDecision('abgeschaltet', 'wert')));

    expect(screen.getByRole('option', { name: 'Nicht im Katalog · abgeschaltet' })).toBeInTheDocument();
  });
});
