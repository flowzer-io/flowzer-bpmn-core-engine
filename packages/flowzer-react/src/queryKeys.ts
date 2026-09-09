import type { QueryClient } from '@tanstack/react-query';

export const flowzerQueryKeys = {
  scope: (cacheNamespace: string, sessionScope: string) =>
    ['flowzer', cacheNamespace, sessionScope] as const,
  userTasks: (cacheNamespace: string, sessionScope: string) =>
    [...flowzerQueryKeys.scope(cacheNamespace, sessionScope), 'user-tasks'] as const,
  userTask: (cacheNamespace: string, sessionScope: string, userTaskId: string) =>
    [...flowzerQueryKeys.userTasks(cacheNamespace, sessionScope), 'detail', userTaskId] as const,
  userTaskForm: (cacheNamespace: string, sessionScope: string, userTaskId: string) =>
    [...flowzerQueryKeys.userTask(cacheNamespace, sessionScope, userTaskId), 'form'] as const,
  userTaskDraft: (cacheNamespace: string, sessionScope: string, userTaskId: string) =>
    [...flowzerQueryKeys.userTask(cacheNamespace, sessionScope, userTaskId), 'draft'] as const,
  instances: (cacheNamespace: string, sessionScope: string) =>
    [...flowzerQueryKeys.scope(cacheNamespace, sessionScope), 'instances'] as const,
  instance: (cacheNamespace: string, sessionScope: string, instanceId: string) =>
    [...flowzerQueryKeys.instances(cacheNamespace, sessionScope), instanceId] as const,
  instanceHistory: (cacheNamespace: string, sessionScope: string, instanceId: string) =>
    [...flowzerQueryKeys.instance(cacheNamespace, sessionScope, instanceId), 'history'] as const,
};

/** Entfernt beim Host-Logout die Daten genau einer früheren Sitzung aus dem QueryClient. */
export function clearFlowzerScope(
  queryClient: QueryClient,
  cacheNamespace: string,
  sessionScope: string,
): void {
  queryClient.removeQueries({ queryKey: flowzerQueryKeys.scope(cacheNamespace, sessionScope) });
}
