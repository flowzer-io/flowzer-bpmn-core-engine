import { describe, expect, it, vi } from 'vitest';

import { FlowzerApiError, FlowzerClient } from './index.js';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

describe('FlowzerClient', () => {
  // Testzweck: Ein externer Host kann Aufgaben mit einem kurzlebig gelieferten
  // Bearer-Token laden, ohne dass der SDK globalen Auth-Zustand speichert.
  it('lädt Aufgaben über einen hostseitigen Bearer-Callback', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: [{ id: 'task-1', name: 'Prüfen' }],
    }));
    const client = new FlowzerClient({
      baseUrl: 'https://flowzer.example/api/',
      fetch,
      auth: { kind: 'bearer', getAccessToken: async () => 'short-lived-token' },
    });

    await expect(client.userTasks.list()).resolves.toEqual([{ id: 'task-1', name: 'Prüfen' }]);
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe('https://flowzer.example/api/usertask');
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer short-lived-token');
    expect(init?.credentials).toBe('omit');
  });

  // Testzweck: Auch eine sehr lange Folge abschließender Schrägstriche wird linear
  // normalisiert und landet nicht in einem rückverfolgenden regulären Ausdruck.
  it('normalisiert viele abschließende Schrägstriche ohne den Requestpfad zu verändern', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: [],
    }));
    const client = new FlowzerClient({ baseUrl: `/api${'/'.repeat(20_000)}`, fetch });

    await client.userTasks.list();

    expect(fetch.mock.calls[0]![0]).toBe('/api/usertask');
  });

  // Testzweck: Cookiegebundene Einbettungen senden bei Mutationen nur den vom Host
  // gelieferten CSRF-Header und niemals zusätzlich einen Bearer-Token.
  it('schützt Cookie-Mutationen mit einem hostseitigen CSRF-Callback', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: { revision: 3 },
    }));
    const client = new FlowzerClient({
      baseUrl: '/api',
      fetch,
      auth: {
        kind: 'cookie',
        getCsrfToken: async () => ({ headerName: 'X-Flowzer-CSRF', requestToken: 'csrf-1' }),
      },
    });

    await client.userTasks.release('task/id', { expectedRevision: 2, reason: 'Übergabe' });

    const [url, init] = fetch.mock.calls[0]!;
    const headers = new Headers(init?.headers);
    expect(url).toBe('/api/usertask/task%2Fid/release');
    expect(init?.credentials).toBe('include');
    expect(headers.get('X-Flowzer-CSRF')).toBe('csrf-1');
    expect(headers.has('Authorization')).toBe(false);
  });

  // Testzweck: Der idempotente Abschluss überträgt die stabile Aktions-ID und bindet
  // Wiederholungen an den ausdrücklich vom Host vergebenen Schlüssel.
  it('schließt eine Aufgabe mit Aktion und Idempotenzschlüssel ab', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({ successful: true }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await client.userTasks.complete({
      flowNodeId: 'approve',
      tokenId: 'token-1',
      processInstanceId: 'instance-1',
      expectedTaskRevision: 4,
      actionId: 'approve',
      data: { comment: 'ok' },
    }, { idempotencyKey: 'host-command-42' });

    const [, init] = fetch.mock.calls[0]!;
    expect(new Headers(init?.headers).get('Idempotency-Key')).toBe('host-command-42');
    expect(JSON.parse(String(init?.body))).toMatchObject({ actionId: 'approve', data: { comment: 'ok' } });
  });

  // Testzweck: Private Entwürfe verwenden im SDK denselben revisionsgebundenen
  // Vertrag wie die Flowzer-Konsole und kodieren auch ungewöhnliche Task-IDs sicher.
  it('speichert einen revisionsgebundenen Aufgabenentwurf', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: { userTaskId: 'task/id', revision: 4, data: { comment: 'offen' } },
    }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await expect(client.userTasks.saveDraft('task/id', {
      expectedRevision: 3,
      expectedTaskRevision: 7,
      data: { comment: 'offen' },
    })).resolves.toMatchObject({ revision: 4 });

    expect(fetch.mock.calls[0]![0]).toBe('/api/usertask/task%2Fid/draft');
    expect(JSON.parse(String(fetch.mock.calls[0]![1]?.body))).toMatchObject({
      expectedRevision: 3,
      expectedTaskRevision: 7,
    });
  });

  // Testzweck: Feldbezogene Problem-Details bleiben für beliebige Host-Oberflächen
  // maschinenlesbar und werden nicht in einen lokalisierten Text reduziert.
  it('liefert Problem Details und Feldfehler typisiert weiter', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      type: 'about:blank',
      title: 'The request could not be processed.',
      status: 422,
      detail: 'Validation failed.',
      instance: '/usertask',
      traceId: 'trace-1',
      errors: { decision: ['action.invalid'] },
    }, 422));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    const failure = await client.userTasks.complete(
      { flowNodeId: 'x', tokenId: 'y', expectedTaskRevision: 2 },
      { idempotencyKey: 'host-command-validation' },
    )
      .catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(FlowzerApiError);
    expect(failure).toMatchObject({
      status: 422,
      fieldErrors: { decision: ['action.invalid'] },
      traceId: 'trace-1',
    });
  });

  // Testzweck: Abbruchsignale des Hosts erreichen fetch unverändert, damit entfernte
  // Views keine unnötigen oder veralteten Antworten weiterverarbeiten.
  it('reicht AbortSignal an den Transport durch', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: [],
    }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });
    const controller = new AbortController();

    await client.instances.list({ signal: controller.signal });

    expect(fetch.mock.calls[0]![1]?.signal).toBe(controller.signal);
  });

  // Testzweck: Die History bleibt eine minimale, serverseitig berechtigte Projektion
  // und wird als eigener, hostneutraler Vertrag über den Instanzpfad geladen.
  it('lädt die Prozesshistorie mit sicher kodierter Instanz-ID', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: {
        instanceId: 'instance/id', events: [{
          id: 'history-1', userTaskId: 'task-1', flowNodeId: 'Review',
          action: 'complete', revision: 3, occurredAtUtc: '2026-09-09T10:00:00Z',
        }],
      },
    }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });
    const controller = new AbortController();

    await expect(client.instances.history('instance/id', { signal: controller.signal })).resolves.toMatchObject({
      events: [{ flowNodeId: 'Review', revision: 3 }],
    });
    expect(fetch.mock.calls[0]![0]).toBe('/api/instance/instance%2Fid/history');
    expect(fetch.mock.calls[0]![1]?.signal).toBe(controller.signal);
  });

  // Testzweck: Der SDK erzeugt niemals stillschweigend einen Ersatzschlüssel und
  // verwirft ungültige Idempotenzwerte, bevor eine Mutation den Server erreicht.
  it('weist ungültige Idempotenzschlüssel vor dem Request zurück', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>();
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await expect(client.userTasks.complete(
      { flowNodeId: 'x', tokenId: 'y', expectedTaskRevision: 2 },
      { idempotencyKey: 'line\nbreak' },
    )).rejects.toThrow(TypeError);
    expect(fetch).not.toHaveBeenCalled();
  });

  // Testzweck: Auch untypisierte JavaScript-Aufrufer können den verpflichtenden
  // Idempotenzschutz beim Abschluss nicht durch Weglassen der Optionen umgehen.
  it('verlangt beim Aufgabenabschluss immer einen Idempotenzschlüssel', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>();
    const client = new FlowzerClient({ baseUrl: '/api', fetch });
    const completeFromJavaScript = client.userTasks.complete as unknown as (
      command: { flowNodeId: string; tokenId: string; expectedTaskRevision: number },
    ) => Promise<void>;

    await expect(completeFromJavaScript({
      flowNodeId: 'x',
      tokenId: 'y',
      expectedTaskRevision: 2,
    })).rejects.toThrow('idempotencyKey');
    expect(fetch).not.toHaveBeenCalled();
  });

  // Testzweck: Formular-Identitätsfelder fragen ausschließlich ihren servergebundenen
  // Task-/Feldkontext ab; Task- und Feldkennung werden dabei als Pfadsegmente kodiert.
  it('sucht erlaubte Formularidentitäten im gebundenen Aufgabenkontext', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: { generationId: 'generation-1', items: [] },
    }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await client.userTasks.searchFormSubjects('task/id', 'delegate/user', {
      query: 'Alex',
      kind: 'user',
      limit: 12,
    });

    expect(fetch.mock.calls[0]![0]).toBe(
      '/api/identity-directory/user-tasks/task%2Fid/fields/delegate%2Fuser/subjects?query=Alex&kind=user&limit=12',
    );
  });

  // Testzweck: Ein Host kann eine konkrete Aufgabe per stabiler ID laden, ohne die
  // gesamte Aufgabenliste abzurufen; ungewöhnliche IDs bleiben ein einzelnes Segment.
  it('lädt eine einzelne sichtbare Aufgabe per Deep Link', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: { id: 'task/id', name: 'Prüfen' },
    }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await expect(client.userTasks.get('task/id')).resolves.toMatchObject({ name: 'Prüfen' });

    expect(fetch.mock.calls[0]![0]).toBe('/api/usertask/task%2Fid');
  });

  // Testzweck: Eine als erfolgreich markierte, aber ergebnislose Datenantwort wird
  // nicht als gültiges typisiertes Objekt an die Host-Anwendung weitergereicht.
  it('weist erfolgreiche Datenantworten ohne Ergebnis zurück', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({ successful: true }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    const failure = await client.userTasks.getForm('task-1').catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(FlowzerApiError);
    expect(failure).toMatchObject({ status: 200 });
  });

  // Testzweck: Eine abgelaufene Host-Sitzung kann zentral invalidiert werden, ohne
  // die maschinenlesbaren Flowzer-Fehlerdetails für den Aufrufer zu verlieren.
  it('meldet eine 401-Antwort an den Host und bewahrt den API-Fehler', async () => {
    const onUnauthorized = vi.fn();
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      title: 'Unauthorized',
      status: 401,
      traceId: 'trace-auth',
    }, 401));
    const client = new FlowzerClient({ baseUrl: '/api', fetch, onUnauthorized });

    const failure = await client.userTasks.list().catch((error: unknown) => error);

    expect(onUnauthorized).toHaveBeenCalledOnce();
    expect(failure).toMatchObject({ status: 401, traceId: 'trace-auth' });
  });

  // Testzweck: Modellierungsoberflächen laden ausschließlich veröffentlichte,
  // konkrete Abschnittsversionen; die Version wird nie als freier "latest"-Text
  // an die API weitergegeben.
  it('lädt eine konkrete Formularabschnittsversion über ihren kanonischen Pfad', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: {
        id: 'version-1', sectionId: 'section/id', version: { major: 1, minor: 2 }, sectionData: '{}',
      },
    }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await expect(client.formSections.getVersion('section/id', { major: 1, minor: 2 }))
      .resolves.toMatchObject({ id: 'version-1', version: { major: 1, minor: 2 } });

    expect(fetch.mock.calls[0]![0]).toBe('/api/form-section/section%2Fid/versions/1.2');
  });

  // Testzweck: Das revisionsgebundene Speichern eines Abschnittsentwurfs bleibt
  // als Compare-and-Swap-Vertrag auch für reine JavaScript-Hosts vollständig erhalten.
  it('speichert einen Formularabschnittsentwurf mit erwarteter Revision', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({
      successful: true,
      result: {
        sectionId: 'section/id', revision: 4, hasDraft: true, sectionData: '{"components":[]}',
      },
    }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await client.formSections.saveDraft('section/id', {
      expectedRevision: 3,
      sectionData: '{"components":[]}',
    });

    expect(fetch.mock.calls[0]![0]).toBe('/api/form-section/section%2Fid/draft');
    expect(JSON.parse(String(fetch.mock.calls[0]![1]?.body))).toEqual({
      expectedRevision: 3,
      sectionData: '{"components":[]}',
    });
  });

  // Testzweck: Das Verwerfen verwendet den gemeinsamen Erfolgsumschlag und bindet
  // die erwartete Revision als Queryparameter, statt einen 204-Körper zu erfinden.
  it('verwirft einen Formularabschnittsentwurf revisionsgebunden', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({ successful: true }));
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await client.formSections.deleteDraft('section/id', 3);

    expect(fetch.mock.calls[0]![0]).toBe('/api/form-section/section%2Fid/draft?expectedRevision=3');
    expect(fetch.mock.calls[0]![1]?.method).toBe('DELETE');
  });

  // Testzweck: Eine Veröffentlichung nimmt ausschließlich eine positive
  // erwartete Revision an und verhindert damit, dass JavaScript-Aufrufer den
  // serverseitigen Draft-CAS mit einem leeren Standardwert umgehen.
  it('weist ungültige Abschnittsveröffentlichungen vor dem Request zurück', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>();
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await expect(client.formSections.publish('section-1', 0)).rejects.toThrow(TypeError);
    expect(fetch).not.toHaveBeenCalled();
  });

  // Testzweck: Unvollständige oder nicht-ganzzahlige Versionswerte werden lokal
  // verworfen; dadurch kann das SDK keine "latest"- oder Pfad-Injection-Semantik
  // in eine konkrete Abschnittsreferenz einschleusen.
  it('weist keine unkonkreten Formularabschnittsversionen an die API weiter', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>();
    const client = new FlowzerClient({ baseUrl: '/api', fetch });

    await expect(client.formSections.getVersion('section-1', {
      major: Number.NaN,
      minor: 0,
    })).rejects.toThrow(TypeError);
    expect(fetch).not.toHaveBeenCalled();
  });
});
