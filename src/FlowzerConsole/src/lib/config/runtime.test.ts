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
    withConfig({ apiBaseUrl: '/', oidcAuthority: '', oidcClientId: '', accent: 'emerald' });

    const config = await loadRuntimeConfig();

    expect(config.accent).toBe('emerald');
  });

  // Testzweck: Ein Tippfehler in der Farbe darf die Oberflaeche nicht am Starten hindern —
  // anders als bei der Anmeldung ist hier nichts unsicher, nur unschoen.
  it('faellt bei unbekannter Farbe still auf iris zurueck', async () => {
    withConfig({ apiBaseUrl: '/', oidcAuthority: '', oidcClientId: '', accent: 'knallpink' });

    const config = await loadRuntimeConfig();

    expect(config.accent).toBe('iris');
  });

  // Testzweck: Ohne Angabe bleibt es beim Standard; die Konfiguration muss nicht vollstaendig sein.
  it('nimmt iris, wenn nichts gesetzt ist', async () => {
    withConfig({ apiBaseUrl: '/', oidcAuthority: '', oidcClientId: '' });

    const config = await loadRuntimeConfig();

    expect(config.accent).toBe('iris');
  });
});

describe('Herkunft der API-Adresse', () => {
  // Testzweck: //fremde.example beginnt mit einem Schraegstrich, ist aber protokollrelativ
  // und landet bei einem fremden Origin. Das Zugangstoken ginge dorthin.
  it('lehnt eine protokollrelative Adresse ab', async () => {
    withConfig({ apiBaseUrl: '//fremde.example', oidcAuthority: '', oidcClientId: '' });

    await expect(loadRuntimeConfig()).rejects.toBeInstanceOf(RuntimeConfigError);
  });

  // Testzweck: Auch eine vollstaendige https-Adresse in einem anderen Origin ist keine
  // Option: Der Browser gibt den Header zur Einordnung einer Ablehnung nicht heraus.
  it('lehnt eine Adresse in einem anderen Origin ab', async () => {
    withConfig({ apiBaseUrl: 'https://fremde.example/api', oidcAuthority: '', oidcClientId: '' });

    await expect(loadRuntimeConfig()).rejects.toBeInstanceOf(RuntimeConfigError);
  });

  // Testzweck: Abweichende Rollennamen kommen aus der Konfiguration; sonst zeigte die
  // Oberflaeche trotz Berechtigung die reduzierte Ansicht.
  it('uebernimmt abweichende Rollennamen', async () => {
    withConfig({ apiBaseUrl: '/', oidcAuthority: '', oidcClientId: '', roleNames: { modeler: 'flowzer.modeler' } });

    const config = await loadRuntimeConfig();

    expect(config.roleNames.modeler).toBe('flowzer.modeler');
    expect(config.roleNames.operator).toBe('operator');
  });
});
