import { describe, expect, it } from 'vitest';

import { loadRuntimeConfig, RuntimeConfigError } from './runtime';

/** Ersetzt `fetch` durch eine Antwort mit der uebergebenen Konfiguration. */
function withConfig(config: Record<string, unknown>) {
  globalThis.fetch = (async () =>
    new Response(JSON.stringify(config), { status: 200, headers: { 'content-type': 'application/json' } })) as typeof fetch;
}

describe('loadRuntimeConfig', () => {
  // Testzweck: Die Konsole kennt nur noch den serverseitigen BFF-Schalter. Browser-OIDC-
  // Werte und frei konfigurierbare Rollennamen dürfen nicht mehr Teil des Runtime-Vertrags sein.
  it('lädt bffEnabled und ignoriert alte Browser-OIDC-Werte', async () => {
    withConfig({
      apiBaseUrl: '/',
      bffEnabled: true,
      oidcAuthority: 'https://auth.example/realms/x',
      oidcClientId: 'legacy-client',
      roleNames: { operator: 'legacy.operator' },
    });

    const config = await loadRuntimeConfig();

    expect(config).toEqual({ apiBaseUrl: '', accent: 'iris', bffEnabled: true });
    expect('oidcAuthority' in config).toBe(false);
    expect('roleNames' in config).toBe(false);
  });

  // Testzweck: Das API-Ziel bleibt auf den eigenen Origin begrenzt, weil Cookies und
  // CSRF-Header des BFF nicht an einen fremden Origin gesendet werden dürfen.
  it('lehnt eine API-Adresse ohne https ab', async () => {
    withConfig({ apiBaseUrl: 'http://irgendwo.example', bffEnabled: true });

    await expect(loadRuntimeConfig()).rejects.toBeInstanceOf(RuntimeConfigError);
  });

  // Testzweck: Der Normalfall im Container ist ein relativer Pfad; er bleibt beim eigenen Origin.
  it('nimmt einen relativen Pfad und aktivierten BFF an', async () => {
    withConfig({ apiBaseUrl: '/', bffEnabled: true });

    const config = await loadRuntimeConfig();

    expect(config.apiBaseUrl).toBe('');
    expect(config.bffEnabled).toBe(true);
  });
});

describe('beschaedigte config.json', () => {
  // Testzweck: Eine vorhandene, aber unlesbare Konfiguration ist ein Betriebsfehler. Fiele
  // sie still auf die Bauwerte zurueck, startete der Container mit der Entwicklungsadresse
  // und ohne Anmeldung — und das faellt erst auf, wenn jemand vor einer leeren Oberflaeche sitzt.
  it('bricht ab, wenn die Datei da, aber kein gueltiges JSON ist', async () => {
    globalThis.fetch = (async () =>
      new Response('{ das ist kein json', {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })) as typeof fetch;

    await expect(loadRuntimeConfig()).rejects.toBeInstanceOf(RuntimeConfigError);
  });

  // Testzweck: Eine fehlende Datei bleibt der Normalfall im Entwicklungsbetrieb.
  it('nimmt die Bauwerte, wenn es die Datei nicht gibt', async () => {
    globalThis.fetch = (async () => {
      throw new TypeError('Failed to fetch');
    }) as typeof fetch;

    await expect(loadRuntimeConfig()).resolves.toBeDefined();
  });

  // Testzweck: Der Vite-Entwicklungsserver beantwortet jede unbekannte Adresse mit der
  // Startseite — also Status 200 und text/html. Das ist keine kaputte Konfiguration,
  // sondern gar keine. Wird es verwechselt, startet die Konsole in der Entwicklung nicht mehr.
  it('behandelt die Startseite des Entwicklungsservers als fehlende Datei', async () => {
    globalThis.fetch = (async () =>
      new Response('<!doctype html><html><body>Flowzer Console</body></html>', {
        status: 200,
        headers: { 'content-type': 'text/html' },
      })) as typeof fetch;

    await expect(loadRuntimeConfig()).resolves.toBeDefined();
  });
});

describe('Akzentfarbe', () => {
  // Testzweck: Die Akzentfarbe kommt aus der Bereitstellung, damit alle dieselbe sehen.
  it('uebernimmt eine bekannte Farbe aus der Konfiguration', async () => {
    withConfig({ apiBaseUrl: '/', bffEnabled: true, accent: 'emerald' });

    const config = await loadRuntimeConfig();

    expect(config.accent).toBe('emerald');
  });

  // Testzweck: Ein Tippfehler in der Farbe darf die Oberflaeche nicht am Starten hindern —
  // anders als bei der Anmeldung ist hier nichts unsicher, nur unschoen.
  it('faellt bei unbekannter Farbe still auf iris zurueck', async () => {
    withConfig({ apiBaseUrl: '/', bffEnabled: true, accent: 'knallpink' });

    const config = await loadRuntimeConfig();

    expect(config.accent).toBe('iris');
  });

  // Testzweck: Ohne Angabe bleibt es beim Standard; die Konfiguration muss nicht vollstaendig sein.
  it('nimmt iris, wenn nichts gesetzt ist', async () => {
    withConfig({ apiBaseUrl: '/', bffEnabled: true });

    const config = await loadRuntimeConfig();

    expect(config.accent).toBe('iris');
  });
});

describe('Herkunft der API-Adresse', () => {
  // Testzweck: //fremde.example beginnt mit einem Schraegstrich, ist aber protokollrelativ
  // und landet bei einem fremden Origin. Das Zugangstoken ginge dorthin.
  it('lehnt eine protokollrelative Adresse ab', async () => {
    withConfig({ apiBaseUrl: '//fremde.example', bffEnabled: true });

    await expect(loadRuntimeConfig()).rejects.toBeInstanceOf(RuntimeConfigError);
  });

  // Testzweck: Auch eine vollstaendige https-Adresse in einem anderen Origin ist keine
  // Option: Der Browser gibt den Header zur Einordnung einer Ablehnung nicht heraus.
  it('lehnt eine Adresse in einem anderen Origin ab', async () => {
    withConfig({ apiBaseUrl: 'https://fremde.example/api', bffEnabled: true });

    await expect(loadRuntimeConfig()).rejects.toBeInstanceOf(RuntimeConfigError);
  });

});
