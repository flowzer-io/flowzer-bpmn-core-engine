import { FlowzerApiError, type ExtendedUserTask } from '@flowzer/sdk';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { TasksPage } from './TasksPage';

const mocks = vi.hoisted(() => ({
  task: undefined as ExtendedUserTask | undefined,
  workspace: undefined as ReturnType<typeof workspaceState> | undefined,
  useTaskDraftEditor: vi.fn(),
  rendererValidate: vi.fn(async () => true),
  rendererProps: undefined as Record<string, unknown> | undefined,
  claim: mutation(),
  release: mutation(),
  assign: mutation(),
  delegate: mutation(),
  saveDraft: mutation(),
  deleteDraft: mutation(),
  complete: mutation(),
  searchAssignees: vi.fn(),
}));

vi.mock('@/lib/useCompactLayout', () => ({ useCompactLayout: () => false }));
vi.mock('@flowzer/react', () => ({
  useUserTasks: () => ({ data: mocks.task ? [mocks.task] : [], isPending: false, error: null }),
  useUserTaskWorkspace: () => mocks.workspace,
  useUserTaskActions: () => ({
    claim: mocks.claim,
    release: mocks.release,
    assign: mocks.assign,
    delegate: mocks.delegate,
    saveDraft: mocks.saveDraft,
    deleteDraft: mocks.deleteDraft,
    complete: mocks.complete,
    searchAssignees: mocks.searchAssignees,
  }),
}));
vi.mock('@/lib/taskDraft', () => ({ useTaskDraftEditor: mocks.useTaskDraftEditor }));
vi.mock('@/components/forms/FormRenderer', async () => {
  const React = await import('react');
  return {
    FormRenderer: React.forwardRef(function FormRendererMock(props: Record<string, unknown>, ref) {
      mocks.rendererProps = props;
      React.useImperativeHandle(ref, () => ({
        validate: mocks.rendererValidate,
        getData: () => ({ comment: 'Geprüft' }),
      }));
      return React.createElement('div', null, 'Formularinhalt');
    }),
  };
});

describe('TasksPage über öffentliche Human-Task-Pakete', () => {
  beforeEach(() => {
    mocks.task = task(false);
    mocks.workspace = workspaceState(mocks.task, false);
    mocks.rendererProps = undefined;
    mocks.rendererValidate.mockClear();
    mocks.useTaskDraftEditor.mockReset().mockReturnValue(draftEditor());
    resetMutation(mocks.claim);
    resetMutation(mocks.release);
    resetMutation(mocks.assign);
    resetMutation(mocks.delegate);
    resetMutation(mocks.saveDraft);
    resetMutation(mocks.deleteDraft);
    resetMutation(mocks.complete);
    mocks.searchAssignees.mockReset();
  });

  // Testzweck: Eine bloß sichtbare Kandidatenaufgabe darf Formular und privaten Draft
  // noch nicht erhalten; ausschließlich canWork des öffentlichen Workspace öffnet sie.
  it('hält Formular und Draft vor dem Claim deaktiviert', () => {
    render(<TasksPage />);

    expect(mocks.useTaskDraftEditor).toHaveBeenCalledWith(
      'task-1', {}, 4, false, expect.objectContaining({ draft: undefined }),
    );
    expect(screen.queryByText('Formular ausfüllen')).not.toBeInTheDocument();
    expect(screen.getByText('Noch nicht zur Bearbeitung geöffnet')).toBeInTheDocument();
  });

  // Testzweck: Ein Profil-4-Formular sendet die stabile Aktions-ID, die Revision aus
  // dem Detail-Workspace und einen ausdrücklich erzeugten Idempotenzschlüssel.
  it('schließt eine Entscheidungsaktion revisions- und idempotenzgebunden ab', async () => {
    enableWork();
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('00000000-0000-4000-8000-000000000001');

    render(<TasksPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Freigeben' }));

    await waitFor(() => expect(mocks.rendererValidate).toHaveBeenCalledWith({ decision: 'approved' }));
    expect(mocks.complete.mutate).toHaveBeenCalledWith({
      command: {
        flowNodeId: 'Task_1',
        tokenId: 'token-1',
        processInstanceId: 'instance-1',
        expectedTaskRevision: 4,
        actionId: 'approve',
        data: { comment: 'Geprüft' },
      },
      options: { idempotencyKey: '00000000-0000-4000-8000-000000000001' },
    }, expect.any(Object));
    expect(mocks.rendererProps?.directoryAdapter).toBeDefined();
    expect(mocks.rendererProps).not.toHaveProperty('directoryContext');
  });

  // Testzweck: Nach einem unklaren Netzwerkausgang muss eine bewusste Wiederholung
  // denselben Schlüssel senden, damit Flowzer keinen zweiten Abschluss erzeugt.
  it('behält den Abschluss-Schlüssel nach einem Netzwerkfehler bei', async () => {
    enableWork();
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('00000000-0000-4000-8000-000000000001');
    mocks.complete.mutate.mockImplementation((_input, callbacks) => {
      callbacks?.onError?.(new FlowzerApiError('Nicht erreichbar', { status: 0, url: '/api/usertask' }));
    });
    render(<TasksPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Freigeben' }));
    fireEvent.click(screen.getByRole('button', { name: 'Freigeben' }));

    await waitFor(() => expect(mocks.complete.mutate).toHaveBeenCalledTimes(2));
    expect(completionKeyAt(1)).toBe(completionKeyAt(0));
  });

  // Testzweck: Ein eindeutiger fachlicher Fehler bedeutet, dass kein Abschluss
  // erfolgt ist; ein korrigierter neuer Versuch erhält daher einen neuen Schlüssel.
  it('erneuert den Abschluss-Schlüssel nach einer eindeutigen Ablehnung', async () => {
    enableWork();
    vi.spyOn(crypto, 'randomUUID')
      .mockReturnValueOnce('00000000-0000-4000-8000-000000000001')
      .mockReturnValueOnce('00000000-0000-4000-8000-000000000002');
    mocks.complete.mutate.mockImplementation((_input, callbacks) => {
      callbacks?.onError?.(new FlowzerApiError('Ungültig', { status: 422, url: '/api/usertask' }));
    });
    render(<TasksPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Freigeben' }));
    fireEvent.click(screen.getByRole('button', { name: 'Freigeben' }));

    await waitFor(() => expect(mocks.complete.mutate).toHaveBeenCalledTimes(2));
    expect(completionKeyAt(0)).not.toBe(completionKeyAt(1));
  });

  // Testzweck: Claim wird nicht über den alten Console-Transport dupliziert, sondern
  // mit der sichtbaren Serverrevision an die öffentliche Action-Mutation delegiert.
  it('übernimmt eine Kandidatenaufgabe über die öffentliche Action-Mutation', () => {
    render(<TasksPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Übernehmen' }));

    expect(mocks.claim.mutate).toHaveBeenCalledWith(
      { expectedRevision: 4 },
      expect.any(Object),
    );
  });
});

function mutation() {
  return {
    isPending: false,
    error: null as Error | null,
    mutate: vi.fn(),
    reset: vi.fn(),
  };
}

function resetMutation(value: ReturnType<typeof mutation>) {
  value.isPending = false;
  value.error = null;
  value.mutate.mockReset();
  value.reset.mockReset();
}

function task(canWork: boolean): ExtendedUserTask {
  return {
    id: 'task-1',
    name: 'Antrag prüfen',
    token: { id: 'token-1', state: 1, currentFlowNodeId: 'Task_1', variables: {} },
    workState: {
      revision: 4,
      claimed: canWork,
      isAssignedToCurrentUser: canWork,
      canWork,
      canClaim: !canWork,
      canRelease: false,
      canAssign: false,
      canDelegate: false,
    },
    processInstanceId: 'instance-1',
    definitionId: 'definition-1',
    processId: 'Process_1',
    definitionMetaName: 'Urlaubsantrag',
    definitionVersion: { major: 1, minor: 0 },
  };
}

function workspaceState(currentTask: ExtendedUserTask, canWork: boolean) {
  return {
    task: currentTask,
    form: canWork ? {
      id: 'form-1',
      formData: JSON.stringify({
        flowzer: {
          contractVersion: 4,
          actions: [{
            id: 'approve',
            label: 'Freigeben',
            variant: 'primary',
            set: [{ field: 'decision', value: 'approved' }],
          }],
        },
        components: [{ type: 'hidden', key: 'decision' }],
      }),
    } : undefined,
    draft: canWork ? { userTaskId: 'task-1', revision: 0, data: {} } : undefined,
    canWork,
    isPending: false,
    isRefreshing: false,
    error: null,
    reload: vi.fn(),
    reloadDraft: vi.fn(),
    searchSubjects: vi.fn(),
  };
}

function enableWork() {
  mocks.task = task(true);
  mocks.workspace = workspaceState(mocks.task, true);
  mocks.useTaskDraftEditor.mockReturnValue({ ...draftEditor(), loadState: 'ready' as const });
}

function completionKeyAt(callIndex: number): string {
  const input = mocks.complete.mutate.mock.calls[callIndex]?.[0] as {
    options: { idempotencyKey: string };
  };
  return input.options.idempotencyKey;
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
  };
}
