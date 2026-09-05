import { describe, expect, it } from 'vitest';

import {
  instanceBucket,
  isFailedToken,
  isLiveToken,
  toFlowNodeState,
  toProcessInstanceState,
} from './normalize';
import { FLOW_NODE_STATES, PROCESS_INSTANCE_STATES, type TokenDto } from './types';

// Testzweck: Die API serialisiert Enums als Zahlen. Diese Tests halten die
// Reihenfolge der Literale mit den C#-Enums in `WebApiEngine.Shared` synchron —
// eine Verschiebung dort würde sonst still falsche Zustände anzeigen.
describe('Enum-Zuordnung', () => {
  it('bildet die numerischen FlowNodeState-Werte auf die C#-Reihenfolge ab', () => {
    expect(FLOW_NODE_STATES).toEqual([
      'Ready',
      'Active',
      'Completing',
      'WaitingForLoopEnd',
      'Completed',
      'Failing',
      'Terminating',
      'Failed',
      'Terminated',
      'Withdrawn',
      'Compensating',
      'Compensated',
      'Merged',
    ]);

    expect(toFlowNodeState(0)).toBe('Ready');
    expect(toFlowNodeState(1)).toBe('Active');
    expect(toFlowNodeState(4)).toBe('Completed');
    expect(toFlowNodeState(12)).toBe('Merged');
  });

  it('bildet die numerischen ProcessInstanceState-Werte auf die C#-Reihenfolge ab', () => {
    expect(PROCESS_INSTANCE_STATES).toEqual([
      'Initialized',
      'Running',
      'Waiting',
      'Completing',
      'Completed',
      'Failing',
      'Failed',
      'Terminating',
      'Terminated',
      'Compensating',
      'Compensated',
    ]);

    expect(toProcessInstanceState(1)).toBe('Running');
    expect(toProcessInstanceState(4)).toBe('Completed');
    expect(toProcessInstanceState(6)).toBe('Failed');
  });

  it('lässt bereits übersetzte Literale unverändert', () => {
    expect(toFlowNodeState('Active')).toBe('Active');
    expect(toProcessInstanceState('Waiting')).toBe('Waiting');
  });

  it('fällt bei unbekannten Zahlen auf den Anfangszustand zurück', () => {
    expect(toFlowNodeState(99)).toBe('Ready');
    expect(toProcessInstanceState(99)).toBe('Initialized');
  });
});

describe('Zustands-Eimer', () => {
  it('ordnet laufende, fertige und fehlerhafte Zustände korrekt zu', () => {
    expect(instanceBucket('Running')).toBe('active');
    expect(instanceBucket('Waiting')).toBe('active');
    expect(instanceBucket('Completed')).toBe('done');
    expect(instanceBucket('Compensated')).toBe('done');
    expect(instanceBucket('Failed')).toBe('error');
    expect(instanceBucket('Terminated')).toBe('error');
  });
});

describe('Token-Klassifizierung', () => {
  const token = (state: TokenDto['state']): TokenDto => ({ id: 't', state });

  it('erkennt aktive Tokens', () => {
    expect(isLiveToken(token('Active'))).toBe(true);
    expect(isLiveToken(token('Ready'))).toBe(true);
    expect(isLiveToken(token('Completed'))).toBe(false);
  });

  it('erkennt fehlgeschlagene Tokens', () => {
    expect(isFailedToken(token('Failed'))).toBe(true);
    expect(isFailedToken(token('Terminated'))).toBe(true);
    expect(isFailedToken(token('Active'))).toBe(false);
  });
});
