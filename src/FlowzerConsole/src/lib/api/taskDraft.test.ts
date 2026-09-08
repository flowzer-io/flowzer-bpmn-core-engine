import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError } from './client';
import { userTasksApi } from './endpoints';

describe('Task-Draft-API', () => {
  beforeEach(() => vi.restoreAllMocks());

  // Testzweck: Der lesende Draft-Endpunkt verwendet den vollständigen Task-Schlüssel
  // und entpackt den bestehenden ApiStatusResult-Vertrag.
  it('lädt einen Draft über die codierte Task-ID', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        successful: true,
        result: { userTaskId: 'task/a', revision: 2, updatedAtUtc: null, data: { reason: 'x' } },
      }), { status: 200 }),
    );

    const result = await userTasksApi.getDraft('task/a');

    expect(result.revision).toBe(2);
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/usertask/task%2Fa/draft');
  });

  // Testzweck: Speichern und Verwerfen führen genau die vom Server erwarteten
  // Revisionen mit; Verwerfen verwendet den Query-Parameter statt eines Bodies.
  it('sendet PUT- und DELETE-Verträge korrekt', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        successful: true,
        result: { userTaskId: 'task-1', revision: 3, updatedAtUtc: 'now', data: { reason: 'x' } },
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ successful: true }), { status: 200 }));

    await userTasksApi.saveDraft('task-1', { expectedRevision: 2, data: { reason: 'x' } });
    await userTasksApi.deleteDraft('task-1', 3);

    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ method: 'PUT' });
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({
      expectedRevision: 2,
      data: { reason: 'x' },
    });
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/usertask/task-1/draft?expectedRevision=3');
    expect(fetchMock.mock.calls[1]?.[1]).toMatchObject({ method: 'DELETE' });
  });

  // Testzweck: Ein serverseitiger Optimistic-Concurrency-Konflikt bleibt ein ApiError
  // mit Status 409 und kann dadurch im Editor separat dargestellt werden.
  it('reicht 409-Konflikte unverändert weiter', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ title: 'Draft conflict' }), { status: 409, statusText: 'Conflict' }),
    );

    const request = userTasksApi.saveDraft('task-1', { expectedRevision: 1, data: {} });
    await expect(request).rejects.toMatchObject({ status: 409 });
    try {
      await request;
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
    }
  });
});
