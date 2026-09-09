import { describe, expect, it, vi } from 'vitest';

import { createTaskAssigneeSearch, createTaskFormDirectoryAdapter } from './taskDirectoryAdapters';

describe('öffentliche Directory-Adapter der Console', () => {
  // Testzweck: Das Form.io-Feld darf Task und Policy nicht selbst bestimmen; die
  // Console bindet die öffentliche Suche an den Task und reicht nur den Feldschlüssel weiter.
  it('bindet Suche und Auflösung an dasselbe Aufgabenfeld', async () => {
    const searchSubjects = vi.fn().mockResolvedValue({
      generationId: 'generation-1',
      items: [{
        subject: { kind: 'user', id: 'user-1' },
        displayName: 'Anna Beispiel',
        detail: 'anna@example.test',
      }],
    });
    const adapter = createTaskFormDirectoryAdapter('task-1', searchSubjects);

    await adapter.search('representative', { query: 'an', kind: 'user' });
    await adapter.resolve('representative', [{ kind: 'user', id: 'user-1' }]);

    expect(searchSubjects).toHaveBeenNthCalledWith(1, 'representative', {
      query: 'an', kind: 'user', limit: 20, signal: undefined,
    });
    expect(searchSubjects).toHaveBeenNthCalledWith(2, 'representative', {
      query: 'user-1', kind: 'user', limit: 20, signal: undefined,
    });
  });

  // Testzweck: Unbekannte Subject-Arten aus einem künftig erweiterten Serververtrag
  // dürfen nicht als auswählbare Benutzer oder Gruppen in die aktuelle Console gelangen.
  it('verwirft unbekannte Subject-Arten an der Paketgrenze', async () => {
    const searchSubjects = vi.fn().mockResolvedValue({
      generationId: 'generation-1',
      items: [{ subject: { kind: 'service', id: 'service-1' }, displayName: 'Dienst', detail: null }],
    });
    const adapter = createTaskFormDirectoryAdapter('task-1', searchSubjects);

    await expect(adapter.search('representative', { query: 'di', kind: 'all' }))
      .resolves.toEqual({ generationId: 'generation-1', items: [] });
  });

  // Testzweck: Der Lifecycle-Dialog kann weder Task noch Aktion überschreiben; die
  // Console ergänzt die beim Öffnen fest gebundene Aktion im öffentlichen Aufruf.
  it('bindet die Bearbeitersuche an die Lifecycle-Aktion', async () => {
    const searchAssignees = vi.fn().mockResolvedValue({ generationId: 'generation-1', items: [] });
    const search = createTaskAssigneeSearch('delegate', searchAssignees);

    await search({ query: 'an' });

    expect(searchAssignees).toHaveBeenCalledWith({
      action: 'delegate', query: 'an', limit: 20, signal: undefined,
    });
  });
});
