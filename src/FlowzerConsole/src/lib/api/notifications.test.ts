import { afterEach, describe, expect, it, vi } from 'vitest';

import { notificationsApi } from './endpoints';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('notificationsApi', () => {
  // Testzweck: GET liefert den persistenten Feed und POST quittiert exakt die ausgewählte ID.
  it('lädt den Feed und markiert eine Meldung serverseitig gelesen', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({ successful: true, result: [] }))
      .mockResolvedValueOnce(Response.json({ successful: true, result: null }));
    vi.stubGlobal('fetch', fetchMock);

    await notificationsApi.list();
    await notificationsApi.markRead('notification/42');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/notifications');
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/notifications/notification%2F42/read');
    expect(fetchMock.mock.calls[1]?.[1]).toMatchObject({ method: 'POST' });
  });
});
