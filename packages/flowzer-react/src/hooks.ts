import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
} from '@tanstack/react-query';
import { useCallback, useEffect } from 'react';

import type {
  CompleteUserTaskCommand,
  DirectorySubjectSearchOptions,
  DirectorySubjectSearchResult,
  ExtendedUserTask,
  FlowzerCompletionOptions,
  FlowzerForm,
  ProcessInstance,
  ProcessHistory,
  ReleaseUserTaskCommand,
  SaveUserTaskDraftCommand,
  TaskAssigneeSearchOptions,
  TransferUserTaskCommand,
  UserTaskDraft,
  UserTaskRevisionCommand,
  UserTaskWorkState,
} from '@flowzer/sdk';

import { useFlowzer } from './context.js';
import { flowzerQueryKeys } from './queryKeys.js';

export interface FlowzerQueryOptions {
  enabled?: boolean | undefined;
  refetchInterval?: number | false | undefined;
}

export interface CompleteTaskInput {
  command: CompleteUserTaskCommand;
  options: FlowzerCompletionOptions;
}

export interface DeleteDraftInput {
  expectedRevision: number;
  expectedTaskRevision?: number | undefined;
}

export interface TaskWorkspaceState {
  task: ExtendedUserTask | undefined;
  form: FlowzerForm | undefined;
  draft: UserTaskDraft | undefined;
  canWork: boolean;
  isPending: boolean;
  isRefreshing: boolean;
  error: Error | null;
  reload: () => Promise<void>;
  reloadDraft: () => Promise<UserTaskDraft | undefined>;
  searchSubjects: (
    fieldKey: string,
    options: DirectorySubjectSearchOptions,
  ) => Promise<DirectorySubjectSearchResult>;
}

export interface UserTaskActions {
  claim: UseMutationResult<UserTaskWorkState, Error, UserTaskRevisionCommand>;
  release: UseMutationResult<UserTaskWorkState, Error, ReleaseUserTaskCommand>;
  assign: UseMutationResult<UserTaskWorkState, Error, TransferUserTaskCommand>;
  delegate: UseMutationResult<UserTaskWorkState, Error, TransferUserTaskCommand>;
  saveDraft: UseMutationResult<UserTaskDraft, Error, SaveUserTaskDraftCommand>;
  deleteDraft: UseMutationResult<void, Error, DeleteDraftInput>;
  complete: UseMutationResult<void, Error, CompleteTaskInput>;
  searchAssignees: (options: TaskAssigneeSearchOptions) => Promise<DirectorySubjectSearchResult>;
}

export function useUserTasks(options: FlowzerQueryOptions = {}) {
  const { client, cacheNamespace, sessionScope } = useFlowzer();
  return useQuery<ExtendedUserTask[], Error>({
    queryKey: flowzerQueryKeys.userTasks(cacheNamespace, sessionScope),
    queryFn: ({ signal }) => client.userTasks.list({ signal }),
    enabled: options.enabled ?? true,
    ...(options.refetchInterval === undefined
      ? {}
      : { refetchInterval: options.refetchInterval }),
  });
}

export function useUserTask(userTaskId: string, options: FlowzerQueryOptions = {}) {
  const { client, cacheNamespace, sessionScope } = useFlowzer();
  return useQuery<ExtendedUserTask, Error>({
    queryKey: flowzerQueryKeys.userTask(cacheNamespace, sessionScope, userTaskId),
    queryFn: ({ signal }) => client.userTasks.get(userTaskId, { signal }),
    enabled: Boolean(userTaskId) && (options.enabled ?? true),
    ...(options.refetchInterval === undefined
      ? {}
      : { refetchInterval: options.refetchInterval }),
  });
}

export function useInstanceStatus(instanceId: string, options: FlowzerQueryOptions = {}) {
  const { client, cacheNamespace, sessionScope } = useFlowzer();
  return useQuery<ProcessInstance, Error>({
    queryKey: flowzerQueryKeys.instance(cacheNamespace, sessionScope, instanceId),
    queryFn: ({ signal }) => client.instances.get(instanceId, { signal }),
    enabled: Boolean(instanceId) && (options.enabled ?? true),
    ...(options.refetchInterval === undefined
      ? {}
      : { refetchInterval: options.refetchInterval }),
  });
}

/** Lädt die datensparsame History erst, wenn die einbettende Oberfläche sie freischaltet. */
export function useInstanceHistory(instanceId: string, options: FlowzerQueryOptions = {}) {
  const { client, cacheNamespace, sessionScope } = useFlowzer();
  const enabled = Boolean(instanceId) && (options.enabled ?? true);
  const query = useQuery<ProcessHistory, Error>({
    queryKey: flowzerQueryKeys.instanceHistory(cacheNamespace, sessionScope, instanceId),
    queryFn: ({ signal }) => client.instances.history(instanceId, { signal }),
    enabled,
    ...(options.refetchInterval === undefined ? {} : { refetchInterval: options.refetchInterval }),
  });
  const queryClient = useQueryClient();
  useEffect(() => {
    if (!enabled) queryClient.removeQueries({
      queryKey: flowzerQueryKeys.instanceHistory(cacheNamespace, sessionScope, instanceId),
    });
  }, [cacheNamespace, enabled, instanceId, queryClient, sessionScope]);
  // Deaktivierte Observer können ihren letzten Wert noch bis zum nächsten internen
  // Query-Update halten. Die öffentliche Projektion schließt deshalb synchron fail-closed.
  return enabled ? query : { ...query, data: undefined };
}

/** Lädt zuerst den sichtbaren Task und erst danach dessen arbeitsberechtigte Inhalte. */
export function useUserTaskWorkspace(
  userTaskId: string,
  options: FlowzerQueryOptions = {},
): TaskWorkspaceState {
  const { client, cacheNamespace, sessionScope } = useFlowzer();
  const queryClient = useQueryClient();
  const task = useUserTask(userTaskId, options);
  const canWork = task.data?.workState?.canWork === true;
  const contentEnabled = Boolean(userTaskId) && canWork && (options.enabled ?? true);
  const form = useQuery<FlowzerForm, Error>({
    queryKey: flowzerQueryKeys.userTaskForm(cacheNamespace, sessionScope, userTaskId),
    queryFn: ({ signal }) => client.userTasks.getForm(userTaskId, { signal }),
    enabled: contentEnabled,
  });
  const draft = useQuery<UserTaskDraft, Error>({
    queryKey: flowzerQueryKeys.userTaskDraft(cacheNamespace, sessionScope, userTaskId),
    queryFn: ({ signal }) => client.userTasks.getDraft(userTaskId, { signal }),
    enabled: contentEnabled,
  });
  useEffect(() => {
    if (!task.data || canWork) return;
    // Ein Refetch kann ein entzogenes Arbeitsrecht sichtbar machen. Deaktivierte Queries
    // behalten standardmäßig alte Daten im Cache; diese dürfen nicht weitergereicht werden.
    queryClient.removeQueries({
      queryKey: flowzerQueryKeys.userTaskForm(cacheNamespace, sessionScope, userTaskId),
    });
    queryClient.removeQueries({
      queryKey: flowzerQueryKeys.userTaskDraft(cacheNamespace, sessionScope, userTaskId),
    });
  }, [cacheNamespace, canWork, queryClient, sessionScope, task.data, userTaskId]);
  const searchSubjects = useCallback(
    (fieldKey: string, search: DirectorySubjectSearchOptions) =>
      client.userTasks.searchFormSubjects(userTaskId, fieldKey, search),
    [client, userTaskId],
  );

  return {
    task: task.data,
    form: canWork ? form.data : undefined,
    draft: canWork ? draft.data : undefined,
    canWork,
    isPending: task.isPending || (contentEnabled && (form.isPending || draft.isPending)),
    isRefreshing: task.isFetching || form.isFetching || draft.isFetching,
    error: task.error ?? (canWork ? form.error ?? draft.error : null),
    reload: async () => {
      const refreshed = await task.refetch();
      if (refreshed.data?.workState?.canWork === true) {
        await Promise.all([form.refetch(), draft.refetch()]);
      }
    },
    reloadDraft: async () => {
      const result = await draft.refetch();
      if (result.error) throw result.error;
      return result.data;
    },
    searchSubjects,
  };
}

/** Task-Mutationen werden nie automatisch wiederholt; der Host entscheidet bewusst. */
export function useUserTaskActions(userTaskId: string): UserTaskActions {
  const { client, cacheNamespace, sessionScope } = useFlowzer();
  const queryClient = useQueryClient();
  const taskKey = flowzerQueryKeys.userTask(cacheNamespace, sessionScope, userTaskId);
  const draftKey = flowzerQueryKeys.userTaskDraft(cacheNamespace, sessionScope, userTaskId);

  const refreshTask = async (state?: UserTaskWorkState) => {
    if (state) {
      queryClient.setQueryData<ExtendedUserTask>(taskKey, (task) =>
        task ? { ...task, workState: state } : task);
    }
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: flowzerQueryKeys.userTasks(cacheNamespace, sessionScope) }),
      queryClient.invalidateQueries({ queryKey: taskKey }),
    ]);
  };

  const claim = useMutation({
    mutationFn: (command: UserTaskRevisionCommand) => client.userTasks.claim(userTaskId, command),
    retry: false,
    onSuccess: refreshTask,
    onError: () => refreshTask(),
  });
  const release = useMutation({
    mutationFn: (command: ReleaseUserTaskCommand) => client.userTasks.release(userTaskId, command),
    retry: false,
    onSuccess: refreshTask,
    onError: () => refreshTask(),
  });
  const assign = useMutation({
    mutationFn: (command: TransferUserTaskCommand) => client.userTasks.assign(userTaskId, command),
    retry: false,
    onSuccess: refreshTask,
    onError: () => refreshTask(),
  });
  const delegate = useMutation({
    mutationFn: (command: TransferUserTaskCommand) => client.userTasks.delegate(userTaskId, command),
    retry: false,
    onSuccess: refreshTask,
    onError: () => refreshTask(),
  });
  const saveDraft = useMutation({
    mutationFn: (command: SaveUserTaskDraftCommand) => client.userTasks.saveDraft(userTaskId, command),
    retry: false,
    onSuccess: (saved) => queryClient.setQueryData(draftKey, saved),
  });
  const deleteDraft = useMutation({
    mutationFn: (command: DeleteDraftInput) => client.userTasks.deleteDraft(
      userTaskId,
      command.expectedRevision,
      command.expectedTaskRevision,
    ),
    retry: false,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: draftKey }),
  });
  const complete = useMutation({
    mutationFn: (input: CompleteTaskInput) => client.userTasks.complete(input.command, input.options),
    retry: false,
    onSuccess: async (_result, input) => {
      await refreshTask();
      if (input.command.processInstanceId) {
        await queryClient.invalidateQueries({
          queryKey: flowzerQueryKeys.instance(
            cacheNamespace,
            sessionScope,
            input.command.processInstanceId,
          ),
        });
      }
    },
  });
  const searchAssignees = useCallback(
    (search: TaskAssigneeSearchOptions) => client.userTasks.searchAssignees(userTaskId, search),
    [client, userTaskId],
  );

  return { claim, release, assign, delegate, saveDraft, deleteDraft, complete, searchAssignees };
}

export type { ExtendedUserTask, FlowzerForm, ProcessInstance, UserTaskDraft };
