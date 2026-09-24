import { beforeEach, describe, expect, it, vi } from 'vitest';

import { clearCsrfToken } from '@/lib/api/client';
import { loadRuntimeConfig } from '@/lib/config/runtime';

import { buildLoginUrl, fetchSession, logout, sanitiseReturnTo } from './bff';

describe('BFF-Authentifizierung', () => {
  beforeEach(async () => {
    vi.restoreAllMocks();
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

  // Testzweck: Ein Login darf nur zu einer lokalen Route zurückkehren; externe Ziele
  // würden den BFF zu einem Open-Redirect machen.
  it('baut die Login-Adresse mit sicherem lokalem returnTo', () => {
    expect(buildLoginUrl('/workflows?search=x')).toBe('/bff/login?returnTo=%2Fworkflows%3Fsearch%3Dx');
    expect(buildLoginUrl('//evil.example')).toBe('/bff/login?returnTo=%2F');
  });

  // Testzweck: Die Session-Antwort enthält nur das datensparsame BFF-DTO und wird
  // mit Same-Origin-Credentials geladen, damit das HttpOnly-Cookie greift.
  it('lädt die Session über den BFF', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ id: 'u-1', name: 'Ada', capabilities: ['access'] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );

    await expect(fetchSession()).resolves.toEqual({ id: 'u-1', name: 'Ada', capabilities: ['access'] });
    expect(fetchMock).toHaveBeenCalledWith('/bff/session', { credentials: 'same-origin' });
  });

  // Testzweck: Logout ist eine zustandsändernde BFF-Aktion und muss deshalb per POST
  // statt durch eine navigierbare GET-Adresse ausgelöst werden.
  it('meldet über POST /bff/logout ab', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ requestToken: 'csrf-logout', headerName: 'X-Flowzer-CSRF' }), {
          status: 200,
        }),
      )
      .mockResolvedValueOnce(new Response(null, { status: 204 }));

    await expect(logout()).resolves.toBeUndefined();

    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/bff/logout', {
      method: 'POST',
      headers: { Accept: 'application/json, text/plain', 'X-Flowzer-CSRF': 'csrf-logout' },
      body: undefined,
      signal: undefined,
      credentials: 'same-origin',
    });
  });

  // Testzweck: Mit Provider-Logout antwortet der BFF mit 200 und der Abmeldeadresse des
  // Identity Providers; logout() reicht sie an den Aufrufer weiter.
  it('liefert die Abmeldeadresse des Identity Providers', async () => {
    const redirectTo =
      'https://idp.example/realms/r/protocol/openid-connect/logout?id_token_hint=x&post_logout_redirect_uri=https%3A%2F%2Fflowzer.example%2F&client_id=c';
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ requestToken: 'csrf-logout', headerName: 'X-Flowzer-CSRF' }), { status: 200 }),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ redirectTo }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      );

    await expect(logout()).resolves.toBe(redirectTo);
  });

  // Testzweck: Eine bereits beendete Sitzung (401) gilt als abgemeldet und liefert kein
  // Navigationsziel; eine 200-Antwort ohne verwertbares Ziel ebenfalls nicht.
  it('liefert ohne verwertbare Abmeldeadresse kein Ziel', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ requestToken: 'csrf-logout', headerName: 'X-Flowzer-CSRF' }), { status: 200 }),
      )
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ requestToken: 'csrf-logout', headerName: 'X-Flowzer-CSRF' }), { status: 200 }),
      )
      .mockResolvedValueOnce(new Response(JSON.stringify({ redirectTo: 42 }), { status: 200 }));

    await expect(logout()).resolves.toBeUndefined();
    await expect(logout()).resolves.toBeUndefined();
  });

  // Testzweck: Die Return-To-Hilfe akzeptiert weder absolute noch protokollrelative
  // Ziele, damit jede Anmeldung im eigenen SPA bleibt.
  it('sanitisiert lokale Return-To-Ziele', () => {
    expect(sanitiseReturnTo('/instances')).toBe('/instances');
    expect(sanitiseReturnTo('https://evil.example')).toBe('/');
  });
});
