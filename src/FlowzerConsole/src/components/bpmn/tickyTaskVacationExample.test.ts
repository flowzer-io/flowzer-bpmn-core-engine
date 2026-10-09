import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { BpmnModdle } from 'bpmn-moddle';
import zeebeModdle from 'zeebe-bpmn-moddle/resources/zeebe.json';
import { describe, expect, it } from 'vitest';

import { FLOWZER_MODDLE } from './flowzerModdle';

interface Element {
  id: string;
  $type: string;
  flowElements?: Element[];
  extensionElements?: { values: Array<Record<string, unknown>> };
}

const rawXml = readFileSync(resolve(process.cwd(), '../../examples/tickytask-urlaub/urlaubsantrag.bpmn'), 'utf8');
const configuredXml = rawXml.replace('__DEMO_PERSONNEL_GROUP_ID__', '22000000-0000-4000-8000-000000000001');
const flatten = (elements: Element[]): Element[] => elements.flatMap(element => [element, ...flatten(element.flowElements ?? [])]);

// Testzweck: Das tatsächlich ausgelieferte Human-Task-Modell bleibt im normalen Editor
// lesbar und verliert beim XML-Roundtrip weder Zuweisung noch Korrektur-/Unterbrechungspfad.
describe('TT-Demo-Urlaubsprozess', () => {
  it('erhält die eingebettete Prüfrunde und ihre echten Directory-Bindungen im Moddle-Roundtrip', async () => {
    const moddle = new BpmnModdle({ zeebe: zeebeModdle, flowzer: FLOWZER_MODDLE });
    const parsed = await moddle.fromXML(configuredXml);
    expect(parsed.warnings).toEqual([]);
    const serialized = await moddle.toXML(parsed.rootElement, { format: true });
    const reread = await moddle.fromXML(serialized.xml);
    expect(reread.warnings).toEqual([]);
    const roots = (reread.rootElement as unknown as { rootElements: Element[] }).rootElements;
    const process = roots.find(element => element.$type === 'bpmn:Process');
    const elements = flatten(process?.flowElements ?? []);
    expect(elements.filter(element => element.$type === 'bpmn:UserTask').map(element => element.id).sort())
      .toEqual(['Task_Correction', 'Task_Personnel', 'Task_Substitute', 'Task_Supervisor']);
    const assignment = (id: string) => elements.find(element => element.id === id)?.extensionElements?.values
      .find(extension => extension.$type === 'flowzer:TaskAssignment');
    expect(assignment('Task_Supervisor')).toMatchObject({ mode: 'directory', assigneeSource: 'variable:demoSupervisor' });
    expect(assignment('Task_Substitute')).toMatchObject({ mode: 'directory', assigneeSource: 'variable:vertretung' });
    expect(assignment('Task_Correction')).toMatchObject({ mode: 'directory', assigneeSource: 'initiator' });
    expect(assignment('Task_Personnel')).toMatchObject({ mode: 'directory', candidateGroupIds: '22000000-0000-4000-8000-000000000001' });
    expect(elements.filter(element => element.$type === 'bpmn:BoundaryEvent').map(element => element.id).sort())
      .toEqual(['Boundary_Reject', 'Boundary_Return']);
    expect(elements.some(element => ['bpmn:ServiceTask', 'bpmn:SendTask', 'bpmn:CallActivity'].includes(element.$type))).toBe(false);
    expect(serialized.xml).not.toContain('script');
  });

  // Testzweck: Die Beispieldatei enthält echte Diagrammgeometrie für jeden Knoten und
  // Sequenzfluss; ein reiner XML-/API-Erfolg ist damit nicht die behauptete visuelle Abnahme.
  it('liefert Diagrammelemente für alle modellierten Knoten und Sequenzflüsse', async () => {
    const moddle = new BpmnModdle({ zeebe: zeebeModdle, flowzer: FLOWZER_MODDLE });
    const parsed = await moddle.fromXML(configuredXml);
    const root = parsed.rootElement as unknown as {
      rootElements: Element[];
      diagrams: Array<{ plane: { planeElement: Array<{ bpmnElement: Element }> } }>;
    };
    const nodes = flatten(root.rootElements.find(element => element.$type === 'bpmn:Process')?.flowElements ?? []);
    const represented = root.diagrams[0]?.plane.planeElement.map(element => element.bpmnElement.id) ?? [];
    expect(represented.sort()).toEqual(nodes.map(element => element.id).sort());
  });
});
