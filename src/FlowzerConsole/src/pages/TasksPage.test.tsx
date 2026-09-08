import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { ExtendedUserTaskSubscriptionDto } from '@/lib/api/types';

import { TasksPage } from './TasksPage';

const mocks = vi.hoisted(() => ({
  useUserTaskForm: vi.fn(),
  useTaskDraftEditor: vi.fn(),
  completeMutate: vi.fn(),
  rendererValidate: vi.fn(async () => true),
  canWork: false,
}));

vi.mock('@/lib/useCompactLayout', () => ({ useCompactLayout: () => false }));
vi.mock('@/lib/api/queries', () => ({
  useUserTasks: () => ({ data: [task(mocks.canWork)], isPending: false, error: null }),
  useCompleteUserTask: () => ({ isPending: false, mutate: mocks.completeMutate }),
  useUserTaskLifecycleMutation: () => ({
    isPending: false,
    error: null,
    mutate: vi.fn(),
    reset: vi.fn(),
  }),
  useUserTaskForm: mocks.useUserTaskForm,
}));
vi.mock('@/lib/taskDraft', () => ({ useTaskDraftEditor: mocks.useTaskDraftEditor }));
vi.mock('@/components/forms/FormRenderer', async () => {
  const React = await import('react');
  return {
    FormRenderer: React.forwardRef(function FormRendererMock(_props, ref) {
      React.useImperativeHandle(ref, () => ({
        validate: mocks.rendererValidate,
        getData: () => ({ comment: 'Geprüft' }),
      }));
      return React.createElement('div', null, 'Formularinhalt');
    }),
  };
});

describe('TasksPage mit Task-Lifecycle', () => {
  beforeEach(() => {
    mocks.canWork = false;
    mocks.completeMutate.mockReset();
    mocks.rendererValidate.mockClear();
    mocks.useUserTaskForm.mockReset();
    mocks.useTaskDraftEditor.mockReset();
  });

  // Testzweck: Eine bloß sichtbare Kandidatenaufgabe darf Formular und privaten Draft
  // noch nicht laden; erst `canWork` des Servers öffnet den Bearbeitungsbereich.
  it('hält Formular und Draft vor dem Claim deaktiviert', () => {
    mocks.useUserTaskForm.mockReturnValue({ data: undefined, isPending: false, error: null });
    mocks.useTaskDraftEditor.mockReturnValue(draftEditor());

    render(<TasksPage />);

    expect(mocks.useUserTaskForm).toHaveBeenCalledWith('task-1', false);
    expect(mocks.useTaskDraftEditor).toHaveBeenCalledWith('task-1', {}, 4, false);
    expect(screen.queryByText('Formular ausfüllen')).not.toBeInTheDocument();
    expect(screen.getByText('Noch nicht zur Bearbeitung geöffnet')).toBeInTheDocument();
  });

  // Testzweck: Ein Profil-4-Aufgabenformular zeigt seine fachlich benannten Aktionen
  // statt des generischen Abschlussknopfs und sendet ausschließlich deren stabile ID.
  it('sendet die ausgewählte Entscheidungsaktion beim Abschluss', async () => {
    mocks.canWork = true;
    mocks.useUserTaskForm.mockReturnValue({
      data: {
        formData: JSON.stringify({
          flowzer: {
            contractVersion: 4,
            actions: [
              { id: 'approve', label: 'Freigeben', variant: 'primary', set: [{ field: 'decision', value: 'approved' }] },
              { id: 'reject', label: 'Ablehnen', variant: 'danger', set: [{ field: 'decision', value: 'rejected' }] },
            ],
          },
          components: [{ type: 'hidden', key: 'decision' }],
        }),
      },
      isPending: false,
      error: null,
    });
    mocks.useTaskDraftEditor.mockReturnValue({ ...draftEditor(), loadState: 'ready' as const });

    render(<TasksPage />);
    expect(screen.queryByRole('button', { name: 'Aufgabe abschließen' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Freigeben' }));

    await waitFor(() => expect(mocks.rendererValidate).toHaveBeenCalledWith({ decision: 'approved' }));
    await waitFor(() => expect(mocks.completeMutate).toHaveBeenCalledWith(
      expect.objectContaining({ actionId: 'approve', data: { comment: 'Geprüft' } }),
      expect.any(Object),
    ));
  });
});

function task(canWork = false): ExtendedUserTaskSubscriptionDto {
  return {
    id: 'task-1',
    name: 'Antrag prüfen',
    token: { id: 'token-1', state: 'Active', currentFlowNodeId: 'Task_1', variables: {} },
    userCandidates: [],
    userGroups: [],
    candidateUsers: [],
    candidateGroups: [],
    assignmentMode: 'text',
    directoryCandidateUsers: [],
    directoryCandidateGroups: [],
    workState: {
      revision: 4,
      claimed: canWork,
      actualAssignee: null,
      actualAssigneeDisplayName: null,
      isAssignedToCurrentUser: canWork,
      canWork,
      canClaim: !canWork,
      canRelease: false,
      canAssign: false,
      canDelegate: false,
    },
    definitionId: 'definition-1',
    processId: 'Process_1',
    definitionMetaName: 'Urlaubsantrag',
    definitionVersion: { major: 1, minor: 0 },
  };
}

function draftEditor() {
  return {
    currentData: {},
    initialData: {},
    revision: 0,
    hasDraft: false,
    updatedAtUtc: null,
    dirty: false,
    loadState: 'loading' as const,
    saveState: 'idle' as const,
    error: null,
    isRefreshing: false,
    isSaving: false,
    isDiscarding: false,
    formInstanceKey: 0,
    setData: vi.fn(),
    save: vi.fn(),
    discard: vi.fn(),
    adoptServerDraft: vi.fn(),
    refetch: vi.fn(),
  };
}
