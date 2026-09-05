import { describe, expect, it } from 'vitest';

import { mainPath, nodeLabel, nodeTypeLabel, parseBpmn } from './bpmnModel';

const XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_1">
  <bpmn:process id="Process_1" isExecutable="true">
    <bpmn:startEvent id="Start_1" name="Rechnung eingegangen">
      <bpmn:outgoing>Flow_1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:userTask id="Task_erfassen" name="Rechnung erfassen" />
    <bpmn:exclusiveGateway id="Gateway_1" name="Betrag prüfen" />
    <bpmn:userTask id="Task_freigeben" name="Freigabe erteilen" />
    <bpmn:serviceTask id="Task_auto" name="Auto-Freigabe" />
    <bpmn:endEvent id="End_1" name="Fertig" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_erfassen" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_erfassen" targetRef="Gateway_1" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="Gateway_1" targetRef="Task_auto" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="Gateway_1" targetRef="Task_freigeben" />
    <bpmn:sequenceFlow id="Flow_5" sourceRef="Task_auto" targetRef="End_1" />
    <bpmn:sequenceFlow id="Flow_6" sourceRef="Task_freigeben" targetRef="End_1" />
  </bpmn:process>
</bpmn:definitions>`;

// Testzweck: Der leichte DOM-Parser ersetzt bpmn-js für Kennzahlen und Namen.
// Er muss Elemente und Sequenzflüsse unabhängig vom Namensraum-Präfix finden.
describe('parseBpmn', () => {
  it('liest alle Flow-Elemente unabhängig vom Namensraum-Präfix', () => {
    const model = parseBpmn(XML);
    expect(model.nodes).toHaveLength(6);
    expect(model.nodeById.get('Task_freigeben')?.name).toBe('Freigabe erteilen');
    expect(model.nodeById.get('Gateway_1')?.type).toBe('exclusiveGateway');
  });

  it('liest die Sequenzflüsse', () => {
    const model = parseBpmn(XML);
    expect(model.flows).toHaveLength(6);
    expect(model.flows[0]).toEqual({ id: 'Flow_1', sourceRef: 'Start_1', targetRef: 'Task_erfassen' });
  });

  it('liefert bei ungültigem oder fehlendem XML ein leeres Modell', () => {
    expect(parseBpmn(undefined).nodes).toHaveLength(0);
    expect(parseBpmn('kein xml').nodes).toHaveLength(0);
  });
});

describe('nodeLabel', () => {
  it('bevorzugt den Namen und fällt auf die Id zurück', () => {
    const model = parseBpmn(XML);
    expect(nodeLabel(model, 'Task_erfassen')).toBe('Rechnung erfassen');
    expect(nodeLabel(model, 'Unbekannt_1')).toBe('Unbekannt_1');
    expect(nodeLabel(model, null)).toBe('—');
  });
});

describe('mainPath', () => {
  it('folgt vom Start-Ereignis dem ersten Zweig', () => {
    const path = mainPath(parseBpmn(XML));
    expect(path.map((node) => node.id)).toEqual(['Start_1', 'Task_erfassen', 'Gateway_1', 'Task_auto', 'End_1']);
  });

  it('bevorzugt den Zweig, der den gesuchten Knoten enthält', () => {
    const path = mainPath(parseBpmn(XML), 'Task_freigeben');
    expect(path.map((node) => node.id)).toEqual([
      'Start_1',
      'Task_erfassen',
      'Gateway_1',
      'Task_freigeben',
      'End_1',
    ]);
  });

  it('ergänzt einen Knoten, der auf keinem verfolgten Pfad liegt', () => {
    const path = mainPath(parseBpmn(XML), 'Nicht_verbunden');
    expect(path.map((node) => node.id)).not.toContain('Nicht_verbunden');
  });
});

describe('nodeTypeLabel', () => {
  it('übersetzt die gängigen BPMN-Typen', () => {
    expect(nodeTypeLabel('userTask')).toBe('User-Task');
    expect(nodeTypeLabel('exclusiveGateway')).toBe('Exklusives Gateway');
    expect(nodeTypeLabel(undefined)).toBe('Element');
  });
});
