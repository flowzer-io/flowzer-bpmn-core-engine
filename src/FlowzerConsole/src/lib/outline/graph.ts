/**
 * BPMN-XML zu einem einfachen Graphen — und die Positivliste, die entscheidet,
 * was die Gliederung ueberhaupt versteht.
 *
 * Der Leser kennt genau die Elemente und Attribute, die unten aufgezaehlt sind.
 * Alles andere wird als Blocker gemeldet, statt beim Speichern still verloren
 * zu gehen. Das ist die erste von zwei Absicherungen; die zweite ist die
 * Rueckuebersetzungsprobe in `read.ts`.
 */
import type { AiTaskConfiguration, ServiceTaskMode } from '@/lib/aiTaskContract';
import type { IoMapping, OutlineIssue } from './model';
import { AI_WORKER_TYPE } from '@/lib/aiTaskContract';

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
  readonly assignmentMode?: 'text' | 'directory';
  readonly directoryAssigneeId?: string;
  readonly directoryCandidateUserIds?: readonly string[];
  readonly directoryCandidateGroupIds?: readonly string[];
  readonly dueDate?: string;
  readonly followUpDate?: string;
  readonly workerType?: string;
  readonly retries?: string;
  readonly serviceTaskMode?: ServiceTaskMode;
  readonly aiTask?: AiTaskConfiguration;
  readonly inputs: readonly IoMapping[];
  readonly outputs: readonly IoMapping[];
}

/**
 * Das Startformular am reinen Startereignis. Es ist freiwillig: Steht es da,
 * fuellt es beim Starten die Variablen des Vorgangs; fehlt es, beginnt der
 * Ablauf ohne Eingabe.
 */
export interface StartFormProperties {
  readonly formKey?: string;
  readonly formId?: string;
}

export interface GraphNode {
  readonly id: string;
  readonly type: GraphNodeType;
  readonly name?: string;
  /** Nur Tore: die Kennung des Standardflusses. */
  readonly defaultFlowId?: string;
  /** Nur Aufgaben. */
  readonly task?: TaskProperties;
  /** Nur das Startereignis: das freiwillige Startformular. */
  readonly startForm?: StartFormProperties;
}

export interface GraphFlow {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  readonly name?: string;
  readonly condition?: string;
}

/** Ein Formular, das im Workflow selbst liegt (`zeebe:userTaskForm` am Prozess). */
export interface EmbeddedForm {
  readonly id: string;
  readonly schema: string;
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
  /** Formulare im Workflow selbst. Die Gliederung zeigt sie, bearbeitet sie aber nicht. */
  readonly embeddedForms: readonly EmbeddedForm[];
  /** Das Diagramm der Vorlage, unveraendert serialisiert. */
  readonly diagramXml?: string;
}

const BPMN_NS = 'http://www.omg.org/spec/BPMN/20100524/MODEL';
const ZEEBE_NS = 'http://camunda.org/schema/zeebe/1.0';
const FLOWZER_NS = 'https://flowzer.io/schema/bpmn/1.0';

interface ElementRule {
  /** Namensraum, in dem das Element stehen muss. */
  readonly namespace: string;
  /**
   * Kinder, deren Regel unter einem anderen Schluessel steht. Die
   * `extensionElements` des Prozesses tragen anderes als die einer Aufgabe.
   */
  readonly childRules?: Readonly<Record<string, string>>;
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
    childRules: { extensionElements: 'processExtensionElements' },
    single: ['extensionElements'],
    children: [
      'extensionElements',
      'startEvent',
      'endEvent',
      'userTask',
      'serviceTask',
      'exclusiveGateway',
      'parallelGateway',
      'sequenceFlow',
    ],
  },
  startEvent: {
    namespace: BPMN_NS,
    attributes: ['id', 'name'],
    childRules: { extensionElements: 'startExtensionElements' },
    single: ['extensionElements'],
    children: ['extensionElements', 'outgoing'],
  },
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
  // Formulare, die der Workflow selbst mitbringt. Sie stehen ausschliesslich in
  // den `extensionElements` des Prozesses; an einer Aufgabe waere dasselbe
  // Element etwas anderes und wuerde beim Schreiben verschwinden.
  processExtensionElements: { namespace: BPMN_NS, attributes: [], children: ['userTaskForm'] },
  // Am Startereignis steht ausschliesslich das Startformular. Alles andere —
  // eine Zuweisung, eine Frist, eine Zuordnung — hat dort keine Wirkung und
  // ginge beim Schreiben verloren; deshalb eine eigene, engere Regel.
  startExtensionElements: {
    namespace: BPMN_NS,
    attributes: [],
    children: ['formDefinition'],
    single: ['formDefinition'],
  },
  userTaskForm: { namespace: ZEEBE_NS, attributes: ['id'], children: [] },
  extensionElements: {
    namespace: BPMN_NS,
    attributes: [],
    children: ['formDefinition', 'assignmentDefinition', 'taskAssignment', 'taskSchedule', 'taskDefinition', 'aiTask', 'ioMapping'],
    single: ['formDefinition', 'assignmentDefinition', 'taskAssignment', 'taskSchedule', 'taskDefinition', 'aiTask', 'ioMapping'],
  },
  formDefinition: { namespace: ZEEBE_NS, attributes: ['formKey', 'formId'], children: [] },
  assignmentDefinition: {
    namespace: ZEEBE_NS,
    attributes: ['assignee', 'candidateGroups', 'candidateUsers'],
    children: [],
  },
  taskAssignment: {
    namespace: FLOWZER_NS,
    attributes: ['mode', 'assigneeId', 'candidateUserIds', 'candidateGroupIds'],
    children: [],
  },
  taskSchedule: { namespace: ZEEBE_NS, attributes: ['dueDate', 'followUpDate'], children: [] },
  taskDefinition: { namespace: ZEEBE_NS, attributes: ['type', 'retries'], children: [] },
  aiTask: {
    namespace: FLOWZER_NS,
    attributes: [
      'contractVersion',
      'connectionId',
      'model',
      'instructionVersion',
      'maxInputTokens',
      'maxOutputTokens',
      'timeoutSeconds',
    ],
    children: ['instruction', 'resultSchema', 'tool'],
    single: ['instruction', 'resultSchema'],
  },
  tool: { namespace: FLOWZER_NS, attributes: ['id', 'version', 'approval'], children: [] },
  instruction: { namespace: FLOWZER_NS, attributes: [], children: [] },
  resultSchema: { namespace: FLOWZER_NS, attributes: [], children: [] },
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
function checkAgainstRules(element: Element, issues: OutlineIssue[], ruleName = element.localName): void {
  if (element.localName === 'BPMNDiagram') return;

  const rule = ELEMENT_RULES[ruleName];
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
    const childRuleName = rule.childRules?.[child.localName] ?? child.localName;
    const childRule = ELEMENT_RULES[childRuleName];
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

    checkAgainstRules(child, issues, childRuleName);
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
  const flowzerAssignment = extensions && firstChild(extensions, 'taskAssignment');
  const flowzerMode = flowzerAssignment && attribute(flowzerAssignment, 'mode');
  const schedule = extensions && firstChild(extensions, 'taskSchedule');
  const definition = extensions && firstChild(extensions, 'taskDefinition');
  const aiTask = extensions && firstChild(extensions, 'aiTask');

  return {
    formKey: form && attribute(form, 'formKey'),
    formId: form && attribute(form, 'formId'),
    assignee: assignment && attribute(assignment, 'assignee'),
    candidateGroups: assignment && attribute(assignment, 'candidateGroups'),
    candidateUsers: assignment && attribute(assignment, 'candidateUsers'),
    assignmentMode: flowzerMode === 'directory' || flowzerMode === 'text' ? flowzerMode : undefined,
    directoryAssigneeId: flowzerAssignment && attribute(flowzerAssignment, 'assigneeId'),
    directoryCandidateUserIds: commaSeparated(flowzerAssignment && attribute(flowzerAssignment, 'candidateUserIds')),
    directoryCandidateGroupIds: commaSeparated(flowzerAssignment && attribute(flowzerAssignment, 'candidateGroupIds')),
    dueDate: schedule && attribute(schedule, 'dueDate'),
    followUpDate: schedule && attribute(schedule, 'followUpDate'),
    workerType: definition && attribute(definition, 'type'),
    retries: definition && attribute(definition, 'retries'),
    serviceTaskMode:
      task.localName === 'serviceTask'
        ? aiTask || (definition && attribute(definition, 'type') === AI_WORKER_TYPE)
          ? 'ai'
          : 'worker'
        : undefined,
    aiTask: aiTask
      ? {
          contractVersion: attribute(aiTask, 'contractVersion') ?? '',
          connectionId: attribute(aiTask, 'connectionId') ?? '',
          model: attribute(aiTask, 'model') ?? '',
          instructionVersion: attribute(aiTask, 'instructionVersion') ?? '',
          instruction: firstChild(aiTask, 'instruction')?.textContent?.trim() ?? '',
          resultSchema: firstChild(aiTask, 'resultSchema')?.textContent?.trim() ?? '',
          maxInputTokens: attribute(aiTask, 'maxInputTokens') ?? '',
          maxOutputTokens: attribute(aiTask, 'maxOutputTokens') ?? '',
          timeoutSeconds: attribute(aiTask, 'timeoutSeconds') ?? '',
          tools: children(aiTask, 'tool').map((tool) => ({
            toolId: attribute(tool, 'id') ?? '',
            toolVersion: attribute(tool, 'version') ?? '',
            approval: (attribute(tool, 'approval') ?? 'human') as AiTaskConfiguration['tools'][number]['approval'],
          })),
        }
      : undefined,
    inputs: readIoMappings(task, 'input'),
    outputs: readIoMappings(task, 'output'),
  };
}

function commaSeparated(value: string | undefined): string[] {
  return (value ?? '')
    .split(',')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

/** Das Startformular, falls das Startereignis eines mitbringt. */
function readStartForm(start: Element): StartFormProperties | undefined {
  const extensions = firstChild(start, 'extensionElements');
  const form = extensions && firstChild(extensions, 'formDefinition');
  if (!form) return undefined;

  const formKey = attribute(form, 'formKey');
  const formId = attribute(form, 'formId');
  return formKey || formId ? { formKey, formId } : undefined;
}

function readEmbeddedForms(process: Element): EmbeddedForm[] {
  const extensions = firstChild(process, 'extensionElements');
  if (!extensions) return [];

  return children(extensions, 'userTaskForm')
    .map((form) => ({ id: form.getAttribute('id')?.trim() ?? '', schema: form.textContent ?? '' }))
    .filter((form) => form.id.length > 0);
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
      startForm: type === 'startEvent' ? readStartForm(element) : undefined,
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
      embeddedForms: readEmbeddedForms(process),
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
      startForm: node.startForm ? normalizeStartForm(node.startForm) : null,
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

  const forms = [...graph.embeddedForms]
    .sort((a, b) => a.id.localeCompare(b.id))
    .map((form) => [form.id, form.schema]);

  return JSON.stringify({
    processId: graph.processId,
    processName: graph.processName ?? null,
    exporter: graph.exporter ?? null,
    exporterVersion: graph.exporterVersion ?? null,
    nodes,
    flows,
    forms,
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

function normalizeStartForm(form: StartFormProperties) {
  return { formKey: form.formKey ?? null, formId: form.formId ?? null };
}

function normalizeTask(task: TaskProperties) {
  return {
    formKey: task.formKey ?? null,
    formId: task.formId ?? null,
    assignee: task.assignee ?? null,
    candidateGroups: task.candidateGroups ?? null,
    candidateUsers: task.candidateUsers ?? null,
    assignmentMode: task.assignmentMode ?? null,
    directoryAssigneeId: task.directoryAssigneeId ?? null,
    directoryCandidateUserIds: task.directoryCandidateUserIds ?? [],
    directoryCandidateGroupIds: task.directoryCandidateGroupIds ?? [],
    dueDate: task.dueDate ?? null,
    followUpDate: task.followUpDate ?? null,
    workerType: task.workerType ?? null,
    retries: task.retries ?? null,
    serviceTaskMode: task.serviceTaskMode ?? null,
    aiTask: task.aiTask
      ? {
          ...task.aiTask,
          model: task.aiTask.model || null,
        }
      : null,
    inputs: task.inputs.map((entry) => [entry.source, entry.target]),
    outputs: task.outputs.map((entry) => [entry.source, entry.target]),
  };
}
