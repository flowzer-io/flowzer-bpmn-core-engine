import { beforeEach, describe, expect, it, vi } from 'vitest';

import { clearCsrfToken, request, setUnauthorizedHandler } from './client';
import { loadRuntimeConfig } from '../config/runtime';

describe('API-Client mit BFF-CSRF', () => {
  beforeEach(async () => {
    vi.restoreAllMocks();
    setUnauthorizedHandler(() => {});
    clearCsrfToken();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ apiBaseUrl: '/api', bffEnabled: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    await loadRuntimeConfig();
    vi.restoreAllMocks();
  });

  // Testzweck: Lesende Aufrufe benötigen keinen CSRF-Token und dürfen niemals einen
  // Bearer- oder Entwicklungs-Header aus dem Browser mitsenden.
  it('lädt GET ohne CSRF und Authorization mit Same-Origin-Credentials', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } }),
    );

    await request('/health');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [, init] = fetchMock.mock.calls[0]!;
    expect(init).toMatchObject({ method: 'GET', credentials: 'same-origin' });
    expect((init?.headers as Record<string, string>).Authorization).toBeUndefined();
    expect((init?.headers as Record<string, string>)['X-Flowzer-UserId']).toBeUndefined();
  });

  // Testzweck: Schreibende Aufrufe holen CSRF nur einmal im Speicher und senden den
  // vom Server benannten Header; dadurch bleibt das Request-Token aus Storage heraus.
  it('holt CSRF vor POST und verwendet den Server-Header', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ requestToken: 'csrf-1', headerName: 'X-CSRF-Token' }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      )
      .mockResolvedValueOnce(new Response(JSON.stringify({ ok: true }), { status: 200 }));

    await request('/jobs', { method: 'POST', body: { name: 'x' } });

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/bff/csrf', { credentials: 'same-origin' });
    expect(fetchMock.mock.calls[1]![1]).toMatchObject({ method: 'POST', credentials: 'same-origin' });
    expect((fetchMock.mock.calls[1]![1]?.headers as Record<string, string>)['X-CSRF-Token']).toBe('csrf-1');
  });

  // Testzweck: Parallele Mutationen teilen sich die erstmalige CSRF-Anfrage; zwei gleichzeitig
  // gesetzte Antiforgery-Cookies koennten sonst eines der beiden Request-Tokens ungueltig machen.
  it('dedupliziert parallele CSRF-Anfragen', async () => {
    let releaseCsrf!: () => void;
    const csrfReady = new Promise<void>((resolve) => {
      releaseCsrf = resolve;
    });
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (input === '/bff/csrf') {
        await csrfReady;
        return new Response(JSON.stringify({ requestToken: 'shared', headerName: 'X-Flowzer-CSRF' }), { status: 200 });
      }
      return new Response(JSON.stringify({ ok: true }), { status: 200 });
    });

    const first = request('/one', { method: 'POST' });
    const second = request('/two', { method: 'POST' });
    releaseCsrf();
    await Promise.all([first, second]);

    expect(fetchMock.mock.calls.filter(([input]) => input === '/bff/csrf')).toHaveLength(1);
  });

  // Testzweck: Der explizite lokale Development-Modus ohne BFF bleibt fuer UI-Smokes
  // nutzbar und erzeugt keinen Aufruf an einen dort absichtlich nicht vorhandenen CSRF-Endpunkt.
  it('überspringt CSRF nur bei explizit deaktiviertem BFF', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ apiBaseUrl: '/api', bffEnabled: false }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    await loadRuntimeConfig();
    // Vitest 5 gibt beim erneuten spyOn denselben aktiven Spy zurück. Der reine
    // Konfigurationsaufruf darf deshalb nicht als eigentlicher API-Request zählen.
    vi.restoreAllMocks();
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }));

    await request('/local-only', { method: 'POST' });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith('/api/local-only', expect.objectContaining({ method: 'POST' }));
    expect((fetchMock.mock.calls[0]![1]?.headers as Record<string, string>)['X-Flowzer-UserId'])
      .toBe('d266f2b6-e96e-4d4a-9c20-c8e541394df0');
  });

  // Testzweck: Eine abgelaufene BFF-Session wird an den Session-Store gemeldet, damit
  // die Oberfläche sofort anonym statt mit veralteten Fähigkeiten weiterläuft.
  it('meldet 401 als anonyme Sitzung', async () => {
    const unauthorized = vi.fn();
    setUnauthorizedHandler(unauthorized);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 401, statusText: 'Unauthorized' }));

    await expect(request('/jobs')).rejects.toMatchObject({ status: 401 });
    expect(unauthorized).toHaveBeenCalledOnce();
  });
});
