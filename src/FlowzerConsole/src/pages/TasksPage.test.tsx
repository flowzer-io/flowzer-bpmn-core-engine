import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { ExtendedUserTaskSubscriptionDto } from '@/lib/api/types';

import { TasksPage } from './TasksPage';

const mocks = vi.hoisted(() => ({
  useUserTaskForm: vi.fn(),
  useTaskDraftEditor: vi.fn(),
}));

vi.mock('@/lib/useCompactLayout', () => ({ useCompactLayout: () => false }));
vi.mock('@/lib/api/queries', () => ({
  useUserTasks: () => ({ data: [task()], isPending: false, error: null }),
  useCompleteUserTask: () => ({ isPending: false, mutate: vi.fn() }),
  useUserTaskLifecycleMutation: () => ({
    isPending: false,
    error: null,
    mutate: vi.fn(),
    reset: vi.fn(),
  }),
  useUserTaskForm: mocks.useUserTaskForm,
}));
vi.mock('@/lib/taskDraft', () => ({ useTaskDraftEditor: mocks.useTaskDraftEditor }));

describe('TasksPage mit Task-Lifecycle', () => {
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
});

function task(): ExtendedUserTaskSubscriptionDto {
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
      claimed: false,
      actualAssignee: null,
      actualAssigneeDisplayName: null,
      isAssignedToCurrentUser: false,
      canWork: false,
      canClaim: true,
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
