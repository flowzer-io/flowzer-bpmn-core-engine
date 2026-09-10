import { afterEach, describe, expect, it, vi } from 'vitest';

import { ApiError } from './client';
import { identityDirectoryApi, userTasksApi } from './endpoints';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Task-Lifecycle-API', () => {
  // Testzweck: Claim und Release müssen ausschließlich die erwartete Taskrevision und
  // beim Zurückgeben den protokollierbaren Grund an den adressierten Task senden.
  it('sendet Claim und Release mit der Taskrevision', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({ successful: true, result: workState(2) }))
      .mockResolvedValueOnce(Response.json({ successful: true, result: workState(3) }));
    vi.stubGlobal('fetch', fetchMock);

    await userTasksApi.claim('task/42', { expectedRevision: 1 });
    await userTasksApi.release('task/42', { expectedRevision: 2, reason: 'Zurück an das Team' });

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/usertask/task%2F42/claim');
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({ expectedRevision: 1 });
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/usertask/task%2F42/release');
    expect(JSON.parse(String(fetchMock.mock.calls[1]?.[1]?.body))).toEqual({
      expectedRevision: 2,
      reason: 'Zurück an das Team',
    });
  });

  // Testzweck: Zuweisung und Delegation übertragen eine typisierte Benutzerreferenz,
  // aber weder Anzeigenamen noch einen vom Browser behaupteten Akteur.
  it('sendet Assign und Delegate mit stabiler Benutzerreferenz und Grund', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({ successful: true, result: workState(2) }))
      .mockResolvedValueOnce(Response.json({ successful: true, result: workState(3) }));
    vi.stubGlobal('fetch', fetchMock);
    const assignee = { kind: 'user' as const, id: 'user-anna' };

    await userTasksApi.assign('task-1', { expectedRevision: 1, assignee, reason: 'Betrieblich nötig' });
    await userTasksApi.delegate('task-1', { expectedRevision: 2, assignee, reason: 'Vertretung' });

    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({
      expectedRevision: 1,
      assignee,
      reason: 'Betrieblich nötig',
    });
    expect(JSON.parse(String(fetchMock.mock.calls[1]?.[1]?.body))).toEqual({
      expectedRevision: 2,
      assignee,
      reason: 'Vertretung',
    });
  });

  // Testzweck: Die Laufzeitauswahl muss task- und aktionsgebunden bleiben; die breitere
  // Workflow-/Modeler-Suche darf für eine Neuzuweisung nicht verwendet werden.
  it('sucht aktive Bearbeiter im Taskkontext', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ successful: true, result: { generationId: 'g-1', items: [] } }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await identityDirectoryApi.searchTaskAssignees('task/42', 'Anna Beispiel', 'delegate');

    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      '/api/identity-directory/user-tasks/task%2F42/assignees?query=Anna+Beispiel&action=delegate&limit=20',
    );
  });

  // Testzweck: Ein konkurrierender Claim bleibt als 409 erkennbar, damit die Oberfläche
  // keine Eingaben verwirft und ausdrücklich einen neuen Taskstand laden kann.
  it('reicht Revisionskonflikte als ApiError weiter', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      Response.json({ code: 'user_task.revision_conflict', title: 'Conflict' }, { status: 409 }),
    ));

    const request = userTasksApi.claim('task-1', { expectedRevision: 0 });

    await expect(request).rejects.toBeInstanceOf(ApiError);
    await expect(request).rejects.toMatchObject({ status: 409 });
  });

  // Testzweck: Auch der Abschluss wird an die Lifecycle-Revision gebunden, damit ein
  // alter Tab nach Release oder Delegation nicht mit veralteten Rechten abschließt.
  it('sendet beim Abschluss die erwartete Taskrevision', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ successful: true }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await userTasksApi.complete({
      flowNodeId: 'Task_1',
      tokenId: 'token-1',
      processInstanceId: 'instance-1',
      expectedTaskRevision: 8,
      data: { approved: true },
    });

    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toMatchObject({
      expectedTaskRevision: 8,
    });
  });
});

function workState(revision: number) {
  return {
    revision,
    claimed: false,
    actualAssignee: null,
    actualAssigneeDisplayName: null,
    isAssignedToCurrentUser: false,
    canWork: false,
    canClaim: true,
    canRelease: false,
    canAssign: false,
    canDelegate: false,
  };
}
