import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError } from './client';
import { formsApi } from './endpoints';

describe('Formularautoren-API', () => {
  beforeEach(() => vi.restoreAllMocks());

  // Testzweck: Das Kompatibilitaetsinventar uebergibt den serverseitigen Filter und
  // verarbeitet ausschließlich Metadaten statt Formular- oder Scriptinhalte.
  it('lädt das gefilterte, datensparsame Kompatibilitätsinventar', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        successful: true,
        result: [{
          formId: 'f', formName: 'Alt', source: 'published', version: { major: 1, minor: 0 },
          compatible: false, issueCode: 'schema.script',
        }],
      }), { status: 200 }),
    );

    const result = await formsApi.compatibility(true);

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/form/compatibility?needsMigration=true');
    expect(result[0]).toMatchObject({ compatible: false, issueCode: 'schema.script' });
    expect(result[0]).not.toHaveProperty('formData');
  });

  // Testzweck: Speichern fuehrt die erwartete Revision und das vollstaendige Schema im
  // PUT-Vertrag mit, statt ueber den kompatiblen Direkt-Publish-Endpunkt zu gehen.
  it('speichert einen Autorenentwurf mit Compare-and-swap-Revision', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        successful: true,
        result: { formId: 'form/a', revision: 4, hasDraft: true, formData: '{"components":[]}' },
      }), { status: 200 }),
    );

    const result = await formsApi.saveDraft('form/a', {
      expectedRevision: 3,
      formData: '{"components":[]}',
    });

    expect(result.revision).toBe(4);
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/form/form%2Fa/draft');
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ method: 'PUT' });
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({
      expectedRevision: 3,
      formData: '{"components":[]}',
    });
  });

  // Testzweck: Veroeffentlichen und Verwerfen binden sich an die konkrete Draft-Revision;
  // dadurch kann die UI keine inzwischen geaenderte Fassung versehentlich bestaetigen.
  it('sendet Publish und Verwerfen mit der erwarteten Revision', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        successful: true,
        result: { formId: 'f', id: 'v', version: { major: 0, minor: 2 }, formData: '{}' },
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));

    await formsApi.publishDraft('f', 7);
    await formsApi.deleteDraft('f', 7);

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/form/f/publish');
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({ expectedRevision: 7 });
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/form/f/draft?expectedRevision=7');
    expect(fetchMock.mock.calls[1]?.[1]).toMatchObject({ method: 'DELETE' });
  });

  // Testzweck: Der 409-Status bleibt fuer die Konfliktansicht unterscheidbar; lokale
  // Editordaten koennen dadurch erhalten statt mit dem Serverstand ersetzt werden.
  it('reicht Revisionskonflikte als ApiError weiter', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ code: 'form_draft.revision_conflict', title: 'Conflict' }), {
        status: 409,
        statusText: 'Conflict',
      }),
    );

    const request = formsApi.saveDraft('f', { expectedRevision: 1, formData: '{}' });
    await expect(request).rejects.toMatchObject({ status: 409 });
    try { await request; } catch (error) { expect(error).toBeInstanceOf(ApiError); }
  });
});
