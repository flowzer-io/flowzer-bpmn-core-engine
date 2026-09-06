/**
 * Von der Gliederung zurueck zu BPMN.
 *
 * `buildGraph` erzeugt Knoten und Fluesse; darauf stuetzt sich sowohl die
 * Rueckuebersetzungsprobe beim Lesen als auch das Schreiben. `writeOutlineXml`
 * haengt Anordnung und Serialisierung an. Vorhandene Flusskennungen werden
 * beibehalten, damit ein Speichern ohne Strukturaenderung das vorhandene
 * Diagramm unveraendert weiterreichen kann.
 */
import { structureSignature, type BpmnGraph, type GraphFlow, type GraphNode } from './graph';
import { layoutGraph, type DiagramLayout } from './layout';
import { allBlocks, type OutlineBlock, type OutlineDocument, type OutlineIssue, type OutlineStep } from './model';

interface Builder {
  readonly document: OutlineDocument;
  readonly nodes: GraphNode[];
  readonly flows: GraphFlow[];
  readonly issues: OutlineIssue[];
  readonly usedIds: Set<string>;
  counter: number;
}

interface FlowOptions {
  readonly preferred?: string;
  readonly name?: string;
  readonly condition?: string;
}

function nextFreeId(builder: Builder, prefix: string): string {
  let candidate = `${prefix}_${++builder.counter}`;
  while (builder.usedIds.has(candidate)) candidate = `${prefix}_${++builder.counter}`;
  return candidate;
}

function addFlow(builder: Builder, source: string, target: string, options: FlowOptions = {}): string {
  const known = builder.document.flowIds[`${source}->${target}`];
  const preferred = [options.preferred, known].find((id) => id && !builder.usedIds.has(id));
  const id = preferred ?? nextFreeId(builder, 'Flow');

  builder.usedIds.add(id);
  builder.flows.push({
    id,
    source,
    target,
    name: options.name?.trim() || undefined,
    condition: options.condition?.trim() || undefined,
  });
  return id;
}

function taskNode(step: OutlineStep): GraphNode {
  return {
    id: step.id,
    type: step.task === 'user' ? 'userTask' : 'serviceTask',
    name: step.name.trim() || undefined,
    task: {
      formKey: step.task === 'user' ? step.formKey : undefined,
      formId: step.task === 'user' ? step.formId : undefined,
      assignee: step.task === 'user' ? step.assignee : undefined,
      candidateGroups: step.task === 'user' ? step.candidateGroups : undefined,
      candidateUsers: step.task === 'user' ? step.candidateUsers : undefined,
      dueDate: step.task === 'user' ? step.dueDate : undefined,
      followUpDate: step.task === 'user' ? step.followUpDate : undefined,
      workerType: step.task === 'service' ? step.workerType : undefined,
      retries: step.task === 'service' ? step.retries : undefined,
      inputs: step.inputs,
      outputs: step.outputs,
    },
  };
}

/**
 * Schreibt eine Folge rueckwaerts, weil jeder Block die Kennung seines
 * Nachfolgers braucht. Rueckgabe ist der Einstieg der Folge — bei einer leeren
 * Folge der uebergebene Ausgang.
 */
function emitSequence(builder: Builder, blocks: readonly OutlineBlock[], exit: string | undefined): string | undefined {
  blocks.forEach((block, index) => {
    if (block.kind === 'end' && index < blocks.length - 1) {
      builder.issues.push({ level: 'blocker', elementId: block.id, message: 'Nach einem Ende stehen weitere Schritte.' });
    }
  });

  let next = exit;
  for (let index = blocks.length - 1; index >= 0; index--) {
    const block = blocks[index];
    if (block) next = emitBlock(builder, block, next);
  }
  return next;
}

function requireTarget(builder: Builder, block: OutlineBlock, next: string | undefined): next is string {
  if (next !== undefined) return true;
  builder.issues.push({
    level: 'blocker',
    elementId: block.id,
    message: 'Der Ablauf läuft nach diesem Block ins Leere — es fehlt ein Ende.',
  });
  return false;
}

function emitBlock(builder: Builder, block: OutlineBlock, next: string | undefined): string {
  switch (block.kind) {
    case 'end':
      builder.nodes.push({ id: block.id, type: 'endEvent', name: block.name.trim() || undefined });
      return block.id;

    case 'step':
      builder.nodes.push(taskNode(block));
      if (requireTarget(builder, block, next)) addFlow(builder, block.id, next);
      return block.id;

    case 'parallel': {
      builder.nodes.push({ id: block.id, type: 'parallelGateway', name: block.name?.trim() || undefined });
      builder.nodes.push({ id: block.joinId, type: 'parallelGateway', name: block.joinName?.trim() || undefined });
      if (requireTarget(builder, block, next)) addFlow(builder, block.joinId, next);

      for (const branch of block.branches) {
        const entry = emitSequence(builder, branch.blocks, block.joinId);
        if (entry) addFlow(builder, block.id, entry, { preferred: branch.flowId });
      }
      return block.id;
    }

    case 'choice': {
      const branchExit = block.joinId ?? next;
      if (block.joinId) {
        builder.nodes.push({ id: block.joinId, type: 'exclusiveGateway', name: block.joinName?.trim() || undefined });
        if (requireTarget(builder, block, next)) addFlow(builder, block.joinId, next);
      }

      let defaultFlowId: string | undefined;
      for (const branch of block.branches) {
        const entry = emitSequence(builder, branch.blocks, branchExit);
        if (!entry) {
          builder.issues.push({
            level: 'blocker',
            elementId: block.id,
            message: 'Ein Zweig dieser Verzweigung hat keine Fortsetzung.',
          });
          continue;
        }
        const flowId = addFlow(builder, block.id, entry, {
          preferred: branch.flowId,
          name: branch.label,
          condition: branch.isDefault ? undefined : branch.condition,
        });
        if (branch.isDefault) defaultFlowId = flowId;
      }

      builder.nodes.push({
        id: block.id,
        type: 'exclusiveGateway',
        name: block.name?.trim() || undefined,
        defaultFlowId,
      });
      return block.id;
    }
  }
}

/** Erzeugt Knoten und Fluesse aus der Gliederung. */
export function buildGraph(document: OutlineDocument): { graph?: BpmnGraph; issues: OutlineIssue[] } {
  // Die Knotenkennungen stehen von Anfang an in `usedIds`: Sonst koennte eine
  // erzeugte Flusskennung auf den Namen eines Knotens fallen.
  const usedIds = new Set<string>([document.startId]);
  for (const block of allBlocks(document.blocks)) {
    usedIds.add(block.id);
    if (block.kind === 'parallel') usedIds.add(block.joinId);
    if (block.kind === 'choice' && block.joinId) usedIds.add(block.joinId);
  }

  const builder: Builder = { document, nodes: [], flows: [], issues: [], usedIds, counter: 0 };

  const entry = emitSequence(builder, document.blocks, undefined);
  builder.nodes.push({ id: document.startId, type: 'startEvent', name: document.startName?.trim() || undefined });
  if (entry) addFlow(builder, document.startId, entry);
  else builder.issues.push({ level: 'blocker', message: 'Der Ablauf enthält keinen ersten Schritt.' });

  const duplicates = builder.nodes.filter((node, index) => builder.nodes.findIndex((other) => other.id === node.id) !== index);
  for (const duplicate of duplicates) {
    builder.issues.push({ level: 'blocker', elementId: duplicate.id, message: `Die Kennung „${duplicate.id}" kommt mehrfach vor.` });
  }

  if (builder.issues.some((issue) => issue.level === 'blocker')) return { issues: builder.issues };

  return {
    graph: {
      definitionsId: document.definitionsId,
      targetNamespace: document.targetNamespace,
      exporter: document.exporter,
      exporterVersion: document.exporterVersion,
      processId: document.processId,
      processName: document.processName,
      nodes: builder.nodes,
      flows: builder.flows,
    },
    issues: builder.issues,
  };
}

/**
 * Die Engine weist ein Modell zurueck, dessen Aufgabe kein Formular beziehungsweise
 * keinen Diensttyp nennt. Das gehoert vor das Speichern und nicht in eine
 * Fehlermeldung der API — geprueft wird aber nur beim Schreiben, damit ein
 * vorhandenes Modell mit dieser Luecke trotzdem lesbar bleibt.
 */
export function missingStepDetails(document: OutlineDocument): OutlineIssue[] {
  const issues: OutlineIssue[] = [];

  for (const block of allBlocks(document.blocks)) {
    if (block.kind !== 'step') continue;
    const name = block.name.trim() || block.id;

    if (block.task === 'user' && !block.formKey?.trim() && !block.formId?.trim()) {
      issues.push({ level: 'blocker', elementId: block.id, message: `„${name}" braucht ein Formular.` });
    }
    if (block.task === 'service' && !block.workerType?.trim()) {
      issues.push({ level: 'blocker', elementId: block.id, message: `„${name}" braucht einen Typ des Dienstes.` });
    }
  }

  return issues;
}

const ESCAPES: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' };

function escape(value: string): string {
  return value.replace(/[&<>"]/g, (character) => ESCAPES[character] ?? character);
}

function attributes(entries: Readonly<Record<string, string | undefined>>): string {
  return Object.entries(entries)
    .filter(([, value]) => value !== undefined && value !== '')
    .map(([name, value]) => ` ${name}="${escape(value!)}"`)
    .join('');
}

function extensionXml(node: GraphNode, indent: string): string {
  const task = node.task;
  if (!task) return '';

  const lines: string[] = [];
  if (task.formKey || task.formId) {
    lines.push(`${indent}  <zeebe:formDefinition${attributes({ formKey: task.formKey, formId: task.formId })} />`);
  }
  if (task.assignee || task.candidateGroups || task.candidateUsers) {
    lines.push(
      `${indent}  <zeebe:assignmentDefinition${attributes({
        assignee: task.assignee,
        candidateGroups: task.candidateGroups,
        candidateUsers: task.candidateUsers,
      })} />`,
    );
  }
  if (task.dueDate || task.followUpDate) {
    lines.push(`${indent}  <zeebe:taskSchedule${attributes({ dueDate: task.dueDate, followUpDate: task.followUpDate })} />`);
  }
  if (task.workerType || task.retries) {
    lines.push(`${indent}  <zeebe:taskDefinition${attributes({ type: task.workerType, retries: task.retries })} />`);
  }
  if (task.inputs.length > 0 || task.outputs.length > 0) {
    lines.push(`${indent}  <zeebe:ioMapping>`);
    for (const input of task.inputs) {
      lines.push(`${indent}    <zeebe:input${attributes({ source: input.source, target: input.target })} />`);
    }
    for (const output of task.outputs) {
      lines.push(`${indent}    <zeebe:output${attributes({ source: output.source, target: output.target })} />`);
    }
    lines.push(`${indent}  </zeebe:ioMapping>`);
  }

  if (lines.length === 0) return '';
  return [`${indent}  <bpmn:extensionElements>`, ...lines.map((line) => `  ${line}`), `${indent}  </bpmn:extensionElements>`].join('\n');
}

function nodeXml(node: GraphNode, graph: BpmnGraph): string {
  const indent = '    ';
  const tag = `bpmn:${node.type}`;
  const own = attributes({ id: node.id, name: node.name, default: node.defaultFlowId });

  const children = [
    extensionXml(node, indent),
    ...graph.flows.filter((flow) => flow.target === node.id).map((flow) => `${indent}  <bpmn:incoming>${escape(flow.id)}</bpmn:incoming>`),
    ...graph.flows.filter((flow) => flow.source === node.id).map((flow) => `${indent}  <bpmn:outgoing>${escape(flow.id)}</bpmn:outgoing>`),
  ].filter((line) => line !== '');

  if (children.length === 0) return `${indent}<${tag}${own} />`;
  return [`${indent}<${tag}${own}>`, ...children, `${indent}</${tag}>`].join('\n');
}

function flowXml(flow: GraphFlow): string {
  const indent = '    ';
  const own = attributes({ id: flow.id, name: flow.name, sourceRef: flow.source, targetRef: flow.target });
  if (!flow.condition) return `${indent}<bpmn:sequenceFlow${own} />`;

  return [
    `${indent}<bpmn:sequenceFlow${own}>`,
    `${indent}  <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${escape(flow.condition)}</bpmn:conditionExpression>`,
    `${indent}</bpmn:sequenceFlow>`,
  ].join('\n');
}

function diagramXml(graph: BpmnGraph, layout: DiagramLayout): string {
  const shapes = layout.nodes.map((box) => {
    const node = graph.nodes.find((candidate) => candidate.id === box.id);
    const marker = node?.type === 'exclusiveGateway' ? ' isMarkerVisible="true"' : '';
    return [
      `      <bpmndi:BPMNShape id="Shape_${escape(box.id)}" bpmnElement="${escape(box.id)}"${marker}>`,
      `        <dc:Bounds x="${box.x}" y="${box.y}" width="${box.width}" height="${box.height}" />`,
      '      </bpmndi:BPMNShape>',
    ].join('\n');
  });

  const edges = layout.edges.map((edge) => {
    const points = edge.waypoints.map((point) => `        <di:waypoint x="${point.x}" y="${point.y}" />`);
    return [
      `      <bpmndi:BPMNEdge id="Edge_${escape(edge.id)}" bpmnElement="${escape(edge.id)}">`,
      ...points,
      '      </bpmndi:BPMNEdge>',
    ].join('\n');
  });

  return [
    `  <bpmndi:BPMNDiagram id="BPMNDiagram_${escape(graph.processId)}">`,
    `    <bpmndi:BPMNPlane id="BPMNPlane_${escape(graph.processId)}" bpmnElement="${escape(graph.processId)}">`,
    ...shapes,
    ...edges,
    '    </bpmndi:BPMNPlane>',
    '  </bpmndi:BPMNDiagram>',
  ].join('\n');
}

/**
 * Schreibt die Gliederung als BPMN-XML. Bleibt der Graph unveraendert, wird das
 * mitgelesene Diagramm unveraendert uebernommen; sonst wird neu angeordnet und
 * darauf hingewiesen.
 */
export function writeOutlineXml(document: OutlineDocument): { xml?: string; issues: OutlineIssue[] } {
  const { graph, issues } = buildGraph(document);
  const missing = missingStepDetails(document);
  if (!graph || missing.length > 0) return { issues: [...issues, ...missing] };

  const unchanged = document.sourceDiagram !== undefined && structureSignature(graph) === document.sourceStructure;
  const layout = layoutGraph(graph);
  const diagram = unchanged ? `  ${document.sourceDiagram}` : diagramXml(graph, layout);

  const notes: OutlineIssue[] = unchanged
    ? issues
    : [
        ...issues,
        { level: 'hinweis', message: 'Die Anordnung im Diagramm wird beim Speichern aus der Gliederung neu berechnet.' },
      ];

  // Die Anordnung von links nach rechts ist auch die lesbarste Reihenfolge im XML.
  const order = new Map(layout.nodes.map((box, index) => [box.id, index]));
  const nodes = [...graph.nodes].sort((a, b) => (order.get(a.id) ?? 0) - (order.get(b.id) ?? 0));

  const xml = [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"',
    '                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"',
    '                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"',
    '                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"',
    '                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"',
    '                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"',
    `                 ${attributes({
      id: graph.definitionsId,
      targetNamespace: graph.targetNamespace ?? 'http://bpmn.io/schema/bpmn',
      exporter: graph.exporter,
      exporterVersion: graph.exporterVersion,
    })}>`,
    `  <bpmn:process${attributes({ id: graph.processId, name: graph.processName })} isExecutable="true">`,
    ...nodes.map((node) => nodeXml(node, graph)),
    ...graph.flows.map(flowXml),
    '  </bpmn:process>',
    diagram,
    '</bpmn:definitions>',
    '',
  ].join('\n');

  return { xml, issues: notes };
}
