import {
  useMutation,
  useQuery,
  useQueries,
  useQueryClient,
  type UseQueryOptions,
} from '@tanstack/react-query';
import { flowzerQueryKeys, useFlowzer } from '@flowzer/react';

import {
  definitionsApi,
  foldersApi,
  formsApi,
  identityDirectoryApi,
  instancesApi,
  operationsApi,
  notificationsApi,
} from './endpoints';
import type {
  BpmnMetaDefinitionDto,
  DirectorySubjectSearchResultDto,
  DirectorySubjectDto,
  FolderAssignmentDto,
  WorkflowFolderDto,
  WorkflowFolderRequestDto,
  ExtendedBpmnMetaDefinitionDto,
  FormDto,
  FormAuthoringDraftDto,
  FormCompatibilityItemDto,
  SaveFormAuthoringDraftRequestDto,
  FormMetaDataDto,
  OperationsDiagnosticsDto,
  ProcessInstanceInfoDto,
  ProcessVariables,
  TimerSubscriptionDto,
  VersionDto,
  SubjectRefDto,
  NotificationDto,
  FormDirectorySearchContext,
} from './types';

/** Zentrale Query-Keys — verhindert Tippfehler beim Invalidieren. */
export const queryKeys = {
  definitions: ['definitions'] as const,
  definitionMeta: () => [...queryKeys.definitions, 'meta'] as const,
  definitionLatest: (definitionId: string) => [...queryKeys.definitions, 'latest', definitionId] as const,
  definitionXml: (versionGuid: string) => [...queryKeys.definitions, 'xml', versionGuid] as const,
  definitionStartForm: (definitionId: string) => [...queryKeys.definitions, 'start-form', definitionId] as const,

  identityDirectory: ['identityDirectory'] as const,
  directorySubjects: (definitionId: string, query: string, kind: 'user' | 'group') =>
    [...queryKeys.identityDirectory, 'workflow', definitionId, kind, query] as const,
  folderDirectorySubjects: (folderId: string, query: string, kind: 'all' | 'user' | 'group') =>
    [...queryKeys.identityDirectory, 'folder', folderId, kind, query] as const,

  folders: ['folders'] as const,
  folderList: () => [...queryKeys.folders, 'list'] as const,

  instances: ['instances'] as const,
  instanceList: () => [...queryKeys.instances, 'list'] as const,
  instance: (instanceId: string) => [...queryKeys.instances, 'detail', instanceId] as const,
  instanceSubscriptions: (instanceId: string) =>
    [...queryKeys.instances, 'subscriptions', instanceId] as const,

  forms: ['forms'] as const,
  formList: () => [...queryKeys.forms, 'list'] as const,
  form: (formId: string) => [...queryKeys.forms, 'detail', formId] as const,
  formDraft: (formId: string) => [...queryKeys.forms, 'draft', formId] as const,
  formCompatibility: (needsMigration?: boolean) =>
    [...queryKeys.forms, 'compatibility', needsMigration ?? null] as const,

  operations: ['operations'] as const,
  diagnostics: () => [...queryKeys.operations, 'diagnostics'] as const,
  timers: () => [...queryKeys.operations, 'timers'] as const,
  health: () => [...queryKeys.operations, 'health'] as const,
  notifications: ['notifications'] as const,
  notificationList: () => [...queryKeys.notifications, 'list'] as const,
} as const;

/** Live-Daten werden regelmäßig nachgeladen, damit die Konsole den Laufzeitzustand zeigt. */
const LIVE_REFETCH_MS = 10_000;

type QueryTuning<T> = Partial<Pick<UseQueryOptions<T>, 'refetchInterval' | 'enabled' | 'staleTime'>>;

/* ---------------------------------------------------------------- Definitionen */

export function useDefinitions(options?: QueryTuning<ExtendedBpmnMetaDefinitionDto[]>) {
  return useQuery({
    queryKey: queryKeys.definitionMeta(),
    queryFn: ({ signal }) => definitionsApi.listMeta(signal),
    staleTime: 15_000,
    ...options,
  });
}

export function useLatestDefinition(definitionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.definitionLatest(definitionId ?? ''),
    queryFn: ({ signal }) => definitionsApi.getLatest(definitionId!, signal),
    enabled: Boolean(definitionId),
    staleTime: 30_000,
  });
}

export function useDefinitionXml(versionGuid: string | undefined | null) {
  return useQuery({
    queryKey: queryKeys.definitionXml(versionGuid ?? ''),
    queryFn: ({ signal }) => definitionsApi.getXml(versionGuid!, signal),
    enabled: Boolean(versionGuid),
    // BPMN-XML einer Version ist unveränderlich — es muss nie neu geladen werden.
    staleTime: Infinity,
    gcTime: 30 * 60_000,
  });
}

export function useDeployDefinition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ xml, previousGuid }: { xml: string; previousGuid?: string }) =>
      definitionsApi.deploy(xml, previousGuid),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.definitions });
    },
  });
}

export function useSaveDefinition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ xml, previousGuid }: { xml: string; previousGuid?: string }) =>
      definitionsApi.save(xml, previousGuid),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.definitions });
    },
  });
}

export function useUpdateDefinitionMeta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: BpmnMetaDefinitionDto) => definitionsApi.updateMeta(dto),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.definitionMeta() });
    },
  });
}

export function useCreateDefinition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ name, folderId }: { name: string; folderId: string | null }) =>
      definitionsApi.create(name, folderId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.definitionMeta() });
      // Der Ordner zaehlt seine Workflows mit; ohne das bliebe die Zahl im Baum stehen.
      void queryClient.invalidateQueries({ queryKey: queryKeys.folders });
    },
  });
}

/** Verschiebt einen Workflow in einen anderen Ordner; `null` ist die oberste Ebene. */
export function useMoveDefinition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ definitionId, folderId }: { definitionId: string; folderId: string | null }) =>
      definitionsApi.moveToFolder(definitionId, folderId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.definitionMeta() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.folders });
    },
  });
}

export function useDeleteDefinition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (definitionId: string) => definitionsApi.deleteMeta(definitionId),
    onSuccess: () => {
      // Nicht nur der Katalog: Eine geloeschte Definition verschwindet auch aus den
      // Instanz- und Betriebsansichten, die ihren Namen aufloesen.
      void queryClient.invalidateQueries({ queryKey: queryKeys.definitions });
      void queryClient.invalidateQueries({ queryKey: queryKeys.instances });
      // Der Ordner zaehlt seine Workflows mit.
      void queryClient.invalidateQueries({ queryKey: queryKeys.folders });
    },
  });
}

export function useStartInstance() {
  const queryClient = useQueryClient();
  const { cacheNamespace, sessionScope } = useFlowzer();
  return useMutation({
    mutationFn: ({ definitionId, variables }: { definitionId: string; variables?: ProcessVariables }) =>
      definitionsApi.startInstance(definitionId, variables),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.instances });
      void queryClient.invalidateQueries({
        queryKey: flowzerQueryKeys.userTasks(cacheNamespace, sessionScope),
      });
    },
  });
}

/**
 * Sucht ausschließlich innerhalb des Workflows, dessen Modellierrechte die API erneut prüft.
 * Der Browser wählt weder globale Verzeichnisse noch eigene Filtergrenzen.
 */
export function useDirectorySubjectSearch(
  definitionId: string,
  query: string,
  kind: 'user' | 'group',
  enabled = true,
) {
  const normalizedQuery = query.trim();
  return useQuery<DirectorySubjectSearchResultDto>({
    queryKey: queryKeys.directorySubjects(definitionId, normalizedQuery, kind),
    queryFn: ({ signal }) => identityDirectoryApi.searchSubjects(definitionId, normalizedQuery, kind, signal),
    enabled: enabled && definitionId.length > 0 && normalizedQuery.length >= 2,
    staleTime: 30_000,
  });
}

/** Ordnergebundene Suche für Delegationen; der Server prüft die Fachverantwortung erneut. */
export function useFolderDirectorySubjectSearch(
  folderId: string,
  query: string,
  kind: 'all' | 'user' | 'group',
  enabled = true,
) {
  const normalizedQuery = query.trim();
  return useQuery<DirectorySubjectSearchResultDto>({
    queryKey: queryKeys.folderDirectorySubjects(folderId, normalizedQuery, kind),
    queryFn: ({ signal }) => identityDirectoryApi.searchFolderSubjects(folderId, normalizedQuery, kind, signal),
    enabled: enabled && folderId.length > 0 && normalizedQuery.length >= 2,
    staleTime: 30_000,
  });
}

/** Formularfeldsuche mit serverseitig geprüftem Start-/Aufgabenkontext. */
export function useFormDirectorySubjectSearch(
  context: FormDirectorySearchContext | undefined,
  fieldKey: string,
  query: string,
  kind: 'all' | 'user' | 'group',
  enabled = true,
) {
  const normalizedQuery = query.trim();
  const contextKey = context?.kind === 'startForm'
    ? `start:${context.definitionId}`
    : context?.kind === 'userTask'
      ? `task:${context.taskId}`
      : '';
  return useQuery<DirectorySubjectSearchResultDto>({
    queryKey: [...queryKeys.identityDirectory, 'form', contextKey, fieldKey, kind, normalizedQuery],
    queryFn: ({ signal }) => identityDirectoryApi.searchFormSubjects(context!, fieldKey, normalizedQuery, kind, signal),
    enabled: enabled && Boolean(context) && fieldKey.length > 0 && normalizedQuery.length >= 2,
    staleTime: 30_000,
  });
}

/** Löst bereits gespeicherte IDs einzeln auf, ohne einen unbeschränkten Directory-Abruf. */
export function useDirectorySubjectResolutions(
  definitionId: string,
  subjects: SubjectRefDto[],
  enabled = true,
) {
  const uniqueSubjects = subjects.filter(
    (subject, index) =>
      subjects.findIndex(
        (candidate) => candidate.kind === subject.kind && candidate.id === subject.id,
      ) === index,
  );

  return useQueries({
    queries: uniqueSubjects.map((subject) => ({
      queryKey: queryKeys.directorySubjects(definitionId, subject.id, subject.kind),
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        identityDirectoryApi.searchSubjects(definitionId, subject.id, subject.kind, signal),
      enabled: enabled && definitionId.length > 0 && subject.id.length >= 1,
      staleTime: 30_000,
    })),
    combine: (results) => ({
      data: results.flatMap((result, index) => {
        const subject = uniqueSubjects[index];
        if (!subject) return [];
        return (result.data?.items ?? []).filter(
          (item: DirectorySubjectDto) =>
            item.subject.kind === subject.kind && item.subject.id === subject.id,
        );
      }),
      isPending: results.some((result) => result.isPending),
      isFetching: results.some((result) => result.isFetching),
      error: results.find((result) => result.error)?.error ?? null,
    }),
  });
}

/** Löst gespeicherte Ordnerreferenzen einzeln auf; unbekannte IDs bleiben im Picker sichtbar. */
export function useFolderDirectorySubjectResolutions(
  folderId: string,
  subjects: SubjectRefDto[],
  enabled = true,
) {
  const uniqueSubjects = subjects.filter(
    (subject, index) =>
      subjects.findIndex(
        (candidate) => candidate.kind === subject.kind && candidate.id === subject.id,
      ) === index,
  );

  return useQueries({
    queries: uniqueSubjects.map((subject) => ({
      queryKey: queryKeys.folderDirectorySubjects(folderId, subject.id, subject.kind),
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        identityDirectoryApi.searchFolderSubjects(folderId, subject.id, subject.kind, signal),
      enabled: enabled && folderId.length > 0 && subject.id.length >= 1,
      staleTime: 30_000,
    })),
    combine: (results) => ({
      data: results.flatMap((result, index) => {
        const subject = uniqueSubjects[index];
        if (!subject) return [];
        return (result.data?.items ?? []).filter(
          (item: DirectorySubjectDto) =>
            item.subject.kind === subject.kind && item.subject.id === subject.id,
        );
      }),
      isPending: results.some((result) => result.isPending),
      isFetching: results.some((result) => result.isFetching),
      error: results.find((result) => result.error)?.error ?? null,
    }),
  });
}

/** Löst Formularwerte über denselben gebundenen Endpoint wie die Suche auf. */
export function useFormDirectorySubjectResolutions(
  context: FormDirectorySearchContext | undefined,
  fieldKey: string,
  subjects: SubjectRefDto[],
  enabled = true,
) {
  const contextKey = context?.kind === 'startForm'
    ? `start:${context.definitionId}`
    : context?.kind === 'userTask'
      ? `task:${context.taskId}`
      : '';
  const uniqueSubjects = subjects.filter(
    (subject, index) => subjects.findIndex(
      (candidate) => candidate.kind === subject.kind && candidate.id === subject.id,
    ) === index,
  );

  return useQueries({
    queries: uniqueSubjects.map((subject) => ({
      queryKey: [...queryKeys.identityDirectory, 'form', contextKey, fieldKey, subject.kind, subject.id],
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        identityDirectoryApi.searchFormSubjects(context!, fieldKey, subject.id, subject.kind, signal),
      enabled: enabled && Boolean(context) && fieldKey.length > 0,
      staleTime: 30_000,
    })),
    combine: (results) => ({
      data: results.flatMap((result, index) => {
        const subject = uniqueSubjects[index];
        return subject
          ? (result.data?.items ?? []).filter((item) => item.subject.kind === subject.kind && item.subject.id === subject.id)
          : [];
      }),
      isPending: results.some((result) => result.isPending),
      isFetching: results.some((result) => result.isFetching),
      error: results.find((result) => result.error)?.error ?? null,
    }),
  });
}

/* ---------------------------------------------------------------------- Ordner */

export function useFolders(options?: QueryTuning<WorkflowFolderDto[]>) {
  return useQuery({
    queryKey: queryKeys.folderList(),
    queryFn: ({ signal }) => foldersApi.list(signal),
    staleTime: 30_000,
    ...options,
  });
}

export function useCreateFolder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (folder: WorkflowFolderRequestDto) => foldersApi.create(folder),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.folders });
    },
  });
}

export function useUpdateFolder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, folder }: { id: string; folder: WorkflowFolderRequestDto }) =>
      foldersApi.update(id, folder),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.folders });
    },
  });
}

export function useDeleteFolder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => foldersApi.remove(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.folders });
    },
  });
}

/**
 * Setzt die Zuweisungen eines Ordners neu. Danach koennen sich die eigenen Rechte
 * geaendert haben — auch die an den Unterordnern —, deshalb wird der ganze Baum neu geladen.
 */
export function useUpdateFolderAssignments() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, assignments }: { id: string; assignments: FolderAssignmentDto[] }) =>
      foldersApi.updateAssignments(id, assignments),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.folders });
    },
  });
}

/* -------------------------------------------------------------------- Instanzen */

export function useInstances(options?: QueryTuning<ProcessInstanceInfoDto[]>) {
  return useQuery({
    queryKey: queryKeys.instanceList(),
    queryFn: ({ signal }) => instancesApi.list(signal),
    refetchInterval: LIVE_REFETCH_MS,
    ...options,
  });
}

export function useInstance(instanceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.instance(instanceId ?? ''),
    queryFn: ({ signal }) => instancesApi.get(instanceId!, signal),
    enabled: Boolean(instanceId),
    refetchInterval: LIVE_REFETCH_MS,
  });
}

/** Bündelt alle vier Subscription-Listen einer Instanz in einem Hook. */
export function useInstanceSubscriptions(instanceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.instanceSubscriptions(instanceId ?? ''),
    enabled: Boolean(instanceId),
    refetchInterval: LIVE_REFETCH_MS,
    queryFn: async ({ signal }) => {
      const id = instanceId!;
      const [messages, signals, timers, services, userTasks] = await Promise.all([
        instancesApi.messageSubscriptions(id, signal),
        instancesApi.signalSubscriptions(id, signal),
        instancesApi.timerSubscriptions(id, signal),
        instancesApi.serviceSubscriptions(id, signal),
        instancesApi.userTaskSubscriptions(id, signal),
      ]);
      return {
        messages: messages ?? [],
        signals: signals ?? [],
        timers: timers ?? [],
        services: services ?? [],
        userTasks: userTasks ?? [],
      };
    },
  });
}

/** Persistenter Meldungsfeed; der Server bleibt Quelle für Inhalt und Lesestatus. */
export function useNotifications(options?: QueryTuning<NotificationDto[]>) {
  return useQuery({
    queryKey: queryKeys.notificationList(),
    queryFn: ({ signal }) => notificationsApi.list(signal),
    refetchInterval: LIVE_REFETCH_MS,
    ...options,
  });
}

export function useMarkNotificationRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => notificationsApi.markRead(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.notificationList() });
    },
  });
}

/* ------------------------------------------------------------------ Formulare */

export function useForms(search?: string, options?: QueryTuning<FormMetaDataDto[]>) {
  return useQuery({
    queryKey: [...queryKeys.formList(), search ?? null],
    queryFn: ({ signal }) => formsApi.listMeta(search, signal),
    staleTime: 30_000,
    ...options,
  });
}

export function useForm(formId: string | undefined) {
  return useQuery<FormDto>({
    queryKey: queryKeys.form(formId ?? ''),
    queryFn: ({ signal }) => formsApi.getLatest(formId!, signal),
    enabled: Boolean(formId),
    retry: false,
  });
}

export function useSaveForm() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: { formId: string; formData: string; version?: VersionDto }) =>
      formsApi.save(payload),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.form(variables.formId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.formList() });
      void queryClient.invalidateQueries({ queryKey: [...queryKeys.forms, 'compatibility'] });
    },
  });
}

export function useFormAuthoringDraft(formId: string | undefined) {
  return useQuery<FormAuthoringDraftDto>({
    queryKey: queryKeys.formDraft(formId ?? ''),
    queryFn: ({ signal }) => formsApi.getDraft(formId!, signal),
    enabled: Boolean(formId),
    retry: false,
  });
}

export function useFormCompatibilityInventory(enabled = true, needsMigration?: boolean) {
  return useQuery<FormCompatibilityItemDto[]>({
    queryKey: queryKeys.formCompatibility(needsMigration),
    queryFn: ({ signal }) => formsApi.compatibility(needsMigration, signal),
    enabled,
    staleTime: 30_000,
    retry: false,
  });
}

export function useSaveFormAuthoringDraft() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ formId, draft }: { formId: string; draft: SaveFormAuthoringDraftRequestDto }) =>
      formsApi.saveDraft(formId, draft),
    onSuccess: (saved) => {
      queryClient.setQueryData(queryKeys.formDraft(saved.formId), saved);
      void queryClient.invalidateQueries({ queryKey: [...queryKeys.forms, 'compatibility'] });
    },
  });
}

export function useDiscardFormAuthoringDraft() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ formId, expectedRevision }: { formId: string; expectedRevision: number }) =>
      formsApi.deleteDraft(formId, expectedRevision),
    onSuccess: async (_data, variables) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.formDraft(variables.formId) });
      await queryClient.invalidateQueries({ queryKey: [...queryKeys.forms, 'compatibility'] });
    },
  });
}

export function usePublishFormAuthoringDraft() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ formId, expectedRevision }: { formId: string; expectedRevision: number }) =>
      formsApi.publishDraft(formId, expectedRevision),
    onSuccess: async (_published, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.formDraft(variables.formId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.form(variables.formId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.formList() }),
        queryClient.invalidateQueries({ queryKey: [...queryKeys.forms, 'compatibility'] }),
      ]);
    },
  });
}

export function useSaveFormMeta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ formId, name }: { formId: string; name: string }) => formsApi.saveMeta(formId, name),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.formList() });
    },
  });
}

export function useDeleteForm() {
  const queryClient = useQueryClient();
  const { cacheNamespace, sessionScope } = useFlowzer();
  return useMutation({
    mutationFn: (formId: string) => formsApi.deleteMeta(formId),
    onSuccess: (_data, formId) => {
      // Die Detailabfrage des geloeschten Formulars wird nicht nur ungueltig, sie ist
      // gegenstandslos — sonst bleibt sie als Leiche im Zwischenspeicher liegen.
      queryClient.removeQueries({ queryKey: queryKeys.form(formId) });
      queryClient.removeQueries({ queryKey: queryKeys.formDraft(formId) });
      queryClient.removeQueries({ queryKey: [...queryKeys.forms, 'compatibility'] });
      // Nicht nur die Liste: Aufgaben loesen ihr Formular ueber den Namen auf, die
      // Aufgabenansicht muss ein geloeschtes also neu bewerten.
      void queryClient.invalidateQueries({ queryKey: queryKeys.forms });
      void queryClient.invalidateQueries({
        queryKey: flowzerQueryKeys.userTasks(cacheNamespace, sessionScope),
      });
    },
  });
}

/* --------------------------------------------------------------------- Betrieb */

export function useDiagnostics(options?: QueryTuning<OperationsDiagnosticsDto>) {
  return useQuery({
    queryKey: queryKeys.diagnostics(),
    queryFn: ({ signal }) => operationsApi.diagnostics(signal),
    refetchInterval: LIVE_REFETCH_MS,
    ...options,
  });
}

export function useTimers(options?: QueryTuning<TimerSubscriptionDto[]>) {
  return useQuery({
    queryKey: queryKeys.timers(),
    queryFn: ({ signal }) => operationsApi.timers(signal),
    refetchInterval: LIVE_REFETCH_MS,
    ...options,
  });
}

export function useHealth() {
  return useQuery({
    queryKey: queryKeys.health(),
    queryFn: ({ signal }) => operationsApi.health(signal),
    refetchInterval: 30_000,
  });
}
