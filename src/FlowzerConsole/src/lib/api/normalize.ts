import {
  FLOW_NODE_STATES,
  PROCESS_INSTANCE_STATES,
  type FlowNodeState,
  type ProcessInstanceInfoDto,
  type ProcessInstanceState,
  type TokenDto,
} from './types';

/**
 * Die API serialisiert C#-Enums als Zahlen (in `Program.cs` ist kein
 * `JsonStringEnumConverter` registriert). Damit die Oberfläche mit sprechenden
 * Werten arbeiten kann, werden die Zahlen hier einmalig in Literale übersetzt.
 *
 * Die Reihenfolge der Konstanten in `types.ts` muss deshalb exakt der Reihenfolge
 * der C#-Enums entsprechen — dafür sorgt `normalize.test.ts`.
 */

export function toFlowNodeState(value: FlowNodeState | number | undefined | null): FlowNodeState {
  if (typeof value === 'number') {
    return FLOW_NODE_STATES[value] ?? 'Ready';
  }
  return value ?? 'Ready';
}

export function toProcessInstanceState(
  value: ProcessInstanceState | number | undefined | null,
): ProcessInstanceState {
  if (typeof value === 'number') {
    return PROCESS_INSTANCE_STATES[value] ?? 'Initialized';
  }
  return value ?? 'Initialized';
}

export function normalizeToken(token: TokenDto): TokenDto {
  return { ...token, state: toFlowNodeState(token.state) };
}

export function normalizeInstance(instance: ProcessInstanceInfoDto): ProcessInstanceInfoDto {
  return {
    ...instance,
    state: toProcessInstanceState(instance.state),
    tokens: (instance.tokens ?? []).map(normalizeToken),
  };
}

/** Fachliche Zusammenfassung der Instanzzustände für Filter und Badges. */
export type InstanceBucket = 'active' | 'done' | 'error';

const BUCKET_BY_STATE: Record<ProcessInstanceState, InstanceBucket> = {
  Initialized: 'active',
  Running: 'active',
  Waiting: 'active',
  Completing: 'active',
  Compensating: 'active',
  Completed: 'done',
  Compensated: 'done',
  Failing: 'error',
  Failed: 'error',
  Terminating: 'error',
  Terminated: 'error',
};

export function instanceBucket(state: ProcessInstanceState): InstanceBucket {
  return BUCKET_BY_STATE[state] ?? 'active';
}

/** Tokens, die noch aktiv im Prozess stehen (also nicht abgeschlossen/verworfen sind). */
const LIVE_TOKEN_STATES = new Set<FlowNodeState>([
  'Ready',
  'Active',
  'Completing',
  'WaitingForLoopEnd',
  'Failing',
  'Compensating',
]);

export function isLiveToken(token: TokenDto): boolean {
  return LIVE_TOKEN_STATES.has(toFlowNodeState(token.state));
}

const FINISHED_TOKEN_STATES = new Set<FlowNodeState>([
  'Completed',
  'Merged',
  'Compensated',
  'Withdrawn',
]);

export function isFinishedToken(token: TokenDto): boolean {
  return FINISHED_TOKEN_STATES.has(toFlowNodeState(token.state));
}

const FAILED_TOKEN_STATES = new Set<FlowNodeState>(['Failed', 'Terminated']);

export function isFailedToken(token: TokenDto): boolean {
  return FAILED_TOKEN_STATES.has(toFlowNodeState(token.state));
}
