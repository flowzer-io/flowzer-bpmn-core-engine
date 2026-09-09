import type { ReactNode } from 'react';

import type { ExtendedUserTask, ProcessInstance } from '@flowzer/sdk';

import {
  useInstanceStatus,
  useUserTaskActions,
  useUserTasks,
  useUserTaskWorkspace,
  type FlowzerQueryOptions,
  type TaskWorkspaceState,
  type UserTaskActions,
} from './hooks.js';

export interface AsyncControllerState<T> {
  data: T;
  isPending: boolean;
  isRefreshing: boolean;
  error: Error | null;
  reload: () => Promise<void>;
}

export interface UserTaskListControllerProps extends FlowzerQueryOptions {
  children: (state: AsyncControllerState<ExtendedUserTask[]>) => ReactNode;
}

export function UserTaskListController({ children, ...options }: UserTaskListControllerProps) {
  const query = useUserTasks(options);
  return children({
    data: query.data ?? [],
    isPending: query.isPending,
    isRefreshing: query.isFetching,
    error: query.error,
    reload: async () => { await query.refetch(); },
  });
}

export interface UserTaskWorkspaceControllerState extends TaskWorkspaceState {
  actions: UserTaskActions;
}

export interface UserTaskWorkspaceControllerProps extends FlowzerQueryOptions {
  userTaskId: string;
  children: (state: UserTaskWorkspaceControllerState) => ReactNode;
}

export function UserTaskWorkspaceController({
  userTaskId,
  children,
  ...options
}: UserTaskWorkspaceControllerProps) {
  const workspace = useUserTaskWorkspace(userTaskId, options);
  const actions = useUserTaskActions(userTaskId);
  return children({ ...workspace, actions });
}

export interface InstanceStatusControllerProps extends FlowzerQueryOptions {
  instanceId: string;
  children: (state: AsyncControllerState<ProcessInstance | undefined>) => ReactNode;
}

export function InstanceStatusController({
  instanceId,
  children,
  ...options
}: InstanceStatusControllerProps) {
  const query = useInstanceStatus(instanceId, options);
  return children({
    data: query.data,
    isPending: query.isPending,
    isRefreshing: query.isFetching,
    error: query.error,
    reload: async () => { await query.refetch(); },
  });
}
