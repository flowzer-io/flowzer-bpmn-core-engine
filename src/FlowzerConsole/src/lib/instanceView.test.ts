import { describe, expect, it } from 'vitest';

import type { ProcessInstanceInfoDto } from '@/lib/api/types';

import { instanceTone, nodeExecutions, processScopeVariables, STATE_LABEL } from './instanceView';

const instance = {
  instanceId: 'instance-1', definitionId: 'definition-1', relatedDefinitionId: 'workflow-1',
  relatedDefinitionName: 'Workflow', state: 'Waiting', canInspect: true,
  userTaskSubscriptionCount: 0, messageSubscriptionCount: 0, signalSubscriptionCount: 0,
  serviceSubscriptionCount: 0,
  tokens: [
    {
      id: 'master', parentTokenId: null, currentFlowNodeId: 'Process_1', state: 'Active',
      variables: { requestId: 'REQ-42', approved: false }, outputData: { initial: true },
      startTime: '2026-09-09T08:00:00Z', lastStateChangeTime: '2026-09-09T08:00:00Z',
    },
    {
      id: 'review-2', parentTokenId: 'master', currentFlowNodeId: 'Review', state: 'Active',
      variables: { reviewer: 'B' }, outputData: { decision: 'open' },
      startTime: '2026-09-09T09:05:00Z', lastStateChangeTime: '2026-09-09T09:06:00Z',
    },
    {
      id: 'review-1', parentTokenId: 'master', currentFlowNodeId: 'Review', state: 'Completed',
      variables: { reviewer: 'A' }, outputData: { decision: 'approved' },
      startTime: '2026-09-09T09:00:00Z', lastStateChangeTime: '2026-09-09T09:04:00Z',
    },
  ],
} satisfies ProcessInstanceInfoDto;

describe('Instanzdatenprojektion', () => {
  // Testzweck: Prozessvariablen stammen immer aus dem Master-Token und nicht aus
  // einem zufällig zuletzt aktiven Fachknoten.
  it('liest den aktuellen Prozessscope aus dem Master-Token', () => {
    expect(processScopeVariables(instance)).toEqual({ requestId: 'REQ-42', approved: false });
  });

  // Testzweck: Parallele und wiederholte Knotenausführungen bleiben einzeln und
  // deterministisch sortiert, damit Input und Output nicht vermischt werden.
  it('liefert alle Ausführungen eines Knotens in stabiler Reihenfolge', () => {
    expect(nodeExecutions(instance, 'Review').map((token) => token.id))
      .toEqual(['review-1', 'review-2']);
    expect(nodeExecutions(instance, 'Review')[0]?.outputData).toEqual({ decision: 'approved' });
  });
});

describe('Anzeige der Instanzzustände', () => {
  // Testzweck: „Beendet“ liest sich wie ein neutrales Ende und verschweigt, dass jemand
  // die Instanz abgebrochen hat. Die Tokenliste nennt denselben Zustand längst
  // „Abgebrochen“; beides muss dasselbe Wort benutzen.
  it('benennt den Abbruch als Abbruch', () => {
    expect(STATE_LABEL.Terminated).toBe('Abgebrochen');
    expect(STATE_LABEL.Terminating).toBe('Wird abgebrochen');
    expect(STATE_LABEL.Completed).toBe('Abgeschlossen');
  });

  // Testzweck: Der Statuschip färbt den Ausgang. Rot behauptete eine Störung, Grün einen
  // fachlichen Erfolg — ein Abbruch ist weder das eine noch das andere.
  it('färbt einen Abbruch weder als Fehler noch als Erfolg', () => {
    expect(instanceTone('Terminated')).toBe('wait');
    expect(instanceTone('Terminating')).toBe('wait');
    expect(instanceTone('Completed')).toBe('done');
    expect(instanceTone('Failed')).toBe('fail');
    expect(instanceTone('Waiting')).toBe('run');
  });
});
