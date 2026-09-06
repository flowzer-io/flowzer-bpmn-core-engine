/**
 * Automatische Anordnung fuer die DI-Koordinaten.
 *
 * Die Gliederung kennt keine Koordinaten. Wird die Struktur geaendert, muss das
 * Diagramm neu angeordnet werden. Das Verfahren ist die uebliche Schichtung:
 * Ebenen von links nach rechts nach dem laengsten Weg, Zeilen von oben nach
 * unten, danach ein paar Ausgleichsdurchlaeufe, die jeden Knoten zu seinen
 * Nachbarn zieht. Das Ergebnis ist brauchbar, nicht huebsch — Feinarbeit bleibt
 * dem Diagramm vorbehalten.
 */
import type { BpmnGraph, GraphNodeType } from './graph';

export interface NodeBox {
  readonly id: string;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface Waypoint {
  readonly x: number;
  readonly y: number;
}

export interface EdgeRoute {
  readonly id: string;
  readonly waypoints: readonly Waypoint[];
}

export interface DiagramLayout {
  readonly nodes: readonly NodeBox[];
  readonly edges: readonly EdgeRoute[];
}

const SIZES: Record<GraphNodeType, { width: number; height: number }> = {
  startEvent: { width: 36, height: 36 },
  endEvent: { width: 36, height: 36 },
  userTask: { width: 140, height: 80 },
  serviceTask: { width: 140, height: 80 },
  exclusiveGateway: { width: 50, height: 50 },
  parallelGateway: { width: 50, height: 50 },
};

const COLUMN_GAP = 60;
const ROW_GAP = 40;
const ROW_PITCH = 130;
const ORIGIN_X = 160;
const ORIGIN_Y = 80;
const BALANCE_PASSES = 4;

interface Placed {
  readonly id: string;
  readonly width: number;
  readonly height: number;
  readonly layer: number;
  /** Mitte des Knotens; erst am Ende in die linke obere Ecke umgerechnet. */
  center: number;
}

/**
 * Ebene nach dem laengsten Weg: ein Knoten steht rechts von allen seinen
 * Vorgaengern. Die Entspannung laeuft, bis sich nichts mehr aendert; bei einem
 * kreisfreien Graphen sind das hoechstens so viele Durchlaeufe wie Knoten.
 */
function assignLayers(graph: BpmnGraph): Map<string, number> {
  const layers = new Map(graph.nodes.map((node) => [node.id, 0]));

  for (let pass = 0; pass <= graph.nodes.length; pass++) {
    let changed = false;
    for (const flow of graph.flows) {
      const wanted = (layers.get(flow.source) ?? 0) + 1;
      if (wanted > (layers.get(flow.target) ?? 0)) {
        layers.set(flow.target, wanted);
        changed = true;
      }
    }
    if (!changed) break;
  }

  return layers;
}

/**
 * Reihenfolge innerhalb der Ebenen. Die Tiefensuche folgt der Reihenfolge der
 * Sequenzfluesse, sodass der Hauptweg oben bleibt und Zweige darunter landen.
 */
function discoveryOrder(graph: BpmnGraph): string[] {
  const outgoing = new Map<string, string[]>();
  for (const flow of graph.flows) {
    const bucket = outgoing.get(flow.source);
    if (bucket) bucket.push(flow.target);
    else outgoing.set(flow.source, [flow.target]);
  }

  const start = graph.nodes.find((node) => node.type === 'startEvent') ?? graph.nodes[0];
  const seen = new Set<string>();
  const visited: string[] = [];

  const walk = (id: string) => {
    if (seen.has(id)) return;
    seen.add(id);
    visited.push(id);
    for (const target of outgoing.get(id) ?? []) walk(target);
  };

  if (start) walk(start.id);
  for (const node of graph.nodes) walk(node.id);
  return visited;
}

/** Verschiebt die Knoten einer Ebene so, dass sie ihre Wunschlage halten und sich nicht überlappen. */
function spread(layer: Placed[], desired: readonly number[]): void {
  layer.forEach((entry, index) => {
    const above = layer[index - 1];
    const minimum = above ? above.center + above.height / 2 + ROW_GAP + entry.height / 2 : -Infinity;
    entry.center = Math.max(desired[index] ?? entry.center, minimum);
  });
}

function averageOf(values: readonly number[]): number | undefined {
  if (values.length === 0) return undefined;
  return values.reduce((sum, value) => sum + value, 0) / values.length;
}

export function layoutGraph(graph: BpmnGraph): DiagramLayout {
  const order = discoveryOrder(graph);
  const layers = assignLayers(graph);
  const rank = new Map(order.map((id, index) => [id, index]));

  const placed = new Map<string, Placed>();
  const byLayer = new Map<number, Placed[]>();

  for (const node of [...graph.nodes].sort((a, b) => (rank.get(a.id) ?? 0) - (rank.get(b.id) ?? 0))) {
    const size = SIZES[node.type];
    const layer = layers.get(node.id) ?? 0;
    const bucket = byLayer.get(layer) ?? [];
    const entry: Placed = { id: node.id, ...size, layer, center: bucket.length * ROW_PITCH };
    bucket.push(entry);
    byLayer.set(layer, bucket);
    placed.set(node.id, entry);
  }

  const layerNumbers = [...byLayer.keys()].sort((a, b) => a - b);
  const predecessors = new Map<string, string[]>();
  const successors = new Map<string, string[]>();
  for (const flow of graph.flows) {
    predecessors.set(flow.target, [...(predecessors.get(flow.target) ?? []), flow.source]);
    successors.set(flow.source, [...(successors.get(flow.source) ?? []), flow.target]);
  }

  // Abwechselnd nach rechts und nach links ausgleichen: Jeder Knoten rutscht zur
  // Mitte seiner Nachbarn, danach werden Ueberlappungen wieder aufgeloest.
  for (let pass = 0; pass < BALANCE_PASSES; pass++) {
    const forward = pass % 2 === 0;
    const sequence = forward ? layerNumbers : [...layerNumbers].reverse();

    for (const layerNumber of sequence) {
      const layer = byLayer.get(layerNumber) ?? [];
      const neighbours = forward ? predecessors : successors;
      const desired = layer.map((entry) => {
        const centers = (neighbours.get(entry.id) ?? [])
          .map((id) => placed.get(id)?.center)
          .filter((value): value is number => value !== undefined);
        return averageOf(centers) ?? entry.center;
      });
      spread(layer, desired);
    }
  }

  const columnWidth = new Map<number, number>();
  const columnX = new Map<number, number>();
  let cursor = 0;
  for (const layerNumber of layerNumbers) {
    const width = Math.max(...(byLayer.get(layerNumber) ?? []).map((entry) => entry.width));
    columnWidth.set(layerNumber, width);
    columnX.set(layerNumber, cursor);
    cursor += width + COLUMN_GAP;
  }

  const topOffset = Math.min(...[...placed.values()].map((entry) => entry.center - entry.height / 2));

  const nodes: NodeBox[] = [...placed.values()].map((entry) => ({
    id: entry.id,
    x: ORIGIN_X + (columnX.get(entry.layer) ?? 0) + ((columnWidth.get(entry.layer) ?? entry.width) - entry.width) / 2,
    y: ORIGIN_Y + entry.center - entry.height / 2 - topOffset,
    width: entry.width,
    height: entry.height,
  }));

  const boxes = new Map(nodes.map((node) => [node.id, node]));

  const edges: EdgeRoute[] = graph.flows.map((flow) => {
    const from = boxes.get(flow.source);
    const to = boxes.get(flow.target);
    if (!from || !to) return { id: flow.id, waypoints: [] };

    const startPoint = { x: from.x + from.width, y: Math.round(from.y + from.height / 2) };
    const endPoint = { x: to.x, y: Math.round(to.y + to.height / 2) };
    if (startPoint.y === endPoint.y) return { id: flow.id, waypoints: [startPoint, endPoint] };

    const middle = Math.round((startPoint.x + endPoint.x) / 2);
    return {
      id: flow.id,
      waypoints: [
        startPoint,
        { x: middle, y: startPoint.y },
        { x: middle, y: endPoint.y },
        endPoint,
      ],
    };
  });

  return { nodes: nodes.map(round), edges };
}

function round(box: NodeBox): NodeBox {
  return { ...box, x: Math.round(box.x), y: Math.round(box.y) };
}
