import { describe, expect, it } from 'vitest';

import { addBranch, canMove, insertAfter, moveBlock, newChoice, newStep, removeBlock, updateStep } from './edit';
import { layoutGraph } from './layout';
import { buildGraph, writeOutlineXml } from './write';
import { readOutline } from './read';
import { hasBlocker, type OutlineChoice, type OutlineDocument, type OutlineStep } from './model';

function process(body: string): string {
  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  id="Definitions_1">
  <bpmn:process id="Process_1" isExecutable="true">
    <bpmn:startEvent id="Start_1" />
${body}
  </bpmn:process>
</bpmn:definitions>`;
}

const LINEAR = process(`    <bpmn:userTask id="Task_1" name="Prüfen" />
    <bpmn:endEvent id="End_1" name="Fertig" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />`);

function messages(xml: string): string[] {
  return readOutline(xml).issues.filter((issue) => issue.level === 'blocker').map((issue) => issue.message);
}

// Testzweck: Der wichtigste Punkt der Gliederung. Was sie nicht abbildet, muss
// sichtbar gemeldet werden und das Speichern sperren — still verlieren waere ein
// Datenverlust im Prozessmodell.
describe('Nicht abbildbare Konstrukte', () => {
  it('meldet ein unbekanntes Element', () => {
    const xml = process(`    <bpmn:subProcess id="Sub_1" name="Teilprozess" />
    <bpmn:endEvent id="End_1" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Sub_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Sub_1" targetRef="End_1" />`);

    expect(messages(xml).join(' ')).toContain('bpmn:subProcess');
  });

  it('meldet eine unbekannte Ereignisdefinition', () => {
    const xml = process(`    <bpmn:userTask id="Task_1" />
    <bpmn:boundaryEvent id="Boundary_1" attachedToRef="Task_1">
      <bpmn:timerEventDefinition id="Timer_1" />
    </bpmn:boundaryEvent>
    <bpmn:endEvent id="End_1" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />`);

    expect(messages(xml).join(' ')).toContain('bpmn:boundaryEvent');
  });

  it('meldet ein unbekanntes Attribut, statt es beim Speichern zu verlieren', () => {
    const xml = process(`    <bpmn:userTask id="Task_1" name="Prüfen" camunda:formRef="x" xmlns:camunda="http://camunda.org/schema/1.0/bpmn" />
    <bpmn:endEvent id="End_1" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />`);

    expect(messages(xml).join(' ')).toContain('camunda:formRef');
  });

  it('meldet einen Rücksprung', () => {
    const xml = process(`    <bpmn:userTask id="Task_1" />
    <bpmn:userTask id="Task_2" />
    <bpmn:endEvent id="End_1" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="Task_2" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="Task_2" targetRef="Task_1" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="Task_2" targetRef="End_1" />`);

    expect(messages(xml).join(' ')).toContain('Rücksprung');
  });

  it('meldet parallele Zweige ohne gemeinsames Tor', () => {
    const xml = process(`    <bpmn:parallelGateway id="Fork_1" name="Los" />
    <bpmn:userTask id="Task_1" />
    <bpmn:userTask id="Task_2" />
    <bpmn:endEvent id="End_1" />
    <bpmn:endEvent id="End_2" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Fork_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Fork_1" targetRef="Task_1" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="Fork_1" targetRef="Task_2" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="Task_1" targetRef="End_1" />
    <bpmn:sequenceFlow id="Flow_5" sourceRef="Task_2" targetRef="End_2" />`);

    expect(messages(xml).join(' ')).toContain('treffen sich nicht');
  });

  it('meldet einen Schritt, der nicht am Ablauf hängt', () => {
    const xml = process(`    <bpmn:userTask id="Task_1" />
    <bpmn:userTask id="Task_lose" name="Vergessen" />
    <bpmn:endEvent id="End_1" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />`);

    expect(messages(xml).join(' ')).toContain('Vergessen');
  });
});

// Testzweck: Ein exklusives Tor mit eigener Zusammenfuehrung ist die zweite
// Grundform neben der Prüfkette und muss unveraendert zurueckgeschrieben werden.
describe('Verzweigung mit eigener Zusammenführung', () => {
  const xml = process(`    <bpmn:exclusiveGateway id="Gw_1" name="Betrag?" default="Flow_klein" />
    <bpmn:userTask id="Task_gross" name="Freigabe" />
    <bpmn:userTask id="Task_klein" name="Buchen" />
    <bpmn:exclusiveGateway id="Gw_2" name="weiter" />
    <bpmn:endEvent id="End_1" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Gw_1" />
    <bpmn:sequenceFlow id="Flow_gross" name="über 1000" sourceRef="Gw_1" targetRef="Task_gross">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=betrag &gt; 1000</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Flow_klein" name="sonst" sourceRef="Gw_1" targetRef="Task_klein" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_gross" targetRef="Gw_2" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="Task_klein" targetRef="Gw_2" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="Gw_2" targetRef="End_1" />`);

  it('erkennt Tor, Bedingung, Standardweg und Zusammenführung', () => {
    const { document, issues } = readOutline(xml);
    expect(hasBlocker(issues)).toBe(false);

    const choice = document!.blocks[0]! as OutlineChoice;
    expect(choice.joinId).toBe('Gw_2');
    expect(choice.branches[0]!.condition).toBe('=betrag > 1000');
    expect(choice.branches[1]!.isDefault).toBe(true);
    expect(document!.blocks.map((block) => block.kind)).toEqual(['choice', 'end']);
  });
});

// Testzweck: Bearbeiten darf die Gliederung nie in einen Zustand bringen, der
// sich nicht mehr schreiben laesst.
describe('Bearbeiten', () => {
  function linear(): OutlineDocument {
    return readOutline(LINEAR).document!;
  }

  it('fügt einen Schritt hinter einem anderen ein und schreibt ihn mit', () => {
    const document = linear();
    const step = newStep(document, 'service');
    const changed = insertAfter(document, 'Task_1', step);

    expect(changed.blocks.map((block) => block.id)).toEqual(['Task_1', step.id, 'End_1']);
    const { graph, issues } = buildGraph(changed);
    expect(hasBlocker(issues)).toBe(false);
    expect(graph!.flows.some((flow) => flow.source === 'Task_1' && flow.target === step.id)).toBe(true);
  });

  it('verschiebt und löscht Schritte innerhalb ihrer Folge', () => {
    const document = insertAfter(linear(), 'Task_1', newStep(linear(), 'user'));
    const second = document.blocks[1]!.id;

    expect(canMove(document, 'Task_1', 'up')).toBe(false);
    const moved = moveBlock(document, second, 'up');
    expect(moved.blocks.map((block) => block.id)).toEqual([second, 'Task_1', 'End_1']);

    const removed = removeBlock(moved, 'Task_1');
    expect(removed.blocks.map((block) => block.id)).toEqual([second, 'End_1']);
    expect(hasBlocker(buildGraph(removed).issues)).toBe(false);
  });

  it('ändert die Angaben eines Schritts', () => {
    const changed = updateStep(linear(), 'Task_1', { candidateGroups: 'Buchhaltung', dueDate: 'P2D' });
    expect(changed.blocks[0] as OutlineStep).toMatchObject({ candidateGroups: 'Buchhaltung', dueDate: 'P2D' });
  });

  it('legt eine Verzweigung mit leeren Zweigen an, die auf den Rest weiterlaufen', () => {
    const document = linear();
    const choice = newChoice(document);
    const changed = addBranch(insertAfter(document, 'Task_1', choice), choice.id);

    const written = buildGraph(changed);
    expect(hasBlocker(written.issues)).toBe(false);
    // Alle drei leeren Zweige zeigen auf das Ende, das der Verzweigung folgt.
    expect(written.graph!.flows.filter((flow) => flow.source === choice.id && flow.target === 'End_1')).toHaveLength(3);
  });
});

// Testzweck: Die Engine verlangt an jeder Aufgabe ein Formular beziehungsweise
// einen Diensttyp. Das muss die Gliederung vor dem Speichern sagen, statt es die
// API ablehnen zu lassen — ein vorhandenes Modell bleibt trotzdem lesbar.
describe('Fehlende Angaben am Schritt', () => {
  it('sperrt das Speichern eines neuen Schritts ohne Formular', () => {
    const document = readOutline(LINEAR).document!;
    const step = newStep(document, 'user');
    const { xml, issues } = writeOutlineXml(insertAfter(document, 'Task_1', step));

    expect(xml).toBeUndefined();
    expect(issues.map((issue) => issue.message).join(' ')).toContain('braucht ein Formular');
  });

  it('sperrt das Speichern eines Dienstaufrufs ohne Typ', () => {
    const document = readOutline(LINEAR).document!;
    const { issues } = writeOutlineXml(insertAfter(document, 'Task_1', newStep(document, 'service')));

    expect(issues.map((issue) => issue.message).join(' ')).toContain('braucht einen Typ des Dienstes');
  });

  it('lässt ein vorhandenes Modell mit dieser Lücke trotzdem lesen', () => {
    // LINEAR enthaelt eine Aufgabe ohne Formular; sie soll sichtbar sein, nicht verborgen.
    expect(readOutline(LINEAR).document).toBeDefined();
  });
});

// Testzweck: Die berechnete Anordnung muss jeden Knoten platzieren und darf
// keine zwei Knoten uebereinanderlegen — sonst ist das Diagramm unbrauchbar.
describe('Anordnung', () => {
  it('platziert jeden Knoten überschneidungsfrei und von links nach rechts', () => {
    const document = readOutline(LINEAR).document!;
    const withChoice = insertAfter(document, 'Task_1', newChoice(document));
    const { graph } = buildGraph(withChoice);
    const layout = layoutGraph(graph!);

    expect(layout.nodes).toHaveLength(graph!.nodes.length);

    for (const flow of graph!.flows) {
      const from = layout.nodes.find((box) => box.id === flow.source)!;
      const to = layout.nodes.find((box) => box.id === flow.target)!;
      expect(from.x + from.width).toBeLessThanOrEqual(to.x);
    }

    for (const a of layout.nodes) {
      for (const b of layout.nodes) {
        if (a.id === b.id) continue;
        const overlaps =
          a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
        expect(overlaps).toBe(false);
      }
    }
  });
});
