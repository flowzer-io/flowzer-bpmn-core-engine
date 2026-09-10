import { afterEach, describe, expect, it, vi } from 'vitest';

import { identityDirectoryApi } from './endpoints';

afterEach(() => {
  vi.unstubAllGlobals();
});

// Testzweck: Verzeichnissuchen dürfen nie global erfolgen. Workflowkennung, Identitätsart
// und feste Ergebnisgrenze müssen vollständig im Request an den geschützten Endpoint stehen.
describe('identityDirectoryApi.searchSubjects', () => {
  it('kodiert den Workflow und sendet nur die erlaubten Suchparameter', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({
        successful: true,
        result: { generationId: 'generation-1', items: [] },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await identityDirectoryApi.searchSubjects('urlaub/2026', 'Anna Beispiel', 'user');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      '/api/identity-directory/workflows/urlaub%2F2026/subjects?query=Anna+Beispiel&kind=user&limit=20',
    );
    expect(init).toMatchObject({ method: 'GET', credentials: 'same-origin' });
  });
});

// Testzweck: Ordnerdelegationen müssen den geplanten, ordnergebundenen Endpoint mit
// URL-kodierter Ordner-ID und derselben begrenzten User-/Gruppen-Suche verwenden.
describe('identityDirectoryApi.searchFolderSubjects', () => {
  it('sendet Suche und Limit an den Ordnerpfad', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ successful: true, result: { generationId: 'generation-1', items: [] } }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await identityDirectoryApi.searchFolderSubjects('folder/2026', 'Anna', 'all');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      '/api/identity-directory/folders/folder%2F2026/subjects?query=Anna&kind=all&limit=20',
    );
    expect(init).toMatchObject({ method: 'GET', credentials: 'same-origin' });
  });
});

// Testzweck: Start- und Aufgabenformulare müssen denselben Vertrag verwenden, aber ihren
// eigenen gebundenen Kontext sowie den Feldschlüssel an den Server weiterreichen.
describe('identityDirectoryApi.searchFormSubjects', () => {
  it.each([
    [{ kind: 'startForm', definitionId: 'definition/2026' }, '/identity-directory/start-forms/definition%2F2026'],
    [{ kind: 'userTask', taskId: 'task/42' }, '/identity-directory/user-tasks/task%2F42'],
  ] as const)('verwendet den %s Kontext ohne globale Suche', async (context, basePath) => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ successful: true, result: { generationId: 'generation-1', items: [] } }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await identityDirectoryApi.searchFormSubjects(context, 'approvers', 'An', 'all');

    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `/api${basePath}/fields/approvers/subjects?query=An&kind=all&limit=20`,
    );
  });
});

// Testzweck: Historische Referenzen werden in einem begrenzten POST-Batch und
// ausschließlich im gebundenen Workflow-, Ordner- oder Formularkontext aufgelöst.
describe('identityDirectoryApi.resolveSubjects', () => {
  it('sendet stabile Referenzen an die kontextgebundenen Auflösungsrouten', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => Response.json({
      successful: true,
      result: { generationId: 'generation-1', items: [] },
    }));
    vi.stubGlobal('fetch', fetchMock);
    const subjects = [{ kind: 'user' as const, id: 'user-1' }];

    await identityDirectoryApi.resolveSubjects('urlaub/2026', subjects);
    await identityDirectoryApi.resolveFolderSubjects('folder/2026', subjects);
    await identityDirectoryApi.resolveFormSubjects(
      { kind: 'userTask', taskId: 'task/42' }, 'approver/user', subjects,
    );

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      '/api/identity-directory/workflows/urlaub%2F2026/subjects/resolve',
      '/api/identity-directory/folders/folder%2F2026/subjects/resolve',
      '/api/identity-directory/user-tasks/task%2F42/fields/approver%2Fuser/subjects/resolve',
    ]);
    for (const [, init] of fetchMock.mock.calls as [string, RequestInit][]) {
      expect(init).toMatchObject({
        method: 'POST',
        credentials: 'same-origin',
      });
      expect(JSON.parse(String(init.body))).toEqual({ subjects });
    }
  });
});
