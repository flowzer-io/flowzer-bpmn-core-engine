import type { ReactNode } from 'react';

import type {
  ExtendedUserTask,
  FormSectionAuthoringDraft,
  FormSectionMetadata,
  FormSectionVersionSummary,
  ProcessInstance,
  RuntimeDiagram,
} from '@flowzer/sdk';

import {
  useInstanceStatus,
  useInstanceRuntimeDiagram,
  useFormSection,
  useFormSectionActions,
  useFormSectionDraft,
  useFormSectionVersions,
  useFormSections,
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

export interface InstanceRuntimeDiagramControllerProps extends FlowzerQueryOptions {
  instanceId: string;
  children: (state: AsyncControllerState<RuntimeDiagram | undefined>) => ReactNode;
}

/** Darstellungsfreier Controller; Diagramm, Timeline und Texte bleiben Aufgabe des Hosts. */
export function InstanceRuntimeDiagramController({
  instanceId,
  children,
  ...options
}: InstanceRuntimeDiagramControllerProps) {
  const query = useInstanceRuntimeDiagram(instanceId, options);
  return children({
    data: query.data,
    isPending: query.isPending,
    isRefreshing: query.isFetching,
    error: query.error,
    reload: async () => { await query.refetch(); },
  });
}

export interface FormSectionListControllerProps extends FlowzerQueryOptions {
  children: (state: AsyncControllerState<FormSectionMetadata[]>) => ReactNode;
}

/** Darstellungsfreier Katalogcontroller; der Host entscheidet über Navigation und UI. */
export function FormSectionListController({ children, ...options }: FormSectionListControllerProps) {
  const query = useFormSections(options);
  return children({
    data: query.data ?? [],
    isPending: query.isPending,
    isRefreshing: query.isFetching,
    error: query.error,
    reload: async () => { await query.refetch(); },
  });
}

export interface FormSectionEditorControllerState {
  section: FormSectionMetadata | undefined;
  versions: FormSectionVersionSummary[];
  draft: FormSectionAuthoringDraft | undefined;
  isPending: boolean;
  isRefreshing: boolean;
  error: Error | null;
  reload: () => Promise<void>;
  actions: ReturnType<typeof useFormSectionActions>;
}

export interface FormSectionEditorControllerProps extends FlowzerQueryOptions {
  sectionId: string;
  children: (state: FormSectionEditorControllerState) => ReactNode;
}

/**
 * Stellt Metadaten, konkrete Versionen und den CAS-Entwurf eines Abschnitts bereit,
 * ohne ein Schema zu rendern oder konkrete Host-Anwendungsbegriffe zu kennen.
 */
export function FormSectionEditorController({
  sectionId,
  children,
  ...options
}: FormSectionEditorControllerProps) {
  const section = useFormSection(sectionId, options);
  const versions = useFormSectionVersions(sectionId, options);
  const draft = useFormSectionDraft(sectionId, options);
  const actions = useFormSectionActions(sectionId);
  return children({
    section: section.data,
    versions: versions.data ?? [],
    draft: draft.data,
    isPending: section.isPending || versions.isPending || draft.isPending,
    isRefreshing: section.isFetching || versions.isFetching || draft.isFetching,
    error: section.error ?? versions.error ?? draft.error,
    reload: async () => { await Promise.all([section.refetch(), versions.refetch(), draft.refetch()]); },
    actions,
  });
}
