/**
 * Kleine Hilfen für den Import einer BPMN-Datei — unabhängig davon, aus welchem Werkzeug
 * sie stammt.
 *
 * Die eine Sache, die hier wirklich zählt: **`bpmn:definitions/@id` ist in Flowzer die
 * Kennung der Definition.** Die API liest sie beim Speichern aus dem XML
 * (`DefinitionBusinessLogic.StoreDefinition`). Eine importierte Datei bringt ihre eigene
 * mit; bliebe sie stehen, landete die neue Version unter einer fremden Kennung — im
 * Katalog unsichtbar und beim Veröffentlichen ohne Katalogeintrag.
 */

const BPMN_NAMESPACE = 'http://www.omg.org/spec/BPMN/20100524/MODEL';

/** Der Name des ersten Prozesses, sofern er einen trägt. */
export function readProcessName(xml: string): string | null {
  const document = parse(xml);
  if (!document) return null;

  for (const element of Array.from(document.getElementsByTagName('*'))) {
    if (element.namespaceURI !== BPMN_NAMESPACE || element.localName !== 'process') continue;
    const name = element.getAttribute('name')?.trim();
    if (name) return name;
  }

  return null;
}

/**
 * Ein Vorschlag für den Workflownamen: der Prozessname, sonst der Dateiname ohne Endung.
 * Der Dateiname ist der schlechtere, aber immer vorhandene Anhaltspunkt.
 */
export function suggestWorkflowName(xml: string, fileName: string): string {
  return readProcessName(xml) ?? fileName.replace(/\.(bpmn|xml)$/i, '').trim();
}

/** Setzt die Definitionskennung, unter der Flowzer die Version speichert. */
export function withDefinitionId(xml: string, definitionId: string): string {
  const document = parse(xml);
  if (!document) {
    throw new Error('Die Datei ist kein lesbares BPMN-XML.');
  }

  document.documentElement.setAttribute('id', definitionId);
  return new XMLSerializer().serializeToString(document);
}

function parse(xml: string): Document | null {
  if (typeof xml !== 'string' || xml.trim().length === 0) return null;

  let document: Document;
  try {
    document = new DOMParser().parseFromString(xml, 'application/xml');
  } catch {
    return null;
  }

  if (document.getElementsByTagName('parsererror').length > 0) return null;
  if (document.documentElement?.namespaceURI !== BPMN_NAMESPACE) return null;

  return document;
}
