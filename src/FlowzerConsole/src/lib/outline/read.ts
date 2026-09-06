/**
 * Vom Graphen zur Gliederung.
 *
 * Der Prozessgraph wird in Folge, Gleichzeitig und Verzweigung zerlegt. Was sich
 * nicht zerlegen laesst, wird gemeldet. Zum Schluss laeuft die
 * Rueckuebersetzungsprobe: Die gelesene Gliederung wird sofort wieder zu einem
 * Graphen geschrieben und mit dem Ausgangsgraphen verglichen. Nur wenn beide
 * gleich sind, darf gespeichert werden.
 */
import {
  graphSignature,
  readGraph,
  structureSignature,
  type BpmnGraph,
  type GraphFlow,
  type GraphNode,
} from './graph';
import type {
  OutlineBlock,
  OutlineBranch,
  OutlineChoiceBranch,
  OutlineDocument,
  OutlineIssue,
  OutlineReadResult,
  OutlineStep,
} from './model';
import { buildGraph } from './write';

interface Structure {
  readonly nodeById: Map<string, GraphNode>;
  readonly outgoing: Map<string, GraphFlow[]>;
  readonly incoming: Map<string, GraphFlow[]>;
  readonly order: Map<string, number>;
  readonly visited: Set<string>;
  readonly issues: OutlineIssue[];
}

function group(flows: readonly GraphFlow[], key: (flow: GraphFlow) => string): Map<string, GraphFlow[]> {
  const grouped = new Map<string, GraphFlow[]>();
  for (const flow of flows) {
    const bucket = grouped.get(key(flow));
    if (bucket) bucket.push(flow);
    else grouped.set(key(flow), [flow]);
  }
  return grouped;
}

/** Topologische Reihenfolge. Knoten in einem Kreis fehlen darin. */
function topologicalOrder(graph: BpmnGraph): Map<string, number> {
  const remaining = new Map(graph.nodes.map((node) => [node.id, 0]));
  for (const flow of graph.flows) remaining.set(flow.target, (remaining.get(flow.target) ?? 0) + 1);

  const outgoing = group(graph.flows, (flow) => flow.source);
  const ready = [...remaining.entries()].filter(([, count]) => count === 0).map(([id]) => id);
  const order = new Map<string, number>();

  while (ready.length > 0) {
    const id = ready.shift()!;
    order.set(id, order.size);
    for (const flow of outgoing.get(id) ?? []) {
      const left = (remaining.get(flow.target) ?? 0) - 1;
      remaining.set(flow.target, left);
      if (left === 0) ready.push(flow.target);
    }
  }

  return order;
}

/** Alle von `from` aus erreichbaren Knoten. Knoten aus `stop` werden aufgenommen, aber nicht weiterverfolgt. */
function reachable(structure: Structure, from: string, stop: ReadonlySet<string>): Set<string> {
  const seen = new Set<string>();
  const queue = [from];

  while (queue.length > 0) {
    const id = queue.shift()!;
    if (seen.has(id)) continue;
    seen.add(id);
    if (stop.has(id)) continue;
    for (const flow of structure.outgoing.get(id) ?? []) queue.push(flow.target);
  }

  return seen;
}

/** Der früheste Knoten, an dem sich alle Zweige wieder treffen — oder keiner. */
function findMerge(structure: Structure, branches: readonly GraphFlow[], stop: ReadonlySet<string>): string | undefined {
  const sets = branches.map((flow) => reachable(structure, flow.target, stop));
  const [first, ...rest] = sets;
  if (!first) return undefined;

  const common = [...first].filter((id) => rest.every((set) => set.has(id)));
  if (common.length === 0) return undefined;

  return common.reduce((earliest, id) =>
    (structure.order.get(id) ?? 0) < (structure.order.get(earliest) ?? 0) ? id : earliest,
  );
}

function fail(structure: Structure, message: string, elementId?: string): void {
  structure.issues.push({ level: 'blocker', message, elementId });
}

function stepFrom(node: GraphNode): OutlineStep {
  const task = node.task;
  return {
    kind: 'step',
    id: node.id,
    name: node.name ?? '',
    task: node.type === 'userTask' ? 'user' : 'service',
    formKey: task?.formKey,
    formId: task?.formId,
    assignee: task?.assignee,
    candidateGroups: task?.candidateGroups,
    candidateUsers: task?.candidateUsers,
    dueDate: task?.dueDate,
    followUpDate: task?.followUpDate,
    workerType: task?.workerType,
    retries: task?.retries,
    inputs: task?.inputs ?? [],
    outputs: task?.outputs ?? [],
  };
}

function choiceBranch(structure: Structure, flow: GraphFlow, node: GraphNode, stop: ReadonlySet<string>): OutlineChoiceBranch {
  return {
    flowId: flow.id,
    label: flow.name,
    condition: flow.condition,
    isDefault: node.defaultFlowId === flow.id,
    blocks: parseSequence(structure, flow.target, stop),
  };
}

function parseParallel(structure: Structure, node: GraphNode, stop: ReadonlySet<string>): { block?: OutlineBlock; next?: string } {
  const forks = structure.outgoing.get(node.id) ?? [];
  const joinId = findMerge(structure, forks, stop);
  const join = joinId ? structure.nodeById.get(joinId) : undefined;

  if (
    !join ||
    join.type !== 'parallelGateway' ||
    (structure.incoming.get(join.id) ?? []).length !== forks.length ||
    (structure.outgoing.get(join.id) ?? []).length !== 1
  ) {
    fail(
      structure,
      `Die Zweige von „${node.name ?? node.id}" treffen sich nicht an genau einem gemeinsamen Tor.`,
      node.id,
    );
    return {};
  }

  structure.visited.add(join.id);
  const branchStop = new Set([...stop, join.id]);
  const branches: OutlineBranch[] = forks.map((flow) => ({
    flowId: flow.id,
    blocks: parseSequence(structure, flow.target, branchStop),
  }));

  for (const branch of branches) {
    if (branch.blocks.at(-1)?.kind === 'end') {
      fail(structure, `Ein Zweig von „${node.name ?? node.id}" endet, statt sich wieder zu treffen.`, node.id);
    }
  }

  return {
    block: { kind: 'parallel', id: node.id, joinId: join.id, name: node.name, joinName: join.name, branches },
    next: (structure.outgoing.get(join.id) ?? [])[0]?.target,
  };
}

function parseChoice(structure: Structure, node: GraphNode, stop: ReadonlySet<string>): { block?: OutlineBlock; next?: string } {
  const splits = structure.outgoing.get(node.id) ?? [];
  const mergeId = findMerge(structure, splits, stop);
  const merge = mergeId ? structure.nodeById.get(mergeId) : undefined;

  // Nur ein eigenes Zusammenfuehrungs-Tor, das genau diese Zweige buendelt, gehoert
  // zur Verzweigung. Alles andere ist der naechste Schritt der aeusseren Folge.
  const ownsJoin =
    merge !== undefined &&
    !stop.has(merge.id) &&
    merge.type === 'exclusiveGateway' &&
    (structure.incoming.get(merge.id) ?? []).length === splits.length &&
    (structure.outgoing.get(merge.id) ?? []).length === 1;

  const branchStop = mergeId ? new Set([...stop, mergeId]) : stop;
  const branches = splits.map((flow) => choiceBranch(structure, flow, node, branchStop));

  if (ownsJoin) structure.visited.add(merge.id);

  const block: OutlineBlock = {
    kind: 'choice',
    id: node.id,
    name: node.name,
    joinId: ownsJoin ? merge.id : undefined,
    joinName: ownsJoin ? merge.name : undefined,
    branches,
  };

  if (!mergeId || stop.has(mergeId)) return { block };
  if (ownsJoin) return { block, next: (structure.outgoing.get(merge.id) ?? [])[0]?.target };
  return { block, next: mergeId };
}

/** Eine Folge von Bloecken ab `start`, bis ein Knoten aus `stop` oder ein Ende erreicht ist. */
function parseSequence(structure: Structure, start: string, stop: ReadonlySet<string>): OutlineBlock[] {
  const blocks: OutlineBlock[] = [];
  let current: string | undefined = start;

  while (current !== undefined && !stop.has(current)) {
    const node = structure.nodeById.get(current);
    if (!node) {
      fail(structure, `Der Sequenzfluss zeigt auf ein unbekanntes Element „${current}".`);
      break;
    }
    if (structure.visited.has(node.id)) {
      fail(structure, `„${node.name ?? node.id}" wird von mehreren Stellen aus erreicht.`, node.id);
      break;
    }
    structure.visited.add(node.id);

    const outgoing = structure.outgoing.get(node.id) ?? [];

    if (node.type === 'endEvent') {
      blocks.push({ kind: 'end', id: node.id, name: node.name ?? '' });
      break;
    }

    if (node.type === 'startEvent') {
      fail(structure, 'Die Gliederung zeigt genau ein Start-Ereignis am Anfang.', node.id);
      break;
    }

    if (node.type === 'userTask' || node.type === 'serviceTask') {
      const only = outgoing.length === 1 ? outgoing[0] : undefined;
      if (!only) {
        fail(structure, `„${node.name ?? node.id}" hat ${outgoing.length} Ausgänge statt genau einem.`, node.id);
        break;
      }
      blocks.push(stepFrom(node));
      current = only.target;
      continue;
    }

    if (outgoing.length < 2) {
      fail(
        structure,
        `Das Tor „${node.name ?? node.id}" verzweigt nicht und lässt sich nicht als Block darstellen.`,
        node.id,
      );
      break;
    }

    const parsed = node.type === 'parallelGateway'
      ? parseParallel(structure, node, stop)
      : parseChoice(structure, node, stop);

    if (!parsed.block) break;
    blocks.push(parsed.block);
    current = parsed.next;
  }

  return blocks;
}

/**
 * Nur die Zweige einer Verzweigung tragen in der Gliederung Beschriftung und
 * Bedingung. Steht beides an einem anderen Fluss, waere es beim Speichern weg —
 * das gehoert benannt und nicht der allgemeinen Rueckuebersetzungsprobe ueberlassen.
 */
function describeLostFlowLabels(structure: Structure, graph: BpmnGraph): void {
  for (const flow of graph.flows) {
    if (!flow.name && !flow.condition) continue;

    const source = structure.nodeById.get(flow.source);
    const isBranch = source?.type === 'exclusiveGateway' && (structure.outgoing.get(flow.source) ?? []).length > 1;
    if (isBranch) continue;

    fail(
      structure,
      `Der Fluss ab „${source?.name ?? flow.source}" trägt eine Beschriftung oder Bedingung; die zeigt die Gliederung nur an Verzweigungen.`,
      flow.id,
    );
  }
}

function describeUnreached(structure: Structure, graph: BpmnGraph): void {
  const missing = graph.nodes.filter((node) => node.type !== 'startEvent' && !structure.visited.has(node.id));
  for (const node of missing) {
    fail(structure, `„${node.name ?? node.id}" liegt außerhalb der Gliederung.`, node.id);
  }
}

/** Liest ein BPMN-Modell als Gliederung. Ohne Blocker steht `document` bereit. */
export function readOutline(xml: string | undefined | null): OutlineReadResult {
  if (!xml) return { issues: [{ level: 'blocker', message: 'Es liegt kein BPMN-XML vor.' }] };

  const { graph, issues } = readGraph(xml);
  if (!graph) return { issues };

  // Ein frisch angelegter Workflow enthaelt noch gar keine Elemente. Statt ihn als
  // unlesbar abzuweisen, legt die Gliederung Start und Ende an; die Schritte
  // dazwischen kommen aus der Bearbeitung.
  if (graph.nodes.length === 0 && graph.flows.length === 0) {
    return {
      document: {
        definitionsId: graph.definitionsId,
        targetNamespace: graph.targetNamespace,
        exporter: graph.exporter,
        exporterVersion: graph.exporterVersion,
        processId: graph.processId,
        processName: graph.processName,
        startId: 'StartEvent_1',
        startName: 'Start',
        blocks: [{ kind: 'end', id: 'EndEvent_1', name: 'Ende' }],
        flowIds: {},
      },
      issues: [
        ...issues,
        { level: 'hinweis', message: 'Dieser Workflow ist noch leer. Die Gliederung legt Start und Ende an.' },
      ],
    };
  }

  // Eine unvollstaendige Ordnung heisst: Diese Knoten haengen in einem Kreis.
  // Sie zu benennen ist die brauchbarere Meldung als ein pauschales „irgendwo
  // ist ein Ruecksprung" — gerade wenn der Kreis abseits des Hauptablaufs liegt.
  const order = topologicalOrder(graph);
  if (order.size !== graph.nodes.length) {
    const caught = graph.nodes.filter((node) => !order.has(node.id)).map((node) => `„${node.name ?? node.id}"`);
    return {
      issues: [
        ...issues,
        {
          level: 'blocker',
          message: `${caught.join(', ')} liegen in einem Rücksprung; die Gliederung stellt nur Abläufe ohne Schleifen dar.`,
        },
      ],
    };
  }

  const structure: Structure = {
    nodeById: new Map(graph.nodes.map((node) => [node.id, node])),
    outgoing: group(graph.flows, (flow) => flow.source),
    incoming: group(graph.flows, (flow) => flow.target),
    order,
    visited: new Set(),
    issues: [...issues],
  };

  const starts = graph.nodes.filter((node) => node.type === 'startEvent');
  const [start] = starts;
  if (!start || starts.length !== 1) {
    structure.issues.push({
      level: 'blocker',
      message: `Die Gliederung braucht genau ein Start-Ereignis; dieser Prozess hat ${starts.length}.`,
    });
    return { issues: structure.issues };
  }

  const firstFlow = (structure.outgoing.get(start.id) ?? [])[0];
  if (!firstFlow || (structure.outgoing.get(start.id) ?? []).length !== 1) {
    structure.issues.push({ level: 'blocker', elementId: start.id, message: 'Das Start-Ereignis hat nicht genau einen Ausgang.' });
    return { issues: structure.issues };
  }

  const blocks = parseSequence(structure, firstFlow.target, new Set());
  describeUnreached(structure, graph);
  describeLostFlowLabels(structure, graph);

  const flowIds: Record<string, string> = {};
  for (const flow of graph.flows) flowIds[`${flow.source}->${flow.target}`] = flow.id;

  const signature = graphSignature(graph);
  const document: OutlineDocument = {
    definitionsId: graph.definitionsId,
    targetNamespace: graph.targetNamespace,
    exporter: graph.exporter,
    exporterVersion: graph.exporterVersion,
    processId: graph.processId,
    processName: graph.processName,
    startId: start.id,
    startName: start.name,
    blocks,
    flowIds,
    sourceDiagram: graph.diagramXml,
    sourceStructure: structureSignature(graph),
  };

  if (structure.issues.some((issue) => issue.level === 'blocker')) {
    return { issues: structure.issues };
  }

  const roundTrip = buildGraph(document);
  structure.issues.push(...roundTrip.issues);
  if (roundTrip.graph && graphSignature(roundTrip.graph) !== signature) {
    structure.issues.push({
      level: 'blocker',
      message: 'Die Gliederung gibt dieses Modell nicht unverändert zurück und darf es deshalb nicht speichern.',
    });
  }

  if (structure.issues.some((issue) => issue.level === 'blocker')) {
    return { issues: structure.issues };
  }

  return { document, issues: structure.issues };
}
