import type { RuntimeDiagram } from '@flowzer/sdk';

import type { NodeMarker } from '@/components/bpmn/BpmnViewer';
import { FLOW_NODE_STATES, type FlowNodeState } from '@/lib/api/types';

export type RuntimeNodeStatus = 'active' | 'completed' | 'cancelled' | 'failed' | 'unknown';

const STATUS_BY_NUMBER: RuntimeNodeStatus[] = ['active', 'completed', 'cancelled', 'failed'];

export const RUNTIME_STATUS_LABEL: Record<RuntimeNodeStatus, string> = {
  active: 'Aktiv',
  completed: 'Abgeschlossen',
  cancelled: 'Abgebrochen',
  failed: 'Gestört',
  unknown: 'Unbekannt',
};

export const RUNTIME_STATUS_ICON: Record<RuntimeNodeStatus, string> = {
  active: 'play_arrow',
  completed: 'check',
  cancelled: 'block',
  failed: 'error',
  unknown: 'help',
};

export function runtimeNodeStatus(value: unknown): RuntimeNodeStatus {
  return typeof value === 'number' ? STATUS_BY_NUMBER[value] ?? 'unknown' : 'unknown';
}

export function runtimeState(value: unknown): FlowNodeState | 'Unknown' {
  return typeof value === 'number' ? FLOW_NODE_STATES[value] ?? 'Unknown' : 'Unknown';
}

export function runtimeMarkers(runtime: RuntimeDiagram | undefined): {
  markers: Record<string, NodeMarker>;
  activeNodeIds: string[];
} {
  const markers: Record<string, NodeMarker> = {};
  const activeNodeIds: string[] = [];
  for (const node of runtime?.nodes ?? []) {
    if (!node.flowNodeId) continue;
    const status = runtimeNodeStatus(node.status);
    if (status === 'unknown') continue;
    markers[node.flowNodeId] = status;
    if (status === 'active') activeNodeIds.push(node.flowNodeId);
  }
  return { markers, activeNodeIds };
}
