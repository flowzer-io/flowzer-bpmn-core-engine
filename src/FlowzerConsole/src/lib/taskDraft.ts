import { useCallback, useEffect, useReducer, useRef } from 'react';

import { ApiError } from './api/client';
import type { UserTaskDraftDto, ProcessVariables } from './api/types';
import {
  useDeleteUserTaskDraft,
  useSaveUserTaskDraft,
  useUserTaskDraft,
} from './api/queries';

export type TaskDraftLoadState = 'loading' | 'ready' | 'error';
export type TaskDraftSaveState = 'idle' | 'dirty' | 'saving' | 'saved' | 'error' | 'conflict';

export interface TaskDraftState {
  taskId: string;
  /** Stabiler Grundwert für „Verwerfen“ und die erste Formular-Hydratisierung. */
  fallbackData: ProcessVariables;
  /** Wert, der gerade im Form.io-Formular sichtbar ist. */
  currentData: ProcessVariables;
  /** Wert, mit dem Form.io initialisiert wurde; nie bei jedem Tastendruck ändern. */
  initialData: ProcessVariables;
  /** Letzter vom Server bestätigter Wert. */
  savedData: ProcessVariables;
  revision: number;
  hasDraft: boolean;
  updatedAtUtc: string | null;
  initialized: boolean;
  dirty: boolean;
  loadState: TaskDraftLoadState;
  saveState: TaskDraftSaveState;
  error: unknown | null;
  /** Ändert sich nur bei Taskwechsel oder bewusstem Serverstand-Laden. */
  formInstanceKey: number;
  pendingSignature: string | null;
}

export type TaskDraftAction =
  | { type: 'taskChanged'; taskId: string; fallbackData: ProcessVariables }
  | { type: 'hydrate'; draft: UserTaskDraftDto; force?: boolean }
  | { type: 'loadFailed'; error: unknown }
  | { type: 'change'; data: ProcessVariables }
  | { type: 'saveStarted' }
  | { type: 'saveSucceeded'; taskId: string; draft: UserTaskDraftDto }
  | { type: 'saveFailed'; taskId: string; error: unknown; conflict: boolean }
  | { type: 'discardSucceeded'; taskId: string };

/** Klont Form.io-Eingaben, damit weder Cacheobjekte noch verschachtelte Werte mutieren. */
export function cloneProcessVariables(data: ProcessVariables | null | undefined): ProcessVariables {
  const source = data ?? {};
  if (typeof structuredClone === 'function') return structuredClone(source);
  return JSON.parse(JSON.stringify(source)) as ProcessVariables;
}

function canonicalValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonicalValue);
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, nested]) => [key, canonicalValue(nested)]),
    );
  }
  return value;
}

function dataSignature(data: ProcessVariables): string {
  return JSON.stringify(canonicalValue(data));
}

function hasServerDraft(draft: UserTaskDraftDto): boolean {
  // Revision 0 plus an empty object is the API's explicit „kein Entwurf“-value.
  return draft.revision !== 0 || Object.keys(draft.data ?? {}).length > 0;
}

export function createTaskDraftState(taskId: string, fallbackData: ProcessVariables): TaskDraftState {
  const fallback = cloneProcessVariables(fallbackData);
  return {
    taskId,
    fallbackData: fallback,
    currentData: cloneProcessVariables(fallback),
    initialData: cloneProcessVariables(fallback),
    savedData: cloneProcessVariables(fallback),
    revision: 0,
    hasDraft: false,
    updatedAtUtc: null,
    initialized: false,
    dirty: false,
    loadState: 'loading',
    saveState: 'idle',
    error: null,
    formInstanceKey: 0,
    pendingSignature: null,
  };
}

export function taskDraftReducer(state: TaskDraftState, action: TaskDraftAction): TaskDraftState {
  switch (action.type) {
    case 'taskChanged':
      return createTaskDraftState(action.taskId, action.fallbackData);

    case 'hydrate': {
      if (action.draft.userTaskId !== state.taskId) return state;
      // Polling/refetches are intentionally observational. Only the initial response,
      // or an explicit „Serverstand laden“, may replace the visible local form.
      if (state.initialized && !action.force) return state;
      const data = hasServerDraft(action.draft)
        ? cloneProcessVariables(action.draft.data)
        : cloneProcessVariables(state.fallbackData);
      return {
        ...state,
        currentData: cloneProcessVariables(data),
        initialData: cloneProcessVariables(data),
        savedData: cloneProcessVariables(data),
        revision: action.draft.revision,
        hasDraft: hasServerDraft(action.draft),
        updatedAtUtc: action.draft.updatedAtUtc,
        initialized: true,
        loadState: 'ready',
        saveState: 'idle',
        error: null,
        dirty: false,
        formInstanceKey: state.formInstanceKey + 1,
        pendingSignature: null,
      };
    }

    case 'loadFailed':
      return state.initialized
        ? state
        : { ...state, loadState: 'error', error: action.error };

    case 'change': {
      const currentData = cloneProcessVariables(action.data);
      const dirty = dataSignature(currentData) !== dataSignature(state.savedData);
      return {
        ...state,
        currentData,
        dirty,
        // A new edit supersedes a previous success/error indicator. While a request is
        // active, keep „saving“ visible until its response has been handled.
        saveState: state.saveState === 'saving' ? 'saving' : dirty ? 'dirty' : 'idle',
        error: null,
      };
    }

    case 'saveStarted':
      return {
        ...state,
        saveState: 'saving',
        error: null,
        pendingSignature: dataSignature(state.currentData),
      };

    case 'saveSucceeded': {
      if (action.taskId !== state.taskId) return state;
      const currentSignature = dataSignature(state.currentData);
      const savedSignature = dataSignature(action.draft.data);
      return {
        ...state,
        // Keep currentData and initialData untouched. This prevents a canonical server
        // response from rebuilding Form.io while the user is still looking at the form.
        savedData: cloneProcessVariables(action.draft.data),
        revision: action.draft.revision,
        hasDraft: true,
        updatedAtUtc: action.draft.updatedAtUtc,
        dirty: currentSignature !== savedSignature,
        saveState: currentSignature === savedSignature ? 'saved' : 'dirty',
        error: null,
        pendingSignature: null,
      };
    }

    case 'saveFailed':
      if (action.taskId !== state.taskId) return state;
      return {
        ...state,
        saveState: action.conflict ? 'conflict' : 'error',
        error: action.error,
        pendingSignature: null,
      };

    case 'discardSucceeded':
      if (action.taskId !== state.taskId) return state;
      return {
        ...state,
        currentData: cloneProcessVariables(state.fallbackData),
        initialData: cloneProcessVariables(state.fallbackData),
        savedData: cloneProcessVariables(state.fallbackData),
        revision: 0,
        hasDraft: false,
        updatedAtUtc: null,
        dirty: false,
        saveState: 'idle',
        error: null,
        pendingSignature: null,
        formInstanceKey: state.formInstanceKey + 1,
      };
  }
}

export interface TaskDraftEditor {
  currentData: ProcessVariables;
  initialData: ProcessVariables;
  revision: number;
  hasDraft: boolean;
  updatedAtUtc: string | null;
  dirty: boolean;
  loadState: TaskDraftLoadState;
  saveState: TaskDraftSaveState;
  error: unknown | null;
  /** Während des Hintergrund-Refetches bleibt der Editor unverändert. */
  isRefreshing: boolean;
  isSaving: boolean;
  isDiscarding: boolean;
  formInstanceKey: number;
  setData: (data: ProcessVariables) => void;
  save: () => void;
  discard: () => void;
  adoptServerDraft: () => Promise<void>;
  refetch: () => Promise<unknown>;
}

/**
 * Verbindet den Draft-Query mit einem lokalen, nicht destruktiv hydratisierten Editor.
 * Die Hook-API hält bewusst die Form.io-Grundlage getrennt von den laufenden Eingaben.
 */
export function useTaskDraftEditor(
  taskId: string | undefined,
  fallbackData: ProcessVariables | null | undefined,
): TaskDraftEditor {
  // Der Task-Refetch liefert häufig neue Objektinstanzen. Der Fallback darf deshalb
  // nur bei einer echten Task-ID-Änderung neu eingefangen werden.
  const seedRef = useRef<{ taskId: string; data: ProcessVariables }>({
    taskId: taskId ?? '',
    data: cloneProcessVariables(fallbackData ?? {}),
  });
  if (seedRef.current.taskId !== (taskId ?? '')) {
    seedRef.current = {
      taskId: taskId ?? '',
      data: cloneProcessVariables(fallbackData ?? {}),
    };
  }
  const [state, dispatch] = useReducer(
    taskDraftReducer,
    createTaskDraftState(taskId ?? '', fallbackData ?? {}),
  );
  const draftQuery = useUserTaskDraft(taskId);
  const saveMutation = useSaveUserTaskDraft();
  const discardMutation = useDeleteUserTaskDraft();
  const stateRef = useRef(state);
  stateRef.current = state;

  useEffect(() => {
    dispatch({ type: 'taskChanged', taskId: seedRef.current.taskId, fallbackData: seedRef.current.data });
  }, [taskId]);

  useEffect(() => {
    if (!taskId || draftQuery.isPending) return;
    if (draftQuery.error) {
      dispatch({ type: 'loadFailed', error: draftQuery.error });
      return;
    }
    if (draftQuery.data) dispatch({ type: 'hydrate', draft: draftQuery.data });
  }, [taskId, draftQuery.data, draftQuery.error, draftQuery.isPending]);

  const setData = useCallback((data: ProcessVariables) => {
    dispatch({ type: 'change', data });
  }, []);

  const save = useCallback(() => {
    const current = stateRef.current;
    if (!taskId || !current.initialized || !current.dirty || saveMutation.isPending) return;

    const data = cloneProcessVariables(current.currentData);
    dispatch({ type: 'saveStarted' });
    saveMutation.mutate(
      { userTaskId: taskId, draft: { expectedRevision: current.revision, data } },
      {
        onSuccess: (saved) => dispatch({ type: 'saveSucceeded', taskId, draft: saved }),
        onError: (error) =>
          dispatch({
            type: 'saveFailed',
            taskId,
            error,
            conflict: error instanceof ApiError && error.status === 409,
          }),
      },
    );
  }, [saveMutation, taskId]);

  const discard = useCallback(() => {
    const current = stateRef.current;
    if (!taskId || discardMutation.isPending) return;
    discardMutation.mutate(
      { userTaskId: taskId, expectedRevision: current.revision },
      {
        onSuccess: () => dispatch({ type: 'discardSucceeded', taskId }),
        onError: (error) => dispatch({
          type: 'saveFailed',
          taskId,
          error,
          conflict: error instanceof ApiError && error.status === 409,
        }),
      },
    );
  }, [discardMutation, taskId]);

  const adoptServerDraft = useCallback(async () => {
    if (!taskId) return;
    const result = await draftQuery.refetch();
    if (result.error) {
      dispatch({ type: 'loadFailed', error: result.error });
      return;
    }
    if (result.data) dispatch({ type: 'hydrate', draft: result.data, force: true });
  }, [draftQuery, taskId]);

  const taskMatches = state.taskId === (taskId ?? '');
  return {
    currentData: taskMatches ? state.currentData : {},
    initialData: taskMatches ? state.initialData : {},
    revision: taskMatches ? state.revision : 0,
    hasDraft: taskMatches && state.hasDraft,
    updatedAtUtc: taskMatches ? state.updatedAtUtc : null,
    dirty: taskMatches && state.dirty,
    loadState: taskMatches ? (draftQuery.isPending ? 'loading' : state.loadState) : 'loading',
    saveState: taskMatches ? state.saveState : 'idle',
    error: taskMatches ? state.error : null,
    isRefreshing: taskMatches && draftQuery.isFetching && !draftQuery.isPending,
    isSaving: saveMutation.isPending,
    isDiscarding: discardMutation.isPending,
    formInstanceKey: taskMatches ? state.formInstanceKey : 0,
    setData,
    save,
    discard,
    adoptServerDraft,
    refetch: async () => {
      await draftQuery.refetch();
    },
  };
}
