/**
 * Die Typen und Lesehilfen für das BPMN-Objektmodell von bpmn-js (moddle).
 *
 * Bewusst nur Lesen: Schreiben braucht die Dienste einer laufenden Modeler-Instanz und
 * gehört deshalb nach `bpmnEditor.ts`. Alles hier ist frei von Seiteneffekten und darf
 * während des Zeichnens aufgerufen werden.
 */

/** Ein Moddle-Element. Die Felder sind bewusst offen — sie hängen am BPMN-Typ. */
export interface ModdleElement {
  $type: string;
  $parent?: ModdleElement;
  [key: string]: unknown;
}

/** Ein Element auf der Zeichenfläche. */
export interface DiagramElement {
  id: string;
  type: string;
  businessObject: ModdleElement;
  source?: DiagramElement;
  target?: DiagramElement;
  outgoing?: DiagramElement[];
}

export interface ElementRegistryLike {
  get: (id: string) => DiagramElement | undefined;
  filter: (predicate: (element: DiagramElement) => boolean) => DiagramElement[];
}

export interface ModelingLike {
  updateProperties: (element: DiagramElement, properties: Record<string, unknown>) => void;
  updateModdleProperties: (
    element: DiagramElement,
    moddleElement: ModdleElement,
    properties: Record<string, unknown>,
  ) => void;
}

export interface BpmnFactoryLike {
  create: (type: string, properties?: Record<string, unknown>) => ModdleElement;
}

/** Die Erweiterungen eines Elements (`bpmn:extensionElements/values`). */
export function extensionValues(businessObject: ModdleElement | undefined): ModdleElement[] {
  const container = businessObject?.extensionElements as ModdleElement | undefined;
  return (container?.values as ModdleElement[] | undefined) ?? [];
}

/** Die erste Erweiterung eines Typs, etwa `zeebe:FormDefinition`. */
export function extension(businessObject: ModdleElement | undefined, type: string): ModdleElement | undefined {
  return extensionValues(businessObject).find((value) => value.$type === type);
}

/** Ein Attribut als Text; alles, was kein String ist, gilt als nicht gesetzt. */
export function text(moddleElement: ModdleElement | undefined, attribute: string): string {
  const value = moddleElement?.[attribute];
  return typeof value === 'string' ? value : '';
}

/**
 * Ein Ja/Nein-Attribut. BPMN-Dateien tragen es je nach Erzeuger als Wahrheitswert oder als
 * Text; moddle liefert deshalb beides.
 */
export function flag(moddleElement: ModdleElement | undefined, attribute: string, fallback = false): boolean {
  const value = moddleElement?.[attribute];
  if (typeof value === 'boolean') return value;
  if (typeof value === 'string') return value === 'true';
  return fallback;
}

/** Die Ereignisdefinition eines Ereignisses, etwa `bpmn:TimerEventDefinition`. */
export function eventDefinition(
  businessObject: ModdleElement | undefined,
  type: string,
): ModdleElement | undefined {
  const definitions = (businessObject?.eventDefinitions as ModdleElement[] | undefined) ?? [];
  return definitions.find((definition) => definition.$type === type);
}

/**
 * Läuft von einem Element aus die Elternkette hoch bis zu einem Typ — so findet ein
 * Element seinen Prozess oder das umgebende `bpmn:Definitions`.
 */
export function enclosing(businessObject: ModdleElement | undefined, type: string): ModdleElement | null {
  let candidate = businessObject;
  while (candidate && candidate.$type !== type) {
    candidate = candidate.$parent;
  }
  return candidate ?? null;
}

/**
 * Der Rumpf eines Ausdrucks (`bpmn:FormalExpression`), etwa einer Bedingung oder einer
 * Zeitangabe.
 */
export function expressionBody(owner: ModdleElement | undefined, property: string): string {
  return text(owner?.[property] as ModdleElement | undefined, 'body');
}
