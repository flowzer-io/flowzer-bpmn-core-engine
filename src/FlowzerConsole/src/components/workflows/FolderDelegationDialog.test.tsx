import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { WorkflowFolderDto } from '@/lib/api/types';

import { FolderDelegationDialog } from './FolderDelegationDialog';

const folderSearchMock = vi.hoisted(() => vi.fn());
const folderResolutionMock = vi.hoisted(() => vi.fn());
const searchMock = vi.hoisted(() => vi.fn());
const resolutionMock = vi.hoisted(() => vi.fn());
const formSearchMock = vi.hoisted(() => vi.fn());
const formResolutionMock = vi.hoisted(() => vi.fn());

vi.mock('@/lib/api/queries', () => ({
  useFolderDirectorySubjectSearch: folderSearchMock,
  useFolderDirectorySubjectResolutions: folderResolutionMock,
  useDirectorySubjectSearch: searchMock,
  useDirectorySubjectResolutions: resolutionMock,
  useFormDirectorySubjectSearch: formSearchMock,
  useFormDirectorySubjectResolutions: formResolutionMock,
}));

const folder: WorkflowFolderDto = {
  id: 'folder-1',
  name: 'Beschaffung',
  parentId: null,
  description: null,
  createdOn: '2026-09-08T00:00:00Z',
  assignments: [{ subjectKind: 'user', subject: 'legacy-user', role: 'editor' }],
  inheritedAssignments: [],
  mayEdit: true,
  mayDelegate: true,
  workflowCount: 0,
};

function searchResult(items: Array<{ subject: { kind: 'user' | 'group'; id: string }; displayName: string; detail: string }> = []) {
  return { data: { generationId: 'generation-1', items }, isPending: false, isFetching: false, error: null };
}

beforeEach(() => {
  folderSearchMock.mockReset().mockReturnValue(
    searchResult([{ subject: { kind: 'user', id: 'directory-user' }, displayName: 'Anna Beispiel', detail: 'anna@example.test' }]),
  );
  folderResolutionMock.mockReset().mockReturnValue({ data: [], isPending: false, isFetching: false, error: null });
  searchMock.mockReset().mockReturnValue(searchResult());
  resolutionMock.mockReset().mockReturnValue({ data: [], isPending: false, isFetching: false, error: null });
  formSearchMock.mockReset().mockReturnValue(searchResult());
  formResolutionMock.mockReset().mockReturnValue({ data: [], isPending: false, isFetching: false, error: null });
});

describe('FolderDelegationDialog', () => {
  // Testzweck: Der bestehende Freitextvertrag bleibt beim Speichern explizit Legacy, während
  // eine neue Directory-Auswahl ausschließlich subjectRef und referenceMode directory trägt.
  it('trennt Legacy-Freitext und ordnergebundene Directory-Auswahl im Payload', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<FolderDelegationDialog open onOpenChange={vi.fn()} folder={folder} onSubmit={onSubmit} />);

    await user.click(screen.getByRole('tab', { name: 'Bekannte Directory-Identität' }));
    const input = screen.getByRole('searchbox', { name: 'Person oder Gruppe suchen' });
    await user.type(input, 'an');
    await waitFor(() => expect(screen.getByRole('option', { name: /Anna Beispiel/ })).toBeInTheDocument(), {
      timeout: 700,
    });
    await user.click(screen.getByRole('option', { name: /Anna Beispiel/ }));
    await user.click(screen.getByRole('button', { name: 'Übernehmen' }));

    expect(folderSearchMock).toHaveBeenLastCalledWith('folder-1', 'an', 'all', true);
    expect(onSubmit).toHaveBeenCalledWith([
      { subjectKind: 'user', subject: 'legacy-user', role: 'editor', referenceMode: 'text' },
      {
        referenceMode: 'directory',
        subjectKind: 'user',
        subject: 'directory-user',
        subjectRef: { kind: 'user', id: 'directory-user' },
        role: 'editor',
        displayName: 'Anna Beispiel',
      },
    ]);
  });

  // Testzweck: Historische Directory-Referenzen werden beim Öffnen nicht verworfen, sondern
  // als sichtbarer Warn-Chip des wiederverwendeten Pickers dargestellt und bleiben entfernbar.
  it('zeigt unbekannte Ordnerreferenzen weiter an', () => {
    render(
      <FolderDelegationDialog
        open
        onOpenChange={vi.fn()}
        folder={{
          ...folder,
          assignments: [
            {
              referenceMode: 'directory',
              subjectKind: 'group',
              subject: '',
              subjectRef: { kind: 'group', id: 'retired-group' },
              role: 'steward',
              displayName: 'Alte Gruppe',
            },
          ],
        }}
        onSubmit={vi.fn()}
      />,
    );

    expect(screen.getByText('Alte Gruppe')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Bekannte Directory-Identität' })).toBeInTheDocument();
  });
});
