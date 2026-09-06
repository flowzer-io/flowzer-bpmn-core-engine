/**
 * BPMN-XML zu einem einfachen Graphen — und die Positivliste, die entscheidet,
 * was die Gliederung ueberhaupt versteht.
 *
 * Der Leser kennt genau die Elemente und Attribute, die unten aufgezaehlt sind.
 * Alles andere wird als Blocker gemeldet, statt beim Speichern still verloren
 * zu gehen. Das ist die erste von zwei Absicherungen; die zweite ist die
 * Rueckuebersetzungsprobe in `read.ts`.
 */
import type { IoMapping, OutlineIssue } from './model';

export type GraphNodeType =
  | 'startEvent'
  | 'endEvent'
  | 'userTask'
  | 'serviceTask'
  | 'exclusiveGateway'
  | 'parallelGateway';

/** Die Angaben an einem Schritt, die aus den Zeebe-Erweiterungen stammen. */
export interface TaskProperties {
  readonly formKey?: string;
  readonly formId?: string;
  readonly assignee?: string;
  readonly candidateGroups?: string;
  readonly candidateUsers?: string;
  readonly dueDate?: string;
  readonly followUpDate?: string;
  readonly workerType?: string;
  readonly retries?: string;
  readonly inputs: readonly IoMapping[];
  readonly outputs: readonly IoMapping[];
}

export interface GraphNode {
  readonly id: string;
  readonly type: GraphNodeType;
  readonly name?: string;
  /** Nur Tore: die Kennung des Standardflusses. */
  readonly defaultFlowId?: string;
  /** Nur Aufgaben. */
  readonly task?: TaskProperties;
}

export interface GraphFlow {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  readonly name?: string;
  readonly condition?: string;
}

export interface BpmnGraph {
  readonly definitionsId: string;
  readonly targetNamespace?: string;
  /** Wer die Datei zuletzt geschrieben hat — wird unveraendert weitergereicht. */
  readonly exporter?: string;
  readonly exporterVersion?: string;
  readonly processId: string;
  readonly processName?: string;
  readonly nodes: readonly GraphNode[];
  readonly flows: readonly GraphFlow[];
  /** Das Diagramm der Vorlage, unveraendert serialisiert. */
  readonly diagramXml?: string;
}

const BPMN_NS = 'http://www.omg.org/spec/BPMN/20100524/MODEL';
const ZEEBE_NS = 'http://camunda.org/schema/zeebe/1.0';

interface ElementRule {
  /** Namensraum, in dem das Element stehen muss. */
  readonly namespace: string;
  /** Erlaubte Attribute mit ihrem vollstaendigen Namen, also samt Praefix. */
  readonly attributes: readonly string[];
  readonly children: readonly string[];
  /** Elemente, die hoechstens einmal vorkommen duerfen — sonst laese der Leser nur das erste. */
  readonly single?: readonly string[];
}

/** Was die Gliederung versteht. Alles, was hier fehlt, ist ein Blocker. */
const ELEMENT_RULES: Readonly<Record<string, ElementRule>> = {
  definitions: {
    namespace: BPMN_NS,
    attributes: ['id', 'targetNamespace', 'exporter', 'exporterVersion'],
    children: ['process', 'BPMNDiagram'],
  },
  process: {
    namespace: BPMN_NS,
    attributes: ['id', 'name', 'isExecutable'],
    children: [
      'startEvent',
      'endEvent',
      'userTask',
      'serviceTask',
      'exclusiveGateway',
      'parallelGateway',
      'sequenceFlow',
    ],
  },
  startEvent: { namespace: BPMN_NS, attributes: ['id', 'name'], children: ['outgoing'] },
  endEvent: { namespace: BPMN_NS, attributes: ['id', 'name'], children: ['incoming'] },
  userTask: {
    namespace: BPMN_NS,
    attributes: ['id', 'name'],
    children: ['incoming', 'outgoing', 'extensionElements'],
    single: ['extensionElements'],
  },
  serviceTask: {
    namespace: BPMN_NS,
    attributes: ['id', 'name'],
    children: ['incoming', 'outgoing', 'extensionElements'],
    single: ['extensionElements'],
  },
  exclusiveGateway: {
    namespace: BPMN_NS,
    attributes: ['id', 'name', 'default'],
    children: ['incoming', 'outgoing'],
  },
  parallelGateway: { namespace: BPMN_NS, attributes: ['id', 'name'], children: ['incoming', 'outgoing'] },
  sequenceFlow: {
    namespace: BPMN_NS,
    attributes: ['id', 'name', 'sourceRef', 'targetRef'],
    children: ['conditionExpression'],
    single: ['conditionExpression'],
  },
  conditionExpression: { namespace: BPMN_NS, attributes: ['xsi:type'], children: [] },
  extensionElements: {
    namespace: BPMN_NS,
    attributes: [],
    children: ['formDefinition', 'assignmentDefinition', 'taskSchedule', 'taskDefinition', 'ioMapping'],
    single: ['formDefinition', 'assignmentDefinition', 'taskSchedule', 'taskDefinition', 'ioMapping'],
  },
  formDefinition: { namespace: ZEEBE_NS, attributes: ['formKey', 'formId'], children: [] },
  assignmentDefinition: {
    namespace: ZEEBE_NS,
    attributes: ['assignee', 'candidateGroups', 'candidateUsers'],
    children: [],
  },
  taskSchedule: { namespace: ZEEBE_NS, attributes: ['dueDate', 'followUpDate'], children: [] },
  taskDefinition: { namespace: ZEEBE_NS, attributes: ['type', 'retries'], children: [] },
  ioMapping: { namespace: ZEEBE_NS, attributes: [], children: ['input', 'output'] },
  input: { namespace: ZEEBE_NS, attributes: ['source', 'target'], children: [] },
  output: { namespace: ZEEBE_NS, attributes: ['source', 'target'], children: [] },
  incoming: { namespace: BPMN_NS, attributes: [], children: [] },
  outgoing: { namespace: BPMN_NS, attributes: [], children: [] },
};

const NODE_TYPES: readonly GraphNodeType[] = [
  'startEvent',
  'endEvent',
  'userTask',
  'serviceTask',
  'exclusiveGateway',
  'parallelGateway',
];

function attribute(element: Element, name: string): string | undefined {
  const value = element.getAttribute(name);
  return value === null || value === '' ? undefined : value;
}

function children(element: Element, localName: string): Element[] {
  return Array.from(element.children).filter((child) => child.localName === localName);
}

function firstChild(element: Element, localName: string): Element | undefined {
  return children(element, localName)[0];
}

/**
 * Prueft einen Teilbaum gegen die Positivliste. Das Diagramm (`BPMNDiagram`)
 * wird uebersprungen: Es wird unveraendert weitergereicht oder neu berechnet,
 * seine Attribute muessen die Gliederung also nicht interessieren.
 */
function checkAgainstRules(element: Element, issues: OutlineIssue[]): void {
  if (element.localName === 'BPMNDiagram') return;

  const rule = ELEMENT_RULES[element.localName];
  if (!rule) return;

  // Attribute werden ueber ihren vollstaendigen Namen geprueft. Nur den lokalen
  // Namen zu vergleichen liesse `camunda:id` als `id` durchgehen — es waere
  // beim Schreiben weg, und die Rueckuebersetzungsprobe saehe es nie.
  for (const attr of Array.from(element.attributes)) {
    if (attr.name === 'xmlns' || attr.name.startsWith('xmlns:')) continue;
    if (rule.attributes.includes(attr.name)) continue;
    issues.push({
      level: 'blocker',
      elementId: attribute(element, 'id'),
      message: `Die Angabe „${attr.name}" an <${element.nodeName}> bildet die Gliederung nicht ab.`,
    });
  }

  const seen = new Map<string, number>();

  for (const child of Array.from(element.children)) {
    const known = rule.children.includes(child.localName);
    const childRule = ELEMENT_RULES[child.localName];
    const rightNamespace = child.localName === 'BPMNDiagram' || child.namespaceURI === childRule?.namespace;

    if (!known || !rightNamespace) {
      issues.push({
        level: 'blocker',
        elementId: attribute(child, 'id') ?? attribute(element, 'id'),
        message: `<${child.nodeName}> bildet die Gliederung nicht ab — dieser Workflow bleibt dem Diagramm vorbehalten.`,
      });
      continue;
    }

    // Der Leser nimmt von jeder Erweiterung nur die erste. Gaebe es eine zweite,
    // stuende sie auf der Positivliste, kaeme aber nie in den Graphen.
    const count = (seen.get(child.localName) ?? 0) + 1;
    seen.set(child.localName, count);
    if (count === 2 && rule.single?.includes(child.localName)) {
      issues.push({
        level: 'blocker',
        elementId: attribute(element, 'id'),
        message: `<${child.nodeName}> steht mehrfach an <${element.nodeName}>; die Gliederung kennt nur eines davon.`,
      });
    }

    checkAgainstRules(child, issues);
  }
}

/**
 * Erklaerende Kommentare im Prozess gehen beim Schreiben verloren — die
 * Gliederung fuehrt sie nicht mit. Das ist kein Verlust an Ausfuehrungslogik,
 * aber einer an Wissen, und deshalb wird es angesagt statt verschwiegen.
 */
function countComments(element: Element): number {
  let found = 0;
  for (const node of Array.from(element.childNodes)) {
    if (node.nodeType === 8 /* Node.COMMENT_NODE */) found++;
    else if (node.nodeType === 1) found += countComments(node as Element);
  }
  return found;
}

function readIoMappings(task: Element, kind: 'input' | 'output'): IoMapping[] {
  const extensions = firstChild(task, 'extensionElements');
  const mapping = extensions && firstChild(extensions, 'ioMapping');
  if (!mapping) return [];

  return children(mapping, kind)
    .map((entry) => ({ source: entry.getAttribute('source') ?? '', target: entry.getAttribute('target') ?? '' }))
    .filter((entry) => entry.source !== '' || entry.target !== '');
}

function readTaskProperties(task: Element): TaskProperties {
  const extensions = firstChild(task, 'extensionElements');
  const form = extensions && firstChild(extensions, 'formDefinition');
  const assignment = extensions && firstChild(extensions, 'assignmentDefinition');
  const schedule = extensions && firstChild(extensions, 'taskSchedule');
  const definition = extensions && firstChild(extensions, 'taskDefinition');

  return {
    formKey: form && attribute(form, 'formKey'),
    formId: form && attribute(form, 'formId'),
    assignee: assignment && attribute(assignment, 'assignee'),
    candidateGroups: assignment && attribute(assignment, 'candidateGroups'),
    candidateUsers: assignment && attribute(assignment, 'candidateUsers'),
    dueDate: schedule && attribute(schedule, 'dueDate'),
    followUpDate: schedule && attribute(schedule, 'followUpDate'),
    workerType: definition && attribute(definition, 'type'),
    retries: definition && attribute(definition, 'retries'),
    inputs: readIoMappings(task, 'input'),
    outputs: readIoMappings(task, 'output'),
  };
}

function readNodes(process: Element, issues: OutlineIssue[]): GraphNode[] {
  const nodes: GraphNode[] = [];

  for (const element of Array.from(process.children)) {
    const type = NODE_TYPES.find((candidate) => candidate === element.localName);
    if (!type) continue;

    const id = attribute(element, 'id');
    if (!id) {
      issues.push({ level: 'blocker', message: `<${element.nodeName}> hat keine Id.` });
      continue;
    }

    nodes.push({
      id,
      type,
      name: attribute(element, 'name'),
      defaultFlowId: attribute(element, 'default'),
      task: type === 'userTask' || type === 'serviceTask' ? readTaskProperties(element) : undefined,
    });
  }

  return nodes;
}

function readFlows(process: Element, issues: OutlineIssue[]): GraphFlow[] {
  const flows: GraphFlow[] = [];

  for (const element of children(process, 'sequenceFlow')) {
    const id = attribute(element, 'id');
    const source = attribute(element, 'sourceRef');
    const target = attribute(element, 'targetRef');

    if (!id || !source || !target) {
      issues.push({ level: 'blocker', elementId: id, message: 'Ein Sequenzfluss ist unvollständig.' });
      continue;
    }

    const condition = firstChild(element, 'conditionExpression')?.textContent?.trim();
    flows.push({ id, source, target, name: attribute(element, 'name'), condition: condition || undefined });
  }

  return flows;
}

/** Liest das BPMN-XML und meldet alles, was die Gliederung nicht abbildet. */
export function readGraph(xml: string): { graph?: BpmnGraph; issues: OutlineIssue[] } {
  const issues: OutlineIssue[] = [];

  const document = new DOMParser().parseFromString(xml, 'application/xml');
  const root = document.documentElement;
  if (!root || document.getElementsByTagName('parsererror').length > 0 || root.localName !== 'definitions') {
    return { issues: [{ level: 'blocker', message: 'Die Datei ist kein lesbares BPMN-XML.' }] };
  }

  checkAgainstRules(root, issues);

  const processes = children(root, 'process');
  if (processes.length !== 1) {
    issues.push({
      level: 'blocker',
      message: `Die Gliederung zeigt genau einen Prozess je Datei; diese enthält ${processes.length}.`,
    });
    return { issues };
  }

  const [process] = processes;
  const processId = process && attribute(process, 'id');
  const definitionsId = attribute(root, 'id');
  if (!process || !processId || !definitionsId) {
    issues.push({ level: 'blocker', message: 'Prozess oder Definition hat keine Id.' });
    return { issues };
  }

  if (attribute(process, 'isExecutable') !== 'true') {
    issues.push({ level: 'blocker', elementId: processId, message: 'Der Prozess ist nicht ausführbar markiert.' });
  }

  const comments = countComments(process);
  if (comments > 0) {
    issues.push({
      level: 'hinweis',
      elementId: processId,
      message: `Das Modell enthält ${comments} erklärende Kommentare. Beim Speichern aus der Gliederung gehen sie verloren.`,
    });
  }

  const nodes = readNodes(process, issues);
  const flows = readFlows(process, issues);
  if (issues.some((issue) => issue.level === 'blocker')) return { issues };

  const diagram = children(root, 'BPMNDiagram')[0];

  return {
    graph: {
      definitionsId,
      targetNamespace: attribute(root, 'targetNamespace'),
      exporter: attribute(root, 'exporter'),
      exporterVersion: attribute(root, 'exporterVersion'),
      processId,
      processName: attribute(process, 'name'),
      nodes,
      flows,
      diagramXml: diagram ? new XMLSerializer().serializeToString(diagram) : undefined,
    },
    issues,
  };
}

/**
 * Fingerabdruck des Graphen ohne Diagramm. Zwei Graphen mit demselben
 * Fingerabdruck beschreiben denselben Prozess — darauf stuetzen sich sowohl
 * die Rueckuebersetzungsprobe als auch die Entscheidung, ob das vorhandene
 * Diagramm weiterverwendet werden darf.
 */
export function graphSignature(graph: BpmnGraph): string {
  const nodes = [...graph.nodes]
    .sort((a, b) => a.id.localeCompare(b.id))
    .map((node) => ({
      id: node.id,
      type: node.type,
      name: node.name ?? null,
      defaultFlowId: node.defaultFlowId ?? null,
      task: node.task ? normalizeTask(node.task) : null,
    }));

  const flows = [...graph.flows]
    .sort((a, b) => a.id.localeCompare(b.id))
    .map((flow) => ({
      id: flow.id,
      source: flow.source,
      target: flow.target,
      name: flow.name ?? null,
      condition: flow.condition ?? null,
    }));

  return JSON.stringify({
    processId: graph.processId,
    processName: graph.processName ?? null,
    exporter: graph.exporter ?? null,
    exporterVersion: graph.exporterVersion ?? null,
    nodes,
    flows,
  });
}

/**
 * Fingerabdruck allein der Struktur: welche Knoten es gibt und wie sie
 * verbunden sind. Namen, Formulare, Zuweisungen und Fristen zaehlen nicht mit —
 * sie aendern nichts an der Anordnung, und das vorhandene Diagramm darf deshalb
 * unveraendert weiterverwendet werden.
 */
export function structureSignature(graph: BpmnGraph): string {
  const nodes = [...graph.nodes]
    .sort((a, b) => a.id.localeCompare(b.id))
    .map((node) => `${node.id}:${node.type}`);

  const flows = [...graph.flows]
    .sort((a, b) => a.id.localeCompare(b.id))
    .map((flow) => `${flow.id}:${flow.source}>${flow.target}`);

  return JSON.stringify({ processId: graph.processId, nodes, flows });
}

function normalizeTask(task: TaskProperties) {
  return {
    formKey: task.formKey ?? null,
    formId: task.formId ?? null,
    assignee: task.assignee ?? null,
    candidateGroups: task.candidateGroups ?? null,
    candidateUsers: task.candidateUsers ?? null,
    dueDate: task.dueDate ?? null,
    followUpDate: task.followUpDate ?? null,
    workerType: task.workerType ?? null,
    retries: task.retries ?? null,
    inputs: task.inputs.map((entry) => [entry.source, entry.target]),
    outputs: task.outputs.map((entry) => [entry.source, entry.target]),
  };
}
