import { describe, expect, it } from 'vitest';
import { ensureDiagram } from './ensureDiagram';

const model = `<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:custom="urn:custom" id="Definition" targetNamespace="urn:test">
  <bpmn:process id="Process" isExecutable="true"><bpmn:startEvent id="Start"/><bpmn:sequenceFlow id="Flow" sourceRef="Start" targetRef="Task"/><bpmn:userTask id="Task"><bpmn:extensionElements><custom:settings value="preserve-me"/></bpmn:extensionElements></bpmn:userTask></bpmn:process>
</bpmn:definitions>`;
const parse = (xml: string) => new DOMParser().parseFromString(xml, 'application/xml');
const di = 'http://www.omg.org/spec/BPMN/20100524/DI';

describe('Automatische Diagrammansicht ohne DI', () => {
  // Testzweck: Alte/API-erzeugte Modelle erhalten sichtbare Knoten, ohne dass
  // unbekannte Erweiterungen oder fachliche Prozessdaten verloren gehen.
  it('ergänzt ausschließlich Diagrammdaten und bewahrt fremde Erweiterungen', async () => {
    const result = parse(await ensureDiagram(model));
    expect(result.getElementsByTagNameNS(di, 'BPMNShape').length).toBe(2);
    const originalProcess = parse(model).getElementsByTagNameNS('*', 'process')[0]!;
    expect(result.getElementsByTagNameNS('*', 'process')[0]!.isEqualNode(originalProcess)).toBe(true);
    expect(result.getElementsByTagNameNS('urn:custom', 'settings')[0]?.getAttribute('value')).toBe('preserve-me');
  });

  // Testzweck: Benutzerdefinierte Positionen werden bei jedem Laden exakt beibehalten.
  it('ändert ein vorhandenes Diagramm nicht und arbeitet idempotent', async () => {
    const first = await ensureDiagram(model);
    expect(await ensureDiagram(first)).toBe(first);
  });

  // Testzweck: Ungültiges XML wird nicht automatisch fachlich repariert oder überschrieben.
  it('überlässt ungültiges XML der normalen Importdiagnose', async () => {
    expect(await ensureDiagram('<broken>')).toBe('<broken>');
  });
});
