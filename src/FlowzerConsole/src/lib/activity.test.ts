import { renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { ProcessInstanceInfoDto } from '@/lib/api/types';

import { useActivityFeed } from './activity';

const mocks = vi.hoisted(() => ({ instances: vi.fn(), timers: vi.fn(), diagnostics: vi.fn() }));
vi.mock('@/lib/api/queries', () => ({
  useInstances: mocks.instances,
  useTimers: mocks.timers,
  useDiagnostics: mocks.diagnostics,
}));

const instance = {
  instanceId: 'a1b2c3d4-0000-0000-0000-000000000000', definitionId: 'definition-1',
  relatedDefinitionId: 'urlaub', relatedDefinitionName: 'Urlaubsantrag', state: 'Terminated',
  canInspect: true, userTaskSubscriptionCount: 0, messageSubscriptionCount: 0,
  signalSubscriptionCount: 0, serviceSubscriptionCount: 0, tokens: [],
  startedAt: '2026-09-08T10:00:00Z', finishedAt: '2026-09-08T11:00:00Z',
} satisfies ProcessInstanceInfoDto;

function feedFor(instances: ProcessInstanceInfoDto[]) {
  mocks.instances.mockReturnValue({ data: instances });
  return renderHook(() => useActivityFeed()).result.current;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.timers.mockReturnValue({ data: [] });
  mocks.diagnostics.mockReturnValue({ data: undefined });
});

describe('Aktivitätsstrom', () => {
  // Testzweck: Wer eine Instanz bewusst abbricht — oder ein Terminate-Endereignis wie beim
  // abgelehnten Urlaubsantrag erreicht —, darf im Dashboard nicht lesen, sie sei
  // fehlgeschlagen. Das schickt den Betrieb einer Störung hinterher, die es nie gab.
  it('meldet einen Abbruch als Abbruch und nicht als Fehlschlag', () => {
    const [entry, ...rest] = feedFor([instance]);

    expect(rest).toHaveLength(0);
    expect(entry?.text).toMatch(/wurde abgebrochen$/);
    expect(entry?.text).toContain('Urlaubsantrag');
    expect(entry?.text).not.toMatch(/fehlgeschlagen|abgeschlossen/);
    expect(entry?.tone).toBe('wait');
  });

  // Testzweck: Der Abbruch darf den echten Fehlschlag nicht mit sich ziehen — eine
  // gestörte Instanz bleibt eine Meldung, der der Betrieb nachgehen muss.
  it('meldet gescheiterte Instanzen weiterhin als Fehlschlag', () => {
    const [entry] = feedFor([{ ...instance, state: 'Failed' }]);

    expect(entry?.text).toMatch(/ist fehlgeschlagen$/);
    expect(entry?.tone).toBe('fail');
  });

  // Testzweck: Abgeschlossene Instanzen behalten ihre eigene Meldung; sonst hätte der
  // Abbruch den fachlichen Erfolg mitgeschluckt.
  it('meldet abgeschlossene Instanzen unverändert als Abschluss', () => {
    const [entry] = feedFor([{ ...instance, state: 'Completed' }]);

    expect(entry?.text).toMatch(/wurde abgeschlossen$/);
    expect(entry?.tone).toBe('done');
  });
});
