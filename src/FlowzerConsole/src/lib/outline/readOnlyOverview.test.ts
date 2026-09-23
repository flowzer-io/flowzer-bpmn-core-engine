import { describe, expect, it } from 'vitest';
import { readOnlyOverview } from './readOnlyOverview';

describe('Verlustfreie Übersicht außerhalb der editierbaren Teilmenge', () => {
  // Testzweck: Ein komplexerer Workflow darf die zweite Ansicht nicht leer lassen.
  // Die Übersicht liest echte Knoten und Verbindungen, behauptet aber keine lineare Reihenfolge.
  it('zeigt Terminierungsereignisse und den tatsächlichen nächsten Knoten', () => {
    const xml = `<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"><process id="P">
      <manualTask id="Task" name="Prüfen"/><sequenceFlow id="F" sourceRef="Task" targetRef="End"/>
      <endEvent id="End"><terminateEventDefinition/></endEvent></process></definitions>`;
    const nodes = readOnlyOverview(xml);
    expect(nodes.map(node => node.id)).toEqual(['Task', 'End']);
    expect(nodes[0]?.next).toEqual(['End']);
    expect(nodes[1]?.type).toBe('endEvent');
  });
  // Testzweck: Kaputtes XML ist kein teilweise brauchbares Diagramm und wird nicht
  // als vollständige Übersicht ausgegeben.
  it('weist ungültiges XML zurück', () => {
    expect(readOnlyOverview('<definitions>')).toEqual([]);
  });
});
