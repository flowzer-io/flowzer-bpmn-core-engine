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

    await logout();

    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/bff/logout', {
      method: 'POST',
      headers: { Accept: 'application/json, text/plain', 'X-Flowzer-CSRF': 'csrf-logout' },
      body: undefined,
      signal: undefined,
      credentials: 'same-origin',
    });
  });

  // Testzweck: Die Return-To-Hilfe akzeptiert weder absolute noch protokollrelative
  // Ziele, damit jede Anmeldung im eigenen SPA bleibt.
  it('sanitisiert lokale Return-To-Ziele', () => {
    expect(sanitiseReturnTo('/instances')).toBe('/instances');
    expect(sanitiseReturnTo('https://evil.example')).toBe('/');
  });
});
