import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type * as Endpoints from './endpoints';
import { queryKeys, useCancelInstance, useMigrateInstances } from './queries';
import type { ProcessInstanceInfoDto } from './types';

const mocks = vi.hoisted(() => ({ cancel: vi.fn(), migrate: vi.fn() }));

vi.mock('@flowzer/react', () => ({
  useFlowzer: () => ({ cacheNamespace: 'console', sessionScope: 'session' }),
  flowzerQueryKeys: {
    instances: (namespace: string, scope: string) => ['flowzer', namespace, scope, 'instances'],
    userTasks: (namespace: string, scope: string) => ['flowzer', namespace, scope, 'userTasks'],
  },
}));
vi.mock('./endpoints', async (importOriginal) => {
  const original = await importOriginal<typeof Endpoints>();
  return { ...original, instancesApi: { ...original.instancesApi, cancel: mocks.cancel, migrate: mocks.migrate } };
});

const terminated: ProcessInstanceInfoDto = {
  instanceId: 'instance-1', definitionId: 'definition-1', relatedDefinitionId: 'urlaub',
  relatedDefinitionName: 'Urlaubsantrag', state: 'Terminated', canInspect: true,
  userTaskSubscriptionCount: 0, messageSubscriptionCount: 0, signalSubscriptionCount: 0,
  serviceSubscriptionCount: 0, tokens: [],
};

function setup() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const invalidated: unknown[] = [];
  const original = queryClient.invalidateQueries.bind(queryClient);
  vi.spyOn(queryClient, 'invalidateQueries').mockImplementation((filters) => {
    invalidated.push(filters?.queryKey);
    return original(filters);
  });

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return { queryClient, invalidated, wrapper };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.cancel.mockResolvedValue(terminated);
  mocks.migrate.mockResolvedValue({
    targetDefinitionId: 'definition-2', targetVersion: { major: 2, minor: 0 },
    instances: [{ instanceId: 'instance-1', migrated: true, problems: [] }],
  });
});

describe('Eingriffe in laufende Instanzen', () => {
  // Testzweck: Die API liefert die beendete Instanz zurück. Landet sie nicht sofort im
  // Cache, zeigt das Detail bis zum nächsten Abruf „Wartet“ und bietet den Abbruch erneut an.
  it('schreibt die abgebrochene Instanz sofort in den Cache', async () => {
    const { queryClient, wrapper } = setup();
    const { result } = renderHook(() => useCancelInstance(), { wrapper });

    result.current.mutate('instance-1');

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(queryKeys.instance('instance-1'))).toMatchObject({
      state: 'Terminated',
    });
  });

  // Testzweck: Mit der Instanz entfallen ihre Timer und Warteobjekte. Die Betriebsansicht
  // zählt genau die und zeigte sonst weiter Aufträge, die es nicht mehr gibt.
  it('verwirft nach dem Abbruch auch Aufgaben- und Betriebsansicht', async () => {
    const { invalidated, wrapper } = setup();
    const { result } = renderHook(() => useCancelInstance(), { wrapper });

    result.current.mutate('instance-1');

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidated).toContainEqual(queryKeys.instances);
    expect(invalidated).toContainEqual(queryKeys.operations);
    expect(invalidated).toContainEqual(['flowzer', 'console', 'session', 'userTasks']);
  });

  // Testzweck: Eine Migration hängt dieselben Warteobjekte um wie ein Abbruch sie entfernt.
  // Sie muss deshalb denselben Satz an Ansichten verwerfen — nicht einen kleineren.
  it('verwirft nach der Migration dieselben Ansichten', async () => {
    const { invalidated, wrapper } = setup();
    const { result } = renderHook(() => useMigrateInstances(), { wrapper });

    result.current.mutate({ instanceIds: ['instance-1'], targetDefinitionId: 'definition-2' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidated).toContainEqual(queryKeys.instances);
    expect(invalidated).toContainEqual(queryKeys.operations);
    expect(invalidated).toContainEqual(['flowzer', 'console', 'session', 'instances']);
    expect(invalidated).toContainEqual(['flowzer', 'console', 'session', 'userTasks']);
  });

  // Testzweck: Die Vorschau ist die Grundlage einer einmaligen Entscheidung. Würde die
  // Migration sie mitverwerfen, lüde der offene Assistent sie neu und zeigte im Ergebnis
  // „v2 → v2" statt des Wegs, den die Instanzen tatsächlich genommen haben.
  it('lässt die Vorschau der Migration beim Verwerfen der Instanzansichten stehen', async () => {
    const { queryClient, wrapper } = setup();
    const previewKey = queryKeys.instanceMigrationPreview(['instance-1']);
    queryClient.setQueryData(previewKey, { sourceVersion: { major: 1, minor: 0 } });
    const { result } = renderHook(() => useMigrateInstances(), { wrapper });

    result.current.mutate({ instanceIds: ['instance-1'], targetDefinitionId: 'definition-2' });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(queryClient.getQueryState(previewKey)?.isInvalidated).toBe(false);
  });
});

describe('Schlüssel der Migrationsvorschau', () => {
  // Testzweck: Eine andere Zuordnung ist eine andere Prüfung. Stünde sie nicht im Schlüssel,
  // beantwortete der Cache sie mit dem Ergebnis der vorigen Zuordnung — der Betrieb sähe
  // seine gerade getroffene Wahl nicht wirken.
  it('trennt Vorschauen nach ihrer Zuordnung', () => {
    const ohne = queryKeys.instanceMigrationPreview(['instance-1']);

    expect(queryKeys.instanceMigrationPreview(['instance-1'], { Review: 'Freigabe' })).not.toEqual(ohne);
    expect(queryKeys.instanceMigrationPreview(['instance-1'], { Review: 'Freigabe' })).not.toEqual(
      queryKeys.instanceMigrationPreview(['instance-1'], { Review: 'Abnahme' }),
    );
  });

  // Testzweck: Dieselbe Zuordnung ist dieselbe Prüfung. Hinge der Schlüssel an der
  // Reihenfolge der Einträge, liefe bei jedem Rendern eine neue Anfrage.
  it('bleibt von der Reihenfolge der Kennungen und Zuordnungen unberührt', () => {
    expect(
      queryKeys.instanceMigrationPreview(['instance-2', 'instance-1'], { B: 'y', A: 'x' }),
    ).toEqual(queryKeys.instanceMigrationPreview(['instance-1', 'instance-2'], { A: 'x', B: 'y' }));
  });

  // Testzweck: Wer seine letzte Wahl wieder leert, steht beim Stand des ersten Öffnens.
  // Beides muss derselbe Schlüssel sein, sonst prüft dieselbe Frage zweimal.
  it('hält eine leere Zuordnung für keine Zuordnung', () => {
    expect(queryKeys.instanceMigrationPreview(['instance-1'], {})).toEqual(
      queryKeys.instanceMigrationPreview(['instance-1']),
    );
  });

  // Testzweck: Die Zuordnung entscheidet, welche Instanzen überhaupt migrieren. Käme sie
  // nicht am Endpunkt an, bliebe genau die Instanz zurück, für die sie gesetzt wurde.
  it('reicht die Zuordnung an die Migration weiter', async () => {
    const { wrapper } = setup();
    const { result } = renderHook(() => useMigrateInstances(), { wrapper });

    result.current.mutate({
      instanceIds: ['instance-1'],
      targetDefinitionId: 'definition-2',
      flowNodeMapping: { Review: 'Freigabe' },
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mocks.migrate).toHaveBeenCalledWith(['instance-1'], 'definition-2', { Review: 'Freigabe' });
  });
});
