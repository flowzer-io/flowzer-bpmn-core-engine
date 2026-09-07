import { afterEach, describe, expect, it, vi } from 'vitest';

import { definitionsApi } from './endpoints';

/**
 * Antworten der Flowzer-API nachstellen. Nur `fetch` wird ersetzt — geprüft wird der Weg
 * durch `client.ts` und `endpoints.ts`, nicht die Netzwerkschicht.
 */
function respondWith(response: Response) {
  const fetchMock = vi.fn().mockResolvedValue(response);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

// Testzweck: Ein Workflow ohne Startformular antwortet mit 204 ohne Rumpf. Ohne die
// Sonderbehandlung im Client käme das als „unerwartete Antwort" an — die Konsole zeigte einen
// Fehler, wo es schlicht nichts auszufüllen gibt.
describe('definitionsApi.getStartForm', () => {
  it('macht aus 204 No Content ein null', async () => {
    respondWith(new Response(null, { status: 204 }));

    await expect(definitionsApi.getStartForm('urlaub')).resolves.toBeNull();
  });

  it('liefert das Formular aus dem Umschlag', async () => {
    respondWith(
      Response.json({ successful: true, result: { formData: '{"display":"form"}' } }, { status: 200 }),
    );

    await expect(definitionsApi.getStartForm('urlaub')).resolves.toEqual({
      formData: '{"display":"form"}',
    });
  });

  it('meldet einen abgelehnten Abruf als Fehler', async () => {
    respondWith(
      Response.json({ successful: false, errorMessage: 'No form named "Antrag" was found.' }, { status: 400 }),
    );

    await expect(definitionsApi.getStartForm('urlaub')).rejects.toThrow('No form named "Antrag" was found.');
  });
});

// Testzweck: Ein Workflow ohne Startformular startet wie bisher ohne Rumpf; erst mit Werten
// schickt die Konsole ein `variables`-Objekt. Ein leeres Objekt ist dabei eine Angabe und
// etwas anderes als gar keine — genau daran entscheidet die API, ob sie den Start annimmt.
describe('definitionsApi.startInstance', () => {
  const started = () =>
    Response.json(
      { successful: true, result: { instanceId: 'i1', tokens: [], state: 0 } },
      { status: 200 },
    );

  it('schickt ohne Werte keinen Rumpf', async () => {
    const fetchMock = respondWith(started());

    await definitionsApi.startInstance('urlaub');

    expect(fetchMock.mock.calls[0]![1]).toMatchObject({ method: 'POST' });
    expect((fetchMock.mock.calls[0]![1] as RequestInit).body).toBeUndefined();
  });

  it('verpackt die Werte in ein variables-Objekt', async () => {
    const fetchMock = respondWith(started());

    await definitionsApi.startInstance('urlaub', {});

    expect((fetchMock.mock.calls[0]![1] as RequestInit).body).toBe('{"variables":{}}');
  });
});
