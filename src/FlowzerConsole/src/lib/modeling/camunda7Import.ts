/**
 * Übersetzt ein Camunda-7-Modell (`camunda:*`, Namensraum
 * `http://camunda.org/schema/1.0/bpmn`) in Flowzers Vokabular (`zeebe:*`).
 *
 * Bewusst ein reines Textmodul: kein React, keine bpmn-js-Instanz, nur `DOMParser` und
 * `XMLSerializer`. Beide gibt es im Browser und in jsdom, damit dieselbe Übersetzung im
 * Test und in der Konsole läuft — und damit sie sich ohne Zeichenfläche prüfen lässt.
 *
 * **Die Zielnamen folgen dem, was Flowzer wirklich liest.** Maßgeblich sind
 * `src/core-engine/ModelParser.cs` (Laufzeit) und
 * `src/FlowzerConsole/src/components/bpmn/elementProperties.ts` (Eigenschaften-Panel).
 * Eine Angabe, die keiner von beiden liest, wäre hier nur Zierde und wird deshalb
 * nicht erfunden, sondern gemeldet.
 *
 * Der Bericht sagt ehrlich, was passiert ist:
 *
 * - `converted` — übernommen, die Angabe wirkt in Flowzer weiter.
 * - `dropped` — entfernt, weil Flowzer dafür keine Entsprechung hat.
 * - `attention` — muss nachgearbeitet werden; hier geht sonst Verhalten verloren
 *   oder die Veröffentlichung wird es später ablehnen.
 */

/** Namensraum der Camunda-7-Erweiterungen. */
export const CAMUNDA7_NAMESPACE = 'http://camunda.org/schema/1.0/bpmn';

/** Namensraum der Erweiterungen, die Flowzer liest. */
export const ZEEBE_NAMESPACE = 'http://camunda.org/schema/zeebe/1.0';

const BPMN_NAMESPACE = 'http://www.omg.org/spec/BPMN/20100524/MODEL';
const XMLNS_NAMESPACE = 'http://www.w3.org/2000/xmlns/';

/** Ein einzelner Befund des Berichts, immer an einem Element festgemacht. */
export interface Finding {
  elementId: string;
  elementName?: string;
  what: string;
  detail?: string;
}

export interface ImportReport {
  converted: Finding[];
  dropped: Finding[];
  attention: Finding[];
}

export interface ConversionResult {
  xml: string;
  report: ImportReport;
}

/**
 * Ob die Datei aus Camunda 7 stammt. Es genügt, dass der Namensraum deklariert ist:
 * Wer ihn deklariert, hat das Modell dort gebaut — und ein Modell ohne eine einzige
 * Erweiterung wird von der Übersetzung ohnehin nur durchgereicht.
 */
export function detectCamunda7(xml: string): boolean {
  const document = parse(xml);
  if (!document) return false;

  for (const element of Array.from(document.getElementsByTagName('*'))) {
    if (element.namespaceURI === CAMUNDA7_NAMESPACE) return true;

    for (const attribute of Array.from(element.attributes)) {
      if (attribute.namespaceURI === CAMUNDA7_NAMESPACE) return true;
      if (attribute.namespaceURI === XMLNS_NAMESPACE && attribute.value === CAMUNDA7_NAMESPACE) return true;
    }
  }

  return false;
}

/**
 * Übersetzt das Modell und berichtet dabei.
 *
 * Ein Modell ohne `camunda:*` läuft unverändert durch: Der Bericht nennt dann nur noch,
 * was Flowzer laut Fähigkeitsvertrag nicht ausführt. Genau deshalb ist derselbe Aufruf
 * auch für gewöhnliche BPMN-Dateien richtig.
 */
export function convertCamunda7(xml: string): ConversionResult {
  const document = parse(xml);
  if (!document) {
    throw new Error('Die Datei ist kein lesbares BPMN-XML.');
  }

  const report: ImportReport = { converted: [], dropped: [], attention: [] };
  const context = createContext(document, report);

  // Vor der ersten Änderung festhalten: Was die Übersetzung neu anlegt, soll sie nicht
  // gleich wieder als Fundstück lesen.
  const elements = Array.from(document.getElementsByTagName('*')).filter(
    (element) => element.namespaceURI === BPMN_NAMESPACE,
  );

  for (const element of elements) {
    convertGenericAttributes(context, element);
    // Erst das Element selbst, dann seine Erweiterungen: So steht der Auftragstyp vor
    // seinen Zuordnungen, wie in einem von Hand geschriebenen Zeebe-Modell auch.
    convertElement(context, element);
    convertExtensionElements(context, element);
    reportCapabilityLimits(context, element);
  }

  removeEmptyExtensionElements(document);
  cleanUpNamespaces(context);

  return { xml: serialize(document), report };
}

// --- Rahmen ---------------------------------------------------------------------------

interface Context {
  document: Document;
  report: ImportReport;
}

function createContext(document: Document, report: ImportReport): Context {
  return { document, report };
}

function parse(xml: string): Document | null {
  if (typeof xml !== 'string' || xml.trim().length === 0) return null;

  let document: Document;
  try {
    document = new DOMParser().parseFromString(xml, 'application/xml');
  } catch {
    return null;
  }

  // Ein Parserfehler landet als Element im Dokument, nicht als Ausnahme.
  if (document.getElementsByTagName('parsererror').length > 0) return null;
  if (document.documentElement?.namespaceURI !== BPMN_NAMESPACE) return null;

  return document;
}

function serialize(document: Document): string {
  return new XMLSerializer().serializeToString(document);
}

/** Die Kennung, unter der ein Befund berichtet wird — notfalls die des Elternelements. */
function identify(element: Element): { elementId: string; elementName?: string } {
  let candidate: Element | null = element;
  while (candidate) {
    const id = candidate.getAttribute('id');
    if (id) {
      const name = candidate.getAttribute('name');
      return name ? { elementId: id, elementName: name } : { elementId: id };
    }
    candidate = candidate.parentElement;
  }
  return { elementId: element.localName };
}

function note(context: Context, group: keyof ImportReport, element: Element, what: string, detail?: string): void {
  const finding: Finding = { ...identify(element), what };
  if (detail) finding.detail = detail;
  context.report[group].push(finding);
}

/**
 * Entfernt ein Element samt der Einrückung davor.
 *
 * Ohne das bliebe für jede entfernte Camunda-Angabe eine leere Zeile stehen — das Modell
 * sähe nach dem Import aus, als hätte jemand darin herumgeschnitten.
 */
function removeNode(element: Element): void {
  const before = element.previousSibling;
  if (before?.nodeType === 3 && (before.textContent ?? '').trim().length === 0) {
    before.parentNode?.removeChild(before);
  }
  element.remove();
}

/** Ein Rahmen ohne Inhalt sagt nichts; er verschwindet mit seinem letzten Kind. */
function removeEmptyExtensionElements(document: Document): void {
  for (const element of Array.from(document.getElementsByTagName('*'))) {
    if (element.namespaceURI !== BPMN_NAMESPACE || element.localName !== 'extensionElements') continue;
    if (element.children.length === 0) removeNode(element);
  }
}

/** Meldet in `dropped` **und** `attention`: Entfernt, und dabei geht Verhalten verloren. */
function noteLostBehaviour(context: Context, element: Element, what: string, detail: string): void {
  note(context, 'dropped', element, what, detail);
  note(context, 'attention', element, what, detail);
}

// --- Camunda-Werkzeug ------------------------------------------------------------------

function camundaAttribute(element: Element, name: string): string | null {
  const value = element.getAttributeNS(CAMUNDA7_NAMESPACE, name);
  return value === null || value.trim().length === 0 ? null : value.trim();
}

function takeCamundaAttribute(element: Element, name: string): string | null {
  const value = camundaAttribute(element, name);
  element.removeAttributeNS(CAMUNDA7_NAMESPACE, name);
  return value;
}

/** Die direkten `camunda:*`-Kinder der eigenen `bpmn:extensionElements`. */
function camundaExtensions(element: Element): Element[] {
  const container = extensionElementsOf(element);
  if (!container) return [];
  return Array.from(container.children).filter((child) => child.namespaceURI === CAMUNDA7_NAMESPACE);
}

function extensionElementsOf(element: Element): Element | null {
  return (
    Array.from(element.children).find(
      (child) => child.namespaceURI === BPMN_NAMESPACE && child.localName === 'extensionElements',
    ) ?? null
  );
}

/**
 * Die `bpmn:extensionElements` des Elements, notfalls neu angelegt.
 *
 * Die Reihenfolge ist nicht beliebig: Nach dem BPMN-Schema steht `documentation` davor.
 * Eine Datei, die dagegen verstößt, lässt sich zwar meist noch lesen, aber sie fällt
 * jedem Prüfwerkzeug auf — und das wäre ein selbst gemachter Fehler.
 */
function ensureExtensionElements(context: Context, element: Element): Element {
  const existing = extensionElementsOf(element);
  if (existing) return existing;

  const created = createBpmnElement(context, element, 'extensionElements');
  const documentation = Array.from(element.children).filter(
    (child) => child.namespaceURI === BPMN_NAMESPACE && child.localName === 'documentation',
  );
  const anchor = documentation.at(-1);
  element.insertBefore(created, anchor ? anchor.nextSibling : element.firstChild);
  return created;
}

function createBpmnElement(context: Context, sibling: Element, localName: string): Element {
  const prefix = sibling.lookupPrefix(BPMN_NAMESPACE);
  return context.document.createElementNS(BPMN_NAMESPACE, prefix ? `${prefix}:${localName}` : localName);
}

function createZeebeElement(context: Context, localName: string, attributes: Record<string, string>): Element {
  const created = context.document.createElementNS(ZEEBE_NAMESPACE, `zeebe:${localName}`);
  for (const [name, value] of Object.entries(attributes)) created.setAttribute(name, value);
  return created;
}

/** Legt eine Zeebe-Erweiterung an oder ergänzt die vorhandene. */
function writeZeebeExtension(
  context: Context,
  owner: Element,
  localName: string,
  attributes: Record<string, string>,
): Element {
  const container = ensureExtensionElements(context, owner);
  const existing = Array.from(container.children).find(
    (child) => child.namespaceURI === ZEEBE_NAMESPACE && child.localName === localName,
  );

  if (existing) {
    for (const [name, value] of Object.entries(attributes)) existing.setAttribute(name, value);
    return existing;
  }

  const created = createZeebeElement(context, localName, attributes);
  container.appendChild(created);
  return created;
}

// --- Ausdrücke: JUEL nach FEEL ---------------------------------------------------------

export interface TranslatedExpression {
  value: string;
  /** Was an der Übersetzung unsicher blieb; leer heißt: eindeutig. */
  notes: string[];
}

const JUEL_EXPRESSION = /^\s*[$#]\{([\s\S]*)\}\s*$/;
const FEEL_KEYWORDS = new Set(['not', 'and', 'or', 'if', 'then', 'else', 'for', 'in', 'return', 'some', 'every']);

/**
 * Übersetzt einen Camunda-7-Ausdruck nach FEEL.
 *
 * `${x}` wird zu `=x`; ein reiner Literaltext bleibt Literal — in Zeebes Schreibweise ist
 * genau das der Unterschied zwischen Ausdruck und Festwert. Gemischter Text („Hallo ${name}“)
 * bleibt unverändert stehen und wird gemeldet: Ihn stillschweigend zu einem FEEL-Ausdruck zu
 * machen, änderte die Bedeutung.
 */
export function translateExpression(raw: string | null | undefined): TranslatedExpression {
  const text = raw ?? '';
  const match = JUEL_EXPRESSION.exec(text);

  if (!match) {
    if (/[$#]\{/.test(text)) {
      return {
        value: text,
        notes: ['Text und Ausdruck sind gemischt; Flowzer wertet das nicht als Ausdruck aus.'],
      };
    }
    return { value: text.trim(), notes: [] };
  }

  const translated = translateJuelBody(match[1] ?? '');
  return { value: `=${translated.value}`, notes: translated.notes };
}

function translateJuelBody(body: string): TranslatedExpression {
  const notes: string[] = [];
  const notEqual = 'NEQ';
  let result = body.trim();

  // Zuerst die Variablenzugriffe: Danach sieht kein Methodenaufruf mehr übrig aus,
  // der in Wahrheit nur ein Variablenname war.
  result = result.replace(/\bexecution\s*\.\s*getVariable\s*\(\s*(['"])([^'"]*)\1\s*\)/g, '$2');

  // `!=` schützen, sonst trifft die Verneinung unten das Ausrufezeichen davor.
  result = result.split('!=').join(notEqual);

  result = result.replace(/\s*&&\s*/g, ' and ');
  result = result.replace(/\s*\|\|\s*/g, ' or ');
  result = result.replace(/\s*==\s*/g, ' = ');

  // `!` nur dort, wo eindeutig ein Bezeichner folgt. Vor einer Klammer oder einem
  // Aufruf bleibt es stehen und wird gemeldet, statt geraten zu werden.
  result = result.replace(/!\s*([A-Za-z_][A-Za-z0-9_.]*)(?!\s*\()/g, 'not($1)');

  if (result.includes('!')) {
    notes.push('Die Verneinung lässt sich nicht eindeutig übersetzen; bitte die Bedingung prüfen.');
  }

  result = result.split(notEqual).join(' != ');
  result = result.replace(/\s{2,}/g, ' ').trim();

  for (const call of result.matchAll(/([A-Za-z_][A-Za-z0-9_.]*)\s*\(/g)) {
    const name = call[1] ?? '';
    if (!FEEL_KEYWORDS.has(name)) {
      notes.push(`Der Ausdruck ruft „${name}()“ auf; FEEL kennt diese Methode nicht.`);
      break;
    }
  }

  return { value: result, notes };
}

/**
 * Der Wert einer Zuordnung oder einer Zuweisung.
 *
 * Ein leerer Ausdruck ist kein Wert; er soll nicht als leeres Attribut im Modell landen.
 */
function translatedOrNull(raw: string | null): TranslatedExpression | null {
  if (raw === null) return null;
  const translated = translateExpression(raw);
  return translated.value.length > 0 ? translated : null;
}

function withNotes(
  context: Context,
  element: Element,
  translated: TranslatedExpression,
  what: string,
): string {
  for (const message of translated.notes) {
    note(context, 'attention', element, what, message);
  }
  return translated.value;
}

// --- Allgemeine Attribute ---------------------------------------------------------------

/** Angaben zur Ausführungssteuerung, für die Flowzer keine Entsprechung hat. */
const DROPPED_ATTRIBUTES: Record<string, string> = {
  asyncBefore: 'Flowzer entscheidet selbst, wo eine Instanz einen Haltepunkt bekommt.',
  asyncAfter: 'Flowzer entscheidet selbst, wo eine Instanz einen Haltepunkt bekommt.',
  exclusive: 'Flowzer kennt keine exklusiven Jobs im Sinne von Camunda 7.',
  jobPriority: 'Flowzer priorisiert Aufträge nicht am Modell.',
  historyTimeToLive: 'Die Aufbewahrung der Historie wird in Flowzer im Betrieb festgelegt.',
  versionTag: 'Flowzer zählt Versionen selbst; ein Etikett am Modell gibt es nicht.',
  candidateStarterGroups: 'Wer starten darf, ergibt sich in Flowzer aus Rollen und Ordnern.',
  candidateStarterUsers: 'Wer starten darf, ergibt sich in Flowzer aus Rollen und Ordnern.',
};

function convertGenericAttributes(context: Context, element: Element): void {
  for (const [name, reason] of Object.entries(DROPPED_ATTRIBUTES)) {
    const value = takeCamundaAttribute(element, name);
    if (value !== null) {
      note(context, 'dropped', element, `camunda:${name} entfernt`, reason);
    }
  }
}

// --- Erweiterungselemente --------------------------------------------------------------

/** Erweiterungen, die Verhalten tragen und in Flowzer ersatzlos wegfallen. */
const LOST_EXTENSIONS: Record<string, string> = {
  executionListener: 'Ausführungs-Listener laufen in Flowzer nicht; der Code muss als Service-Task modelliert werden.',
  taskListener: 'Aufgaben-Listener laufen in Flowzer nicht; die Wirkung muss im Ablauf sichtbar werden.',
  properties: 'Freie Eigenschaften wertet Flowzer nicht aus.',
  failedJobRetryTimeCycle: 'Die Wiederholung steuert in Flowzer der Worker über „retries“ und seine Rückmeldung.',
  connector: 'Konnektoren gibt es in Flowzer nicht; der Aufruf gehört in einen eigenen Worker.',
};

function convertExtensionElements(context: Context, element: Element): void {
  for (const extension of camundaExtensions(element)) {
    const reason = LOST_EXTENSIONS[extension.localName];
    if (reason) {
      noteLostBehaviour(context, element, `camunda:${extension.localName} entfernt`, reason);
      removeNode(extension);
      continue;
    }

    if (extension.localName === 'inputOutput') {
      convertInputOutput(context, element, extension);
    }
  }
}

/**
 * `camunda:inputOutput` wird zu `zeebe:ioMapping`.
 *
 * Camunda 7 nennt im `name` die Zielvariable und im Text den Ausdruck — dieselbe
 * Aufteilung wie Zeebes `target`/`source`. Ein Parameter, der statt Text ein Skript,
 * eine Liste oder eine Abbildung enthält, hat in Flowzer keine Entsprechung: Er wird
 * entfernt und gemeldet, statt ihn auf einen halben Ausdruck einzudampfen.
 */
function convertInputOutput(context: Context, owner: Element, inputOutput: Element): void {
  const inputs: Element[] = [];
  const outputs: Element[] = [];

  for (const parameter of Array.from(inputOutput.children)) {
    if (parameter.namespaceURI !== CAMUNDA7_NAMESPACE) continue;

    const direction =
      parameter.localName === 'inputParameter' ? 'input' : parameter.localName === 'outputParameter' ? 'output' : null;
    if (!direction) continue;

    const target = parameter.getAttribute('name')?.trim() ?? '';
    const structured = Array.from(parameter.children).find((child) => child.namespaceURI === CAMUNDA7_NAMESPACE);

    if (structured) {
      note(
        context,
        'attention',
        owner,
        `camunda:${parameter.localName} „${target}“ entfernt`,
        `Der Parameter enthält „camunda:${structured.localName}“; Flowzer kennt an dieser Stelle nur einen Ausdruck.`,
      );
      continue;
    }

    if (target.length === 0) {
      note(context, 'attention', owner, 'Zuordnung ohne Ziel entfernt', 'Dem Parameter fehlt der Name der Zielvariable.');
      continue;
    }

    const translated = translateExpression(parameter.textContent ?? '');
    if (translated.value.length === 0) {
      note(context, 'attention', owner, `Zuordnung „${target}“ entfernt`, 'Der Parameter hat keinen Wert.');
      continue;
    }

    const source = withNotes(context, owner, translated, `Zuordnung „${target}“`);
    const created = createZeebeElement(context, direction, { source, target });
    (direction === 'input' ? inputs : outputs).push(created);
    note(context, 'converted', owner, `${direction === 'input' ? 'Eingabe' : 'Ausgabe'} „${target}“`, `source="${source}"`);
  }

  removeNode(inputOutput);
  if (inputs.length === 0 && outputs.length === 0) return;

  const mapping = writeZeebeExtension(context, owner, 'ioMapping', {});
  // Zeebe erwartet erst alle Eingaben, dann alle Ausgaben.
  for (const entry of [...inputs, ...outputs]) mapping.appendChild(entry);
}

// --- Elemente ---------------------------------------------------------------------------

function convertElement(context: Context, element: Element): void {
  switch (element.localName) {
    case 'serviceTask':
    case 'sendTask':
      convertServiceTask(context, element);
      return;
    case 'userTask':
      convertUserTask(context, element);
      return;
    case 'startEvent':
      convertStartForm(context, element);
      return;
    case 'sequenceFlow':
      convertCondition(context, element);
      return;
    case 'multiInstanceLoopCharacteristics':
      convertMultiInstance(context, element);
      return;
    case 'businessRuleTask':
      convertBusinessRuleTask(context, element);
      return;
    default:
  }
}

/**
 * Ein Camunda-7-Service-Task wird zum Flowzer-Auftrag.
 *
 * Ein External Task trägt sein Thema schon am Modell — das ist genau Flowzers
 * Auftragstyp und übersetzt sich verlustfrei. Ein Java-Delegate dagegen ist Code im
 * Engine-Prozess; Flowzer hat dort niemanden, der ihn ausführt. Aus dem Klassennamen
 * wird deshalb ein Auftragstyp abgeleitet, und der Bericht sagt, dass dafür erst ein
 * Worker entstehen muss.
 */
function convertServiceTask(context: Context, element: Element): void {
  const type = takeCamundaAttribute(element, 'type');
  const topic = takeCamundaAttribute(element, 'topic');
  const className = takeCamundaAttribute(element, 'class');
  const delegate = takeCamundaAttribute(element, 'delegateExpression');
  const expression = takeCamundaAttribute(element, 'expression');

  if (topic) {
    writeZeebeExtension(context, element, 'taskDefinition', { type: topic });
    note(context, 'converted', element, 'External Task wird Flowzer-Auftrag', `Auftragstyp „${topic}“`);
    if (type && type !== 'external') {
      note(context, 'attention', element, `camunda:type="${type}" entfernt`, 'Übernommen wurde das Thema des External Task.');
    }
    return;
  }

  const derived = deriveJobType(className, delegate, expression);
  if (derived) {
    writeZeebeExtension(context, element, 'taskDefinition', { type: derived.type });
    note(context, 'converted', element, 'Auftragstyp abgeleitet', `Auftragstyp „${derived.type}“`);
    note(
      context,
      'attention',
      element,
      'Java-Delegate wird zum Worker-Auftrag',
      `Aus ${derived.origin} wurde der Auftragstyp „${derived.type}“. Für diesen Typ muss ein Worker bereitstehen.`,
    );
    return;
  }

  if (type) {
    note(
      context,
      'attention',
      element,
      `camunda:type="${type}" entfernt`,
      'Flowzer hat keine eingebaute Aufgabenart; der Schritt braucht einen Auftragstyp und einen Worker.',
    );
  }
}

interface DerivedJobType {
  type: string;
  origin: string;
}

function deriveJobType(
  className: string | null,
  delegate: string | null,
  expression: string | null,
): DerivedJobType | null {
  if (className) {
    const segment = className.split(/[.$]/).filter(Boolean).at(-1) ?? className;
    return { type: segment, origin: `camunda:class="${className}"` };
  }
  if (delegate) {
    return { type: stripJuel(delegate), origin: `camunda:delegateExpression="${delegate}"` };
  }
  if (expression) {
    return { type: stripJuel(expression), origin: `camunda:expression="${expression}"` };
  }
  return null;
}

/** Nur die Hülle `${…}` fällt weg; der Inhalt bleibt, damit der Typ wiedererkennbar ist. */
function stripJuel(value: string): string {
  return JUEL_EXPRESSION.exec(value)?.[1]?.trim() ?? value.trim();
}

function convertUserTask(context: Context, element: Element): void {
  const assignment: Record<string, string> = {};

  for (const name of ['assignee', 'candidateUsers', 'candidateGroups'] as const) {
    const translated = translatedOrNull(takeCamundaAttribute(element, name));
    if (!translated) continue;
    assignment[name] = withNotes(context, element, translated, `camunda:${name}`);
    note(context, 'converted', element, `Zuweisung ${name}`, assignment[name]);
  }

  if (Object.keys(assignment).length > 0) {
    writeZeebeExtension(context, element, 'assignmentDefinition', assignment);
  }

  const schedule: Record<string, string> = {};
  for (const name of ['dueDate', 'followUpDate'] as const) {
    const translated = translatedOrNull(takeCamundaAttribute(element, name));
    if (!translated) continue;
    schedule[name] = withNotes(context, element, translated, `camunda:${name}`);
    note(context, 'converted', element, `Frist ${name}`, schedule[name]);
  }

  if (Object.keys(schedule).length > 0) {
    writeZeebeExtension(context, element, 'taskSchedule', schedule);
  }

  const priority = takeCamundaAttribute(element, 'priority');
  if (priority) {
    note(context, 'dropped', element, 'camunda:priority entfernt', 'Flowzer priorisiert Aufgaben nicht am Modell.');
  }

  convertFormReference(context, element);
}

/** Am reinen Startereignis meint derselbe Verweis das Startformular. */
function convertStartForm(context: Context, element: Element): void {
  convertFormReference(context, element);
}

/**
 * Camunda-7-Formulare kommen nicht mit.
 *
 * Ein `camunda:formKey` zeigt auf ein eingebettetes HTML-Formular, eine Camunda-Form oder
 * eine fremde Anwendung; `camunda:formRef` auf den Formularbestand von Camunda 7. Keines
 * davon kann Flowzer lesen. Der Verweis wird deshalb entfernt — sonst stünde im Modell eine
 * Bindung, die beim Veröffentlichen ins Leere liefe.
 */
function convertFormReference(context: Context, element: Element): void {
  const formKey = takeCamundaAttribute(element, 'formKey');
  const formRef = takeCamundaAttribute(element, 'formRef');
  takeCamundaAttribute(element, 'formRefBinding');
  takeCamundaAttribute(element, 'formRefVersion');

  const reference = formKey ?? formRef;
  if (!reference) return;

  note(
    context,
    'attention',
    element,
    'Formularverweis entfernt',
    `Formulare werden in Flowzer neu erstellt und beim Veröffentlichen gebunden (bisher „${reference}“).`,
  );
}

function convertCondition(context: Context, element: Element): void {
  const condition = Array.from(element.children).find(
    (child) => child.namespaceURI === BPMN_NAMESPACE && child.localName === 'conditionExpression',
  );
  if (!condition) return;

  translateTextContent(context, element, condition, 'Bedingung');
}

/**
 * Übersetzt den Text eines Ausdruckselements an Ort und Stelle.
 *
 * Die Hinweise werden **vor** dem Vergleich gemeldet: Ein gemischter Text bleibt
 * unverändert stehen, und genau dann muss der Bericht ihn trotzdem nennen.
 */
function translateTextContent(context: Context, owner: Element, holder: Element, label: string): void {
  const original = holder.textContent ?? '';
  if (original.trim().length === 0) return;

  const translated = translateExpression(original);
  for (const message of translated.notes) {
    note(context, 'attention', owner, label, message);
  }

  if (translated.value === original.trim()) return;

  holder.textContent = translated.value;
  note(context, 'converted', owner, `${label} nach FEEL übersetzt`, `${original.trim()} → ${translated.value}`);
}

/**
 * Mehrfachausführung: `camunda:collection` und `camunda:elementVariable` werden zu
 * `zeebe:loopCharacteristics`. Camunda 7 nimmt an der Sammlung auch einen bloßen
 * Variablennamen an; in FEEL ist beides ein Ausdruck und bekommt deshalb das führende `=`.
 */
function convertMultiInstance(context: Context, element: Element): void {
  const collection = takeCamundaAttribute(element, 'collection');
  const elementVariable = takeCamundaAttribute(element, 'elementVariable');

  const completion = Array.from(element.children).find(
    (child) => child.namespaceURI === BPMN_NAMESPACE && child.localName === 'completionCondition',
  );
  if (completion) translateTextContent(context, element, completion, 'Abbruchbedingung');

  if (!collection && !elementVariable) return;

  const attributes: Record<string, string> = {};
  if (collection) attributes.inputCollection = asFeelReference(collection);
  if (elementVariable) attributes.inputElement = elementVariable;

  writeZeebeExtension(context, element, 'loopCharacteristics', attributes);
  note(
    context,
    'converted',
    element,
    'Mehrfachausführung übernommen',
    [
      collection ? `inputCollection="${attributes.inputCollection}"` : null,
      elementVariable ? `inputElement="${elementVariable}"` : null,
    ]
      .filter(Boolean)
      .join(', '),
  );

  if (!collection) {
    note(
      context,
      'attention',
      element,
      'Mehrfachausführung ohne Sammlung',
      'Ohne „inputCollection“ weiß Flowzer nicht, worüber der Schritt läuft.',
    );
  }
}

function asFeelReference(value: string): string {
  const translated = translateExpression(value);
  return translated.value.startsWith('=') ? translated.value : `=${translated.value}`;
}

function convertBusinessRuleTask(context: Context, element: Element): void {
  const decision = takeCamundaAttribute(element, 'decisionRef');
  takeCamundaAttribute(element, 'decisionRefBinding');
  takeCamundaAttribute(element, 'decisionRefVersion');
  takeCamundaAttribute(element, 'mapDecisionResult');
  const resultVariable = takeCamundaAttribute(element, 'resultVariable');

  note(
    context,
    'attention',
    element,
    'Entscheidungstabelle wird nicht übernommen',
    `Flowzer führt kein DMN aus. ${
      decision
        ? `Die Entscheidung „${decision}“${resultVariable ? ` (Ergebnis in „${resultVariable}“)` : ''} muss`
        : 'Die Entscheidung muss'
    } als Service-Task oder als Tor nachgebaut werden. ${PUBLISH_HINT}`,
  );
}

// --- Fähigkeitsvertrag -------------------------------------------------------------------

/**
 * Was Flowzer laut [Fähigkeitsvertrag](../../../../docs/BPMN-CAPABILITIES.md) nicht ausführt.
 *
 * Die Veröffentlichung meldet das später ohnehin. Der Bericht sagt es vorab, damit niemand
 * erst nach dem Import merkt, dass der halbe Prozess nicht läuft.
 */
const NOT_EXECUTABLE: Record<string, string> = {
  scriptTask: 'Skript-Aufgaben führt Flowzer nicht aus.',
  callActivity: 'Aufruf-Aktivitäten führt Flowzer nicht aus.',
  inclusiveGateway: 'Das einschließende Tor führt Flowzer nicht aus.',
  complexGateway: 'Das komplexe Tor führt Flowzer nicht aus.',
  eventBasedGateway: 'Das ereignisbasierte Tor führt Flowzer nicht aus.',
  intermediateThrowEvent: 'Zwischenereignisse, die etwas aussenden, führt Flowzer nicht aus.',
};

const NOT_EXECUTABLE_DEFINITIONS: Record<string, string> = {
  errorEventDefinition: 'Fehlerpfade führt Flowzer nicht aus.',
  escalationEventDefinition: 'Eskalationen führt Flowzer nicht aus.',
  compensateEventDefinition: 'Kompensationen führt Flowzer nicht aus.',
};

/** Nachrichten-Ereignisse, die warten — dort fehlt in Camunda 7 der Korrelationsschlüssel. */
const MESSAGE_CATCH_ELEMENTS = new Set(['intermediateCatchEvent', 'boundaryEvent', 'receiveTask']);

const PUBLISH_HINT = 'Die Veröffentlichung lehnt diesen Schritt ab.';

function reportCapabilityLimits(context: Context, element: Element): void {
  const reason = NOT_EXECUTABLE[element.localName] ?? NOT_EXECUTABLE_DEFINITIONS[element.localName];
  if (reason) {
    note(context, 'attention', element, 'Von Flowzer nicht ausgeführt', `${reason} ${PUBLISH_HINT}`);
  }

  if (!MESSAGE_CATCH_ELEMENTS.has(element.localName)) return;

  const waitsForMessage =
    element.localName === 'receiveTask'
      ? element.hasAttribute('messageRef')
      : Array.from(element.children).some(
          (child) => child.namespaceURI === BPMN_NAMESPACE && child.localName === 'messageEventDefinition',
        );

  if (!waitsForMessage) return;

  note(
    context,
    'attention',
    element,
    'Korrelationsschlüssel fehlt',
    'Camunda 7 korreliert Nachrichten über die Laufzeit-API. In Flowzer gehört der Schlüssel ins Modell: ' +
      'am bpmn:message als zeebe:subscription mit correlationKey="=…".',
  );
}

// --- Namensräume -------------------------------------------------------------------------

function cleanUpNamespaces(context: Context): void {
  const root = context.document.documentElement;
  if (root.lookupPrefix(ZEEBE_NAMESPACE) === null) {
    root.setAttributeNS(XMLNS_NAMESPACE, 'xmlns:zeebe', ZEEBE_NAMESPACE);
  }

  const leftovers = collectCamundaLeftovers(context.document);
  if (leftovers.length > 0) {
    for (const leftover of leftovers) {
      note(context, 'attention', leftover.element, `${leftover.label} bleibt stehen`, 'Flowzer wertet die Angabe nicht aus.');
    }
    return;
  }

  for (const element of Array.from(context.document.getElementsByTagName('*'))) {
    for (const attribute of Array.from(element.attributes)) {
      if (attribute.namespaceURI === XMLNS_NAMESPACE && attribute.value === CAMUNDA7_NAMESPACE) {
        element.removeAttributeNS(XMLNS_NAMESPACE, attribute.localName);
      }
    }
  }
}

interface Leftover {
  element: Element;
  label: string;
}

function collectCamundaLeftovers(document: Document): Leftover[] {
  const leftovers: Leftover[] = [];

  for (const element of Array.from(document.getElementsByTagName('*'))) {
    if (element.namespaceURI === CAMUNDA7_NAMESPACE) {
      leftovers.push({ element, label: `camunda:${element.localName}` });
      continue;
    }

    for (const attribute of Array.from(element.attributes)) {
      if (attribute.namespaceURI === CAMUNDA7_NAMESPACE) {
        leftovers.push({ element, label: `camunda:${attribute.localName}` });
      }
    }
  }

  return leftovers;
}

/** Ob der Bericht überhaupt etwas zu sagen hat. */
export function isEmptyReport(report: ImportReport): boolean {
  return report.converted.length === 0 && report.dropped.length === 0 && report.attention.length === 0;
}
