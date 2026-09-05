/**
 * Leichtgewichtiges Auslesen von BPMN-XML über den DOM-Parser des Browsers.
 *
 * Für Kennzahlen wie „wie viele Schritte hat der Prozess?" oder „wie heißt dieses
 * Element?" wäre es unverhältnismäßig, bpmn-js zu laden. Für die Darstellung des
 * Diagramms selbst bleibt bpmn-js zuständig.
 */

/** BPMN-Elemente, die als eigenständiger Prozessschritt zählen. */
const FLOW_NODE_TAGS = new Set([
  'task',
  'userTask',
  'serviceTask',
  'scriptTask',
  'businessRuleTask',
  'manualTask',
  'receiveTask',
  'sendTask',
  'callActivity',
  'subProcess',
  'startEvent',
  'endEvent',
  'intermediateCatchEvent',
  'intermediateThrowEvent',
  'boundaryEvent',
  'exclusiveGateway',
  'parallelGateway',
  'inclusiveGateway',
  'eventBasedGateway',
  'complexGateway',
]);

export interface BpmnFlowNode {
  id: string;
  name: string | null;
  type: string;
}

export interface BpmnSequenceFlow {
  id: string;
  sourceRef: string;
  targetRef: string;
}

export interface BpmnModelSummary {
  nodes: BpmnFlowNode[];
  nodeById: Map<string, BpmnFlowNode>;
  flows: BpmnSequenceFlow[];
  /** Anzahl der Schritte ohne Start-/End-Ereignisse — die fachlich "echten" Schritte. */
  stepCount: number;
}

const EMPTY_SUMMARY: BpmnModelSummary = {
  nodes: [],
  nodeById: new Map(),
  flows: [],
  stepCount: 0,
};

export function parseBpmn(xml: string | undefined | null): BpmnModelSummary {
  if (!xml) return EMPTY_SUMMARY;

  let document: Document;
  try {
    document = new DOMParser().parseFromString(xml, 'application/xml');
  } catch {
    return EMPTY_SUMMARY;
  }

  if (document.querySelector('parsererror')) return EMPTY_SUMMARY;

  const nodes: BpmnFlowNode[] = [];
  const flows: BpmnSequenceFlow[] = [];

  for (const element of Array.from(document.getElementsByTagName('*'))) {
    const tag = element.localName;

    if (tag === 'sequenceFlow') {
      const id = element.getAttribute('id');
      const sourceRef = element.getAttribute('sourceRef');
      const targetRef = element.getAttribute('targetRef');
      if (id && sourceRef && targetRef) flows.push({ id, sourceRef, targetRef });
      continue;
    }

    if (!FLOW_NODE_TAGS.has(tag)) continue;

    const id = element.getAttribute('id');
    if (!id) continue;

    nodes.push({ id, name: element.getAttribute('name'), type: tag });
  }

  const nodeById = new Map(nodes.map((node) => [node.id, node]));
  const stepCount = nodes.filter((node) => !node.type.endsWith('Event') || node.type === 'boundaryEvent').length;

  return { nodes, nodeById, flows, stepCount: stepCount || nodes.length };
}

/**
 * Repräsentativer Hauptpfad des Prozesses: vom Start-Ereignis entlang der
 * Sequenzflüsse. An Verzweigungen wird der erste ausgehende Fluss gewählt.
 *
 * Das ist bewusst eine Näherung für die Schrittanzeige („Wo stehe ich?") und
 * keine Ausführungssemantik — welcher Zweig tatsächlich läuft, entscheidet die
 * Engine. Enthält der Pfad den aktuellen Knoten nicht, wird er ergänzt.
 */
export function mainPath(summary: BpmnModelSummary, includeNodeId?: string | null): BpmnFlowNode[] {
  const start = summary.nodes.find((node) => node.type === 'startEvent') ?? summary.nodes[0];
  if (!start) return [];

  const outgoing = new Map<string, string[]>();
  for (const flow of summary.flows) {
    const targets = outgoing.get(flow.sourceRef);
    if (targets) targets.push(flow.targetRef);
    else outgoing.set(flow.sourceRef, [flow.targetRef]);
  }

  const path: BpmnFlowNode[] = [];
  const visited = new Set<string>();
  let currentId: string | undefined = start.id;

  while (currentId && !visited.has(currentId)) {
    visited.add(currentId);
    const node = summary.nodeById.get(currentId);
    if (node) path.push(node);

    const targets = outgoing.get(currentId);
    // An einer Verzweigung wird bevorzugt der Zweig verfolgt, der den gesuchten
    // Knoten enthält — sonst der erste ausgehende Fluss.
    currentId = targets?.find((target) => target === includeNodeId) ?? targets?.[0];
  }

  if (includeNodeId && !visited.has(includeNodeId)) {
    const node = summary.nodeById.get(includeNodeId);
    if (node) path.push(node);
  }

  return path;
}

/** Lesbarer Name eines Elements; fällt auf die technische Id zurück. */
export function nodeLabel(summary: BpmnModelSummary, nodeId: string | null | undefined): string {
  if (!nodeId) return '—';
  const node = summary.nodeById.get(nodeId);
  return node?.name?.trim() || nodeId;
}

const TYPE_LABELS: Record<string, string> = {
  userTask: 'User-Task',
  serviceTask: 'Service-Task',
  scriptTask: 'Script-Task',
  businessRuleTask: 'Business-Rule-Task',
  manualTask: 'Manuelle Aufgabe',
  receiveTask: 'Empfangs-Task',
  sendTask: 'Sende-Task',
  callActivity: 'Aufruf-Aktivität',
  subProcess: 'Teilprozess',
  task: 'Aufgabe',
  startEvent: 'Start-Ereignis',
  endEvent: 'End-Ereignis',
  intermediateCatchEvent: 'Zwischenereignis (empfangend)',
  intermediateThrowEvent: 'Zwischenereignis (sendend)',
  boundaryEvent: 'Boundary-Ereignis',
  exclusiveGateway: 'Exklusives Gateway',
  parallelGateway: 'Paralleles Gateway',
  inclusiveGateway: 'Inklusives Gateway',
  eventBasedGateway: 'Ereignisbasiertes Gateway',
  complexGateway: 'Komplexes Gateway',
};

export function nodeTypeLabel(type: string | undefined): string {
  return type ? (TYPE_LABELS[type] ?? type) : 'Element';
}

const TYPE_ICONS: Record<string, string> = {
  userTask: 'person',
  serviceTask: 'settings',
  scriptTask: 'code',
  businessRuleTask: 'rule',
  manualTask: 'back_hand',
  receiveTask: 'mail',
  sendTask: 'send',
  callActivity: 'call_merge',
  subProcess: 'account_tree',
  task: 'crop_square',
  startEvent: 'play_circle',
  endEvent: 'stop_circle',
  intermediateCatchEvent: 'download',
  intermediateThrowEvent: 'upload',
  boundaryEvent: 'adjust',
  exclusiveGateway: 'call_split',
  parallelGateway: 'add',
  inclusiveGateway: 'radio_button_checked',
  eventBasedGateway: 'bolt',
  complexGateway: 'hub',
};

export function nodeTypeIcon(type: string | undefined): string {
  return type ? (TYPE_ICONS[type] ?? 'crop_square') : 'crop_square';
}
