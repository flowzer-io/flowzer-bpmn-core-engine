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
  readonly processId: string;
  readonly processName?: string;
  readonly nodes: readonly GraphNode[];
  readonly flows: readonly GraphFlow[];
  /** Das Diagramm der Vorlage, unveraendert serialisiert. */
  readonly diagramXml?: string;
}

interface ElementRule {
  readonly attributes: readonly string[];
  readonly children: readonly string[];
}

/** Was die Gliederung versteht. Alles, was hier fehlt, ist ein Blocker. */
const ELEMENT_RULES: Readonly<Record<string, ElementRule>> = {
  definitions: {
    attributes: ['id', 'targetNamespace', 'exporter', 'exporterVersion'],
    children: ['process', 'BPMNDiagram'],
  },
  process: {
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
  startEvent: { attributes: ['id', 'name'], children: ['outgoing'] },
  endEvent: { attributes: ['id', 'name'], children: ['incoming'] },
  userTask: { attributes: ['id', 'name'], children: ['incoming', 'outgoing', 'extensionElements'] },
  serviceTask: { attributes: ['id', 'name'], children: ['incoming', 'outgoing', 'extensionElements'] },
  exclusiveGateway: { attributes: ['id', 'name', 'default'], children: ['incoming', 'outgoing'] },
  parallelGateway: { attributes: ['id', 'name'], children: ['incoming', 'outgoing'] },
  sequenceFlow: {
    attributes: ['id', 'name', 'sourceRef', 'targetRef'],
    children: ['conditionExpression'],
  },
  conditionExpression: { attributes: ['type'], children: [] },
  extensionElements: {
    attributes: [],
    children: ['formDefinition', 'assignmentDefinition', 'taskSchedule', 'taskDefinition', 'ioMapping'],
  },
  formDefinition: { attributes: ['formKey', 'formId'], children: [] },
  assignmentDefinition: {
    attributes: ['assignee', 'candidateGroups', 'candidateUsers'],
    children: [],
  },
  taskSchedule: { attributes: ['dueDate', 'followUpDate'], children: [] },
  taskDefinition: { attributes: ['type', 'retries'], children: [] },
  ioMapping: { attributes: [], children: ['input', 'output'] },
  input: { attributes: ['source', 'target'], children: [] },
  output: { attributes: ['source', 'target'], children: [] },
  incoming: { attributes: [], children: [] },
  outgoing: { attributes: [], children: [] },
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

  for (const attr of Array.from(element.attributes)) {
    if (attr.name === 'xmlns' || attr.name.startsWith('xmlns:')) continue;
    if (rule.attributes.includes(attr.localName)) continue;
    issues.push({
      level: 'blocker',
      elementId: attribute(element, 'id'),
      message: `Die Angabe „${attr.name}" an <${element.nodeName}> bildet die Gliederung nicht ab.`,
    });
  }

  for (const child of Array.from(element.children)) {
    if (rule.children.includes(child.localName)) {
      checkAgainstRules(child, issues);
      continue;
    }
    issues.push({
      level: 'blocker',
      elementId: attribute(child, 'id') ?? attribute(element, 'id'),
      message: `<${child.nodeName}> bildet die Gliederung nicht ab — dieser Workflow bleibt dem Diagramm vorbehalten.`,
    });
  }
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

  const nodes = readNodes(process, issues);
  const flows = readFlows(process, issues);
  if (issues.some((issue) => issue.level === 'blocker')) return { issues };

  const diagram = children(root, 'BPMNDiagram')[0];

  return {
    graph: {
      definitionsId,
      targetNamespace: attribute(root, 'targetNamespace'),
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

  return JSON.stringify({ processId: graph.processId, processName: graph.processName ?? null, nodes, flows });
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
