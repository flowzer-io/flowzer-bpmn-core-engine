import { describe, expect, it } from 'vitest';

import { loadRuntimeConfig, RuntimeConfigError } from './runtime';

/** Ersetzt `fetch` durch eine Antwort mit der uebergebenen Konfiguration. */
function withConfig(config: Record<string, unknown>) {
  globalThis.fetch = (async () =>
    new Response(JSON.stringify(config), { status: 200, headers: { 'content-type': 'application/json' } })) as typeof fetch;
}

describe('loadRuntimeConfig', () => {
  // Testzweck: Eine halbe Anmeldekonfiguration ist ein Betriebsfehler. Wuerde sie als
  // "ohne Anmeldung" durchgehen, zeigte die Konsole die volle Ansicht, waehrend die API
  // jeden Aufruf ablehnt.
  it('bricht bei halber Anmeldekonfiguration ab', async () => {
    withConfig({ apiBaseUrl: '/', oidcAuthority: 'https://auth.example/realms/x', oidcClientId: '' });

    await expect(loadRuntimeConfig()).rejects.toBeInstanceOf(RuntimeConfigError);
  });

  // Testzweck: Das Zugangstoken geht als Bearer an die API-Adresse. Ein unverschluesseltes
  // oder fremdes Ziel darf nicht stillschweigend uebernommen werden.
  it('lehnt eine API-Adresse ohne https ab', async () => {
    withConfig({ apiBaseUrl: 'http://irgendwo.example', oidcAuthority: '', oidcClientId: '' });

    await expect(loadRuntimeConfig()).rejects.toBeInstanceOf(RuntimeConfigError);
  });

  // Testzweck: Der Normalfall im Container ist ein relativer Pfad; er bleibt beim eigenen Origin.
  it('nimmt einen relativen Pfad und eine vollstaendige Anmeldung an', async () => {
    withConfig({
      apiBaseUrl: '/',
      oidcAuthority: 'https://auth.example/realms/x',
      oidcClientId: 'konsole',
      oidcAudience: 'flowzer-api',
    });

    const config = await loadRuntimeConfig();

    expect(config.apiBaseUrl).toBe('');
    expect(config.oidcAudience).toBe('flowzer-api');
  });
});
