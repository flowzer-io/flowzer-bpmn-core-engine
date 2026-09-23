const BPMN = 'http://www.omg.org/spec/BPMN/20100524/MODEL';
const DI = 'http://www.omg.org/spec/BPMN/20100524/DI';

/**
 * Alte oder per API erstellte BPMN-Dateien besitzen mitunter keine Koordinaten.
 * Nur dann wird automatisch angeordnet. Aus dem Layout-Ergebnis übernehmen wir
 * ausschließlich DI: der Layouter kennt weder alle Erweiterungen noch deren Semantik.
 * Der ursprüngliche Prozess bleibt deshalb einschließlich fremder Erweiterungen erhalten.
 */
export async function ensureDiagram(xml: string): Promise<string> {
  const document = new DOMParser().parseFromString(xml, 'application/xml');
  if (document.querySelector('parsererror') || document.getElementsByTagNameNS(DI, 'BPMNDiagram').length) return xml;
  const processes = document.getElementsByTagNameNS(BPMN, 'process');
  if (processes.length === 0) return xml;
  if (processes.length !== 1 || document.getElementsByTagNameNS(BPMN, 'collaboration').length) {
    throw new Error('Für dieses Prozessmodell fehlt die Diagrammanordnung. Bitte die BPMN-Datei mit Diagrammkoordinaten importieren.');
  }
  // Automatische Anordnung ist ein Fallback für kleine Bestandsmodelle. Sehr große
  // Graphen sollen nicht durch synchrone Layoutberechnungen den Browser blockieren.
  if (xml.length > 1_000_000 || processes[0]!.getElementsByTagName('*').length > 500) {
    throw new Error('Dieses große Modell benötigt gespeicherte Diagrammkoordinaten. Bitte eine angeordnete BPMN-Datei importieren.');
  }
  const { layoutProcess } = await import('bpmn-auto-layout');
  const layoutInput = document.cloneNode(true) as Document;
  // Erweiterungen sind für Koordinaten irrelevant; das Original wird nicht verändert.
  Array.from(layoutInput.getElementsByTagNameNS(BPMN, 'extensionElements')).forEach((element) => element.remove());
  const layout = new DOMParser().parseFromString(
    await layoutProcess(new XMLSerializer().serializeToString(layoutInput)), 'application/xml');
  for (const diagram of Array.from(layout.getElementsByTagNameNS(DI, 'BPMNDiagram'))) {
    const copy = document.importNode(diagram, true);
    // DI-Prefixe können im Ergebnis nur am Root deklariert sein. Lokal deklarieren,
    // damit auch fachliche QName-Attribute bei anderen Original-Prefixen gültig bleiben.
    for (const attribute of Array.from(layout.documentElement.attributes)) {
      if (attribute.namespaceURI === 'http://www.w3.org/2000/xmlns/') {
        copy.setAttributeNS(attribute.namespaceURI, attribute.name, attribute.value);
      }
    }
    document.documentElement.appendChild(copy);
  }
  return new XMLSerializer().serializeToString(document);
}
