import { describe, expect, it } from 'vitest';

import type { ProcessInstanceInfoDto } from '@/lib/api/types';

import { nodeExecutions, processScopeVariables } from './instanceView';

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
