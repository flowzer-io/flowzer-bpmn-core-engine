import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { PropsWithChildren } from 'react';
import { describe, expect, it, vi } from 'vitest';

import { FlowzerClient } from '@flowzer/sdk';
import {
  FlowzerProvider,
  clearFlowzerScope,
  flowzerQueryKeys,
  useFlowzer,
  useInstanceHistory,
  useTaskFormData,
  useFormSectionActions,
  useFormSectionDraft,
  useUserTaskActions,
  useUserTasks,
  useUserTaskWorkspace,
} from './index.js';

function response(result: unknown, status = 200) {
  return new Response(JSON.stringify(status < 400
    ? { successful: true, result }
    : { title: 'Fehler', status }), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function setup(fetch: typeof globalThis.fetch, sessionScope = 'session-a') {
  const client = new FlowzerClient({ baseUrl: '/api', fetch });
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: 4 } },
  });
  const wrapper = ({ children }: PropsWithChildren) => (
    <QueryClientProvider client={queryClient}>
      <FlowzerProvider client={client} cacheNamespace="installation-a" sessionScope={sessionScope}>
        {children}
      </FlowzerProvider>
    </QueryClientProvider>
  );
  return { client, queryClient, wrapper };
}

describe('@flowzer/react', () => {
  // Testzweck: Hooks dürfen nicht unbemerkt auf einen globalen Client zurückfallen,
  // weil dies Daten zwischen Installationen oder Sitzungen vermischen könnte.
  it('verlangt einen expliziten FlowzerProvider', () => {
    expect(() => renderHook(() => useFlowzer())).toThrow('FlowzerProvider');
  });

  // Testzweck: Cache-Schlüssel trennen Installation und Host-Sitzung, enthalten aber
  // weder Token noch E-Mail oder andere Authentisierungsdaten.
  it('trennt Query-Keys nach Installation und nicht geheimem Sitzungsscope', () => {
    expect(flowzerQueryKeys.userTasks('installation-a', 'session-a')).toEqual([
      'flowzer', 'installation-a', 'session-a', 'user-tasks',
    ]);
    expect(flowzerQueryKeys.userTasks('installation-a', 'session-b')).not.toEqual(
      flowzerQueryKeys.userTasks('installation-a', 'session-a'),
    );
  });

  // Testzweck: Ein Host-Logout entfernt nur den früheren Sitzungsscope und lässt
  // andere Installationen oder parallele Sitzungen im gemeinsamen QueryClient unberührt.
  it('entfernt den Cache einer beendeten Sitzung gezielt', () => {
    const queryClient = new QueryClient();
    const oldKey = flowzerQueryKeys.userTasks('installation-a', 'session-old');
    const otherKey = flowzerQueryKeys.userTasks('installation-a', 'session-other');
    queryClient.setQueryData(oldKey, [{ id: 'old' }]);
    queryClient.setQueryData(otherKey, [{ id: 'other' }]);

    clearFlowzerScope(queryClient, 'installation-a', 'session-old');

    expect(queryClient.getQueryData(oldKey)).toBeUndefined();
    expect(queryClient.getQueryData(otherKey)).toEqual([{ id: 'other' }]);
  });

  // Testzweck: Die Historie wird erst nach Freischaltung durch die einbettende Oberfläche
  // geladen und bei einem Entzug vollständig aus dem Sitzungscache entfernt.
  it('lädt die History nur nach Freischaltung und entfernt sie bei Entzug', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response({
      instanceId: 'instance-1',
      events: [],
    }));
    const { queryClient, wrapper } = setup(fetch);
    const { result, rerender } = renderHook(
      ({ enabled }) => useInstanceHistory('instance-1', { enabled }),
      { wrapper, initialProps: { enabled: false } },
    );
    expect(result.current.isFetching).toBe(false);
    expect(fetch).not.toHaveBeenCalled();

    rerender({ enabled: true });
    await waitFor(() => expect(result.current.data?.instanceId).toBe('instance-1'));
    expect(fetch).toHaveBeenCalledOnce();

    rerender({ enabled: false });
    await waitFor(() => expect(result.current.data).toBeUndefined());
    await waitFor(() => expect(queryClient.getQueryData(
      flowzerQueryKeys.instanceHistory('installation-a', 'session-a', 'instance-1'),
    )).toBeUndefined());
  });

  // Testzweck: Ohne serverseitiges Arbeitsrecht lädt der Arbeitsbereich weder
  // Formular noch privaten Entwurf und verrät dadurch keine zusätzlichen Daten.
  it('lädt Formular und Entwurf nicht ohne canWork', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response({
      id: 'task-1',
      token: {},
      workState: { revision: 0, canWork: false },
    }));
    const { wrapper } = setup(fetch);

    const { result } = renderHook(() => useUserTaskWorkspace('task-1'), { wrapper });

    await waitFor(() => expect(result.current.task?.id).toBe('task-1'));
    expect(result.current.canWork).toBe(false);
    expect(fetch).toHaveBeenCalledOnce();
    expect(fetch.mock.calls[0]![0]).toBe('/api/usertask/task-1');
  });

  // Testzweck: Mit Arbeitsrecht werden gebundenes Formular und privater Entwurf
  // über genau den geladenen Taskkontext parallel bereitgestellt.
  it('lädt den vollständigen berechtigten Task-Arbeitsbereich', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/form')) return response({ id: 'form-1', formData: '{}' });
      if (url.endsWith('/draft')) return response({ userTaskId: 'task-1', revision: 2, data: { note: 'offen' } });
      return response({ id: 'task-1', token: {}, workState: { revision: 4, canWork: true } });
    });
    const { wrapper } = setup(fetch);

    const { result } = renderHook(() => useUserTaskWorkspace('task-1'), { wrapper });

    await waitFor(() => expect(result.current.form?.id).toBe('form-1'));
    expect(result.current.draft?.revision).toBe(2);
    expect(fetch.mock.calls.map(([url]) => String(url))).toEqual(expect.arrayContaining([
      '/api/usertask/task-1',
      '/api/usertask/task-1/form',
      '/api/usertask/task-1/draft',
    ]));
  });

  // Testzweck: Entzieht der Server bei einem Refetch das Arbeitsrecht, verschwinden
  // bereits geladene Formular-/Entwurfsdaten und werden nicht noch einmal angefragt.
  it('entfernt geschützte Arbeitsdaten nach einem Rechteentzug', async () => {
    let canWork = true;
    const fetch = vi.fn<typeof globalThis.fetch>().mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/form')) return response({ id: 'form-1', formData: '{}' });
      if (url.endsWith('/draft')) return response({ userTaskId: 'task-1', revision: 1, data: {} });
      return response({ id: 'task-1', token: {}, workState: { revision: 1, canWork } });
    });
    const { wrapper } = setup(fetch);
    const { result } = renderHook(() => useUserTaskWorkspace('task-1'), { wrapper });
    await waitFor(() => expect(result.current.form?.id).toBe('form-1'));
    const contentCalls = fetch.mock.calls.length;

    canWork = false;
    await act(async () => { await result.current.reload(); });

    await waitFor(() => expect(result.current.canWork).toBe(false));
    expect(result.current.form).toBeUndefined();
    expect(result.current.draft).toBeUndefined();
    expect(fetch).toHaveBeenCalledTimes(contentCalls + 1);
  });

  // Testzweck: Ein ausdrücklicher Draft-Reload gibt den frisch vom Server
  // gelieferten Entwurf zurück, damit Hosts ihren lokalen Formularstand gezielt abgleichen können.
  it('liefert beim erneuten Laden den aktuellen Entwurf zurück', async () => {
    let revision = 1;
    const fetch = vi.fn<typeof globalThis.fetch>().mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/form')) return response({ id: 'form-1', formData: '{}' });
      if (url.endsWith('/draft')) return response({ userTaskId: 'task-1', revision, data: { note: `stand-${revision}` } });
      return response({ id: 'task-1', token: {}, workState: { revision: 1, canWork: true } });
    });
    const { wrapper } = setup(fetch);
    const { result } = renderHook(() => useUserTaskWorkspace('task-1'), { wrapper });

    await waitFor(() => expect(result.current.draft?.revision).toBe(1));
    revision = 2;

    let reloadedDraft: Awaited<ReturnType<typeof result.current.reloadDraft>>;
    await act(async () => { reloadedDraft = await result.current.reloadDraft(); });

    expect(reloadedDraft).toMatchObject({ revision: 2, data: { note: 'stand-2' } });
    await waitFor(() => expect(result.current.draft).toMatchObject({
      revision: 2,
      data: { note: 'stand-2' },
    }));
  });

  // Testzweck: Ein ausdrücklicher Draft-Reload darf einen Serverfehler nicht als
  // erfolgreichen, aber leeren Stand tarnen, weil der Host sonst lokale Eingaben verwirft.
  it('reicht Fehler beim erneuten Laden des Entwurfs an den Host weiter', async () => {
    let failReload = false;
    const fetch = vi.fn<typeof globalThis.fetch>().mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/form')) return response({ id: 'form-1', formData: '{}' });
      if (url.endsWith('/draft')) {
        return failReload
          ? response(null, 503)
          : response({ userTaskId: 'task-1', revision: 1, data: { note: 'server' } });
      }
      return response({ id: 'task-1', token: {}, workState: { revision: 1, canWork: true } });
    });
    const { wrapper } = setup(fetch);
    const { result } = renderHook(() => useUserTaskWorkspace('task-1'), { wrapper });
    await waitFor(() => expect(result.current.draft?.revision).toBe(1));
    failReload = true;

    await act(async () => {
      await expect(result.current.reloadDraft()).rejects.toThrow('Fehler');
    });
  });

  // Testzweck: Jede Lifecycle-Mutation muss bei einem 409 auch aktive Detail- und
  // Listen-Queries neu laden, damit der Host den maßgeblichen Serverstand anzeigt.
  it.each([
    ['claim', (actions: ReturnType<typeof useUserTaskActions>) => actions.claim.mutateAsync({ expectedRevision: 1 })],
    ['release', (actions: ReturnType<typeof useUserTaskActions>) => actions.release.mutateAsync({ expectedRevision: 1, reason: 'Zurückgeben' })],
    ['assign', (actions: ReturnType<typeof useUserTaskActions>) => actions.assign.mutateAsync({ expectedRevision: 1, reason: 'Neu zuweisen', assignee: { id: '00000000-0000-0000-0000-000000000001', kind: 'user' } })],
    ['delegate', (actions: ReturnType<typeof useUserTaskActions>) => actions.delegate.mutateAsync({ expectedRevision: 1, reason: 'Delegieren', assignee: { id: '00000000-0000-0000-0000-000000000002', kind: 'user' } })],
  ])('aktualisiert Taskdetail und -liste nach einem %s-Konflikt', async (_action, mutate) => {
    let conflictOccurred = false;
    const fetch = vi.fn<typeof globalThis.fetch>().mockImplementation(async (input) => {
      const url = String(input);
      if (/\/usertask\/task-1\/(claim|release|assign|delegate)$/.test(url)) {
        conflictOccurred = true;
        return response(null, 409);
      }
      const revision = conflictOccurred ? 2 : 1;
      const task = { id: 'task-1', token: {}, workState: { revision, canWork: false } };
      if (url === '/api/usertask/task-1') return response(task);
      if (url === '/api/usertask') return response([task]);
      throw new Error(`Unerwarteter Request: ${url}`);
    });
    const { wrapper } = setup(fetch);
    const { result } = renderHook(() => ({
      list: useUserTasks(),
      workspace: useUserTaskWorkspace('task-1'),
      actions: useUserTaskActions('task-1'),
    }), { wrapper });

    await waitFor(() => expect(result.current.list.data?.[0]?.workState?.revision).toBe(1));
    await waitFor(() => expect(result.current.workspace.task?.workState?.revision).toBe(1));

    await act(async () => {
      await expect(mutate(result.current.actions)).rejects.toThrow();
    });

    await waitFor(() => expect(result.current.list.data?.[0]?.workState?.revision).toBe(2));
    await waitFor(() => expect(result.current.workspace.task?.workState?.revision).toBe(2));
    expect(fetch.mock.calls.map(([url]) => String(url))).toEqual(expect.arrayContaining([
      '/api/usertask',
      '/api/usertask/task-1',
    ]));
  });

  // Testzweck: Human-Task-Mutationen werden auch dann nicht automatisch wiederholt,
  // wenn der Host-QueryClient global Wiederholungen für Mutationen konfiguriert hat.
  it('deaktiviert automatische Wiederholungen für Task-Mutationen', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response(null, 503));
    const { wrapper } = setup(fetch);
    const { result } = renderHook(() => useUserTaskActions('task-1'), { wrapper });

    await act(async () => {
      await expect(result.current.claim.mutateAsync({ expectedRevision: 0 })).rejects.toThrow();
    });

    expect(fetch).toHaveBeenCalledOnce();
  });

  // Testzweck: Der Abschnittseditor liest seinen Entwurf in einem eigenen,
  // sitzungsgetrennten Cache und vermischt ihn deshalb nicht mit Human-Task-Drafts.
  it('lädt einen Formularabschnittsentwurf über einen getrennten Query-Key', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response({
      sectionId: 'section-1', revision: 2, hasDraft: true, sectionData: '{"components":[]}',
    }));
    const { wrapper } = setup(fetch);
    const { result } = renderHook(() => useFormSectionDraft('section-1'), { wrapper });

    await waitFor(() => expect(result.current.data?.revision).toBe(2));
    expect(fetch.mock.calls[0]![0]).toBe('/api/form-section/section-1/draft');
    expect(flowzerQueryKeys.formSectionDraft('installation-a', 'session-a', 'section-1')).toEqual([
      'flowzer', 'installation-a', 'session-a', 'form-sections', 'section-1', 'draft',
    ]);
  });

  // Testzweck: Der darstellungsfreie Editor überträgt die erwartete Revision
  // unverändert und deaktiviert automatische Wiederholungen für CAS-Mutationen.
  it('speichert einen Abschnittsentwurf revisionsgebunden ohne automatische Wiederholung', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response({
      sectionId: 'section-1', revision: 3, hasDraft: true, sectionData: '{"components":[]}',
    }));
    const { wrapper } = setup(fetch);
    const { result } = renderHook(() => useFormSectionActions('section-1'), { wrapper });

    await act(async () => {
      await result.current.saveDraft.mutateAsync({
        expectedRevision: 2,
        sectionData: '{"components":[]}',
      });
    });

    expect(fetch).toHaveBeenCalledOnce();
    expect(fetch.mock.calls[0]![0]).toBe('/api/form-section/section-1/draft');
    expect(JSON.parse(String(fetch.mock.calls[0]![1]?.body))).toEqual({
      expectedRevision: 2,
      sectionData: '{"components":[]}',
    });
  });

  // Testzweck: Ein manueller Abschluss übergibt Task-Revision und den vom Host
  // verwalteten stabilen Idempotenzschlüssel unverändert an das Headless-SDK.
  it('schließt mit explizitem stabilen Idempotenzschlüssel ab', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response(null));
    const { wrapper } = setup(fetch);
    const { result } = renderHook(() => useUserTaskActions('task-1'), { wrapper });

    await act(async () => {
      await result.current.complete.mutateAsync({
        command: { flowNodeId: 'review', tokenId: 'token-1', expectedTaskRevision: 3 },
        options: { idempotencyKey: 'host-operation-42' },
      });
    });

    const [, init] = fetch.mock.calls[0]!;
    expect(new Headers(init?.headers).get('Idempotency-Key')).toBe('host-operation-42');
    expect(JSON.parse(String(init?.body))).toMatchObject({ expectedTaskRevision: 3 });
  });

  // Testzweck: Ein Hintergrund-Refetch darf lokale Formulareingaben nicht durch einen
  // neuen Serverstand ersetzen; nur ein ausdrücklicher Reset übernimmt diesen Stand.
  it('bewahrt lokale Formulardaten bis zum ausdrücklichen Reset', () => {
    const { result, rerender } = renderHook(
      ({ serverData }) => useTaskFormData('task-1', serverData),
      { initialProps: { serverData: { note: 'server-alt' } } },
    );
    act(() => result.current.setData({ note: 'lokal' }));

    rerender({ serverData: { note: 'server-neu' } });

    expect(result.current.data).toEqual({ note: 'lokal' });
    expect(result.current.isDirty).toBe(true);
    act(() => result.current.resetToServer());
    expect(result.current.data).toEqual({ note: 'server-neu' });
    expect(result.current.isDirty).toBe(false);
  });
});
