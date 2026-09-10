import { beforeEach, describe, expect, it, vi } from 'vitest';

import { applyRuntimeConfig, clearCsrfToken, setUnauthorizedHandler } from '@/lib/api/client';
import { loadRuntimeConfig } from '@/lib/config/runtime';

import { createConsoleFlowzerClient } from './client';

describe('Console-Adapter für den öffentlichen FlowzerClient', () => {
  beforeEach(async () => {
    vi.restoreAllMocks();
    clearCsrfToken();
    setUnauthorizedHandler(() => {});
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ apiBaseUrl: '/api', bffEnabled: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    await loadRuntimeConfig();
    applyRuntimeConfig();
    vi.restoreAllMocks();
  });

  // Testzweck: Ausschließlich die Console ergänzt die BFF-Cookie-/CSRF-Details;
  // der veröffentlichte SDK-Client erhält keine Development- oder BFF-Headerlogik.
  it('bindet den öffentlichen Client über die Console an BFF und CSRF', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ requestToken: 'csrf-1', headerName: 'X-Flowzer-CSRF' })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ successful: true, result: { revision: 1 } })));

    await expect(createConsoleFlowzerClient().userTasks.claim('task-1', { expectedRevision: 0 }))
      .resolves.toEqual({ revision: 1 });

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/bff/csrf', { credentials: 'same-origin' });
    const [url, init] = fetchMock.mock.calls[1]!;
    expect(url).toBe('/api/usertask/task-1/claim');
    expect(init).toMatchObject({ method: 'POST', credentials: 'same-origin' });
    expect((init?.headers as Headers).get('X-Flowzer-CSRF')).toBe('csrf-1');
    expect((init?.headers as Headers).get('Authorization')).toBeNull();
    expect((init?.headers as Headers).get('X-Flowzer-UserId')).toBeNull();
  });

  // Testzweck: Auch Aufrufe über den öffentlichen Client beenden bei 401 dieselbe
  // Console-Sitzung, damit deren öffentliche Paketcaches anschließend entfernt werden.
  it('meldet 401 über den Console-Sitzungshandler', async () => {
    const unauthorized = vi.fn();
    setUnauthorizedHandler(unauthorized);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 401 }));

    await expect(createConsoleFlowzerClient().userTasks.list()).rejects.toMatchObject({ status: 401 });
    expect(unauthorized).toHaveBeenCalledOnce();
  });
});
