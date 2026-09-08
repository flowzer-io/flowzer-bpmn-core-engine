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
