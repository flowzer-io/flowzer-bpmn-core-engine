import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseQueryOptions,
} from '@tanstack/react-query';
import { flowzerQueryKeys, useFlowzer } from '@flowzer/react';

import {
  definitionsApi,
  foldersApi,
  formsApi,
  formSectionsApi,
  identityDirectoryApi,
  instancesApi,
  operationsApi,
  notificationsApi,
  aiConnectionsApi,
  aiToolsApi,
  decisionsApi,
} from './endpoints';
import type {
  BpmnMetaDefinitionDto,
  BpmnCapabilityContract,
  DirectorySubjectSearchResultDto,
  DirectorySubjectResolutionResultDto,
  FolderAssignmentDto,
  WorkflowFolderDto,
  WorkflowFolderRequestDto,
  ExtendedBpmnMetaDefinitionDto,
  FormDto,
  FormAuthoringDraftDto,
  FormAuthoringPreviewDto,
  FormCompatibilityItemDto,
  SaveFormAuthoringDraftRequestDto,
  FormMetaDataDto,
  FormFolderDto,
  FormFolderRequestDto,
  FormVersionSummaryDto,
  OperationsDiagnosticsDto,
  ProcessInstanceInfoDto,
  ProcessVariables,
  TimerSubscriptionDto,
  VersionDto,
  SubjectRefDto,
  NotificationDto,
  FormDirectorySearchContext,
  FormSectionMetadataDto,
  FormSectionVersionSummaryDto,
  FormSectionAuthoringDraftDto,
  FormSectionVersionDto,
  SaveFormSectionAuthoringDraftRequestDto,
  AiConnectionDto,
  AiToolDto,
  CreateAiConnectionInput,
  UpdateAiConnectionInput,
  DecisionDefinition,
} from './types';

/** Zentrale Query-Keys — verhindert Tippfehler beim Invalidieren. */
export const queryKeys = {
  definitions: ['definitions'] as const,
  definitionCapabilities: ['definitions', 'capabilities'] as const,
  definitionMeta: () => [...queryKeys.definitions, 'meta'] as const,
  definitionLatest: (definitionId: string) => [...queryKeys.definitions, 'latest', definitionId] as const,
  definitionXml: (versionGuid: string) => [...queryKeys.definitions, 'xml', versionGuid] as const,
  definitionStartForm: (definitionId: string) => [...queryKeys.definitions, 'start-form', definitionId] as const,

  identityDirectory: ['identityDirectory'] as const,
  directorySubjects: (definitionId: string, query: string, kind: 'user' | 'group') =>
    [...queryKeys.identityDirectory, 'workflow', definitionId, kind, query] as const,
  folderDirectorySubjects: (folderId: string, query: string, kind: 'all' | 'user' | 'group') =>
    [...queryKeys.identityDirectory, 'folder', folderId, kind, query] as const,
  directorySubjectResolution: (definitionId: string, subjects: string) =>
    [...queryKeys.identityDirectory, 'workflow', definitionId, 'resolve', subjects] as const,
  folderDirectorySubjectResolution: (folderId: string, subjects: string) =>
    [...queryKeys.identityDirectory, 'folder', folderId, 'resolve', subjects] as const,

  folders: ['folders'] as const,
  folderList: () => [...queryKeys.folders, 'list'] as const,

  instances: ['instances'] as const,
  instanceList: () => [...queryKeys.instances, 'list'] as const,
  instance: (instanceId: string) => [...queryKeys.instances, 'detail', instanceId] as const,
  instanceSubscriptions: (instanceId: string) =>
    [...queryKeys.instances, 'subscriptions', instanceId] as const,
  instanceChildren: (instanceId: string) =>
    [...queryKeys.instances, 'children', instanceId] as const,
  /**
   * Kennungen und Zuordnung stehen sortiert im Schlüssel: dieselbe Auswahl mit derselben
   * Zuordnung ist dieselbe Prüfung — und eine geänderte Zuordnung ist eine andere, die
   * nicht aus dem Cache der alten beantwortet werden darf.
   *
   * Bewusst nicht unter `instances`: Die Migration verwirft die Instanzansichten, die
   * Vorschau aber ist die Grundlage genau dieser Entscheidung und bleibt dabei stehen.
   */
  instanceMigrationPreview: (
    instanceIds: readonly string[],
    flowNodeMapping?: Readonly<Record<string, string>>,
  ) =>
    [
      'instance-migration-preview',
      [...instanceIds].sort(),
      Object.entries(flowNodeMapping ?? {}).sort(([left], [right]) => left.localeCompare(right)),
    ] as const,

  forms: ['forms'] as const,
  formList: () => [...queryKeys.forms, 'list'] as const,
  form: (formId: string) => [...queryKeys.forms, 'detail', formId] as const,
  formDraft: (formId: string) => [...queryKeys.forms, 'draft', formId] as const,
  formPreview: (formId: string, formData: string) =>
    [...queryKeys.forms, 'preview', formId, formData] as const,
  formCompatibility: (needsMigration?: boolean) =>
    [...queryKeys.forms, 'compatibility', needsMigration ?? null] as const,
  formFolders: () => [...queryKeys.forms, 'folders'] as const,
  formVersions: (formId: string) => [...queryKeys.forms, 'versions', formId] as const,

  formSections: ['formSections'] as const,
  formSectionList: () => [...queryKeys.formSections, 'list'] as const,
  formSectionVersions: (sectionId: string) => [...queryKeys.formSections, 'versions', sectionId] as const,
  formSectionDraft: (sectionId: string) => [...queryKeys.formSections, 'draft', sectionId] as const,

  decisions: ['decisions'] as const,
  decisionList: () => [...queryKeys.decisions, 'list'] as const,
  decision: (decisionDefinitionId: string) => [...queryKeys.decisions, 'detail', decisionDefinitionId] as const,
  decisionVersions: (decisionDefinitionId: string) =>
    [...queryKeys.decisions, 'versions', decisionDefinitionId] as const,

  aiConnections: ['aiConnections'] as const,
  aiConnectionList: () => [...queryKeys.aiConnections, 'list'] as const,
  aiTools: ['aiTools'] as const,
  aiToolList: () => [...queryKeys.aiTools, 'list'] as const,

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

/** Der Vertrag ist versioniert und kann deshalb für die Sitzung gecacht werden. */
export function useBpmnCapabilities() {
  return useQuery<BpmnCapabilityContract>({
    queryKey: queryKeys.definitionCapabilities,
    queryFn: ({ signal }) => definitionsApi.capabilities(signal),
    staleTime: 5 * 60_000,
  });
}

/** Prüft das aktuelle Modell vor Save oder Deploy, ohne eine Version anzulegen. */
export function useValidateDefinition() {
  return useMutation({
    mutationFn: ({ xml, deployment }: { xml: string; deployment: boolean }) =>
      definitionsApi.validate(xml, deployment),
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

/** Löst bereits gespeicherte IDs in einem begrenzten, workflowgebundenen Batch auf. */
export function useDirectorySubjectResolutions(
  definitionId: string,
  subjects: SubjectRefDto[],
  enabled = true,
) {
  const uniqueSubjects = uniqueSubjectRefs(subjects);
  const subjectKey = subjectResolutionKey(uniqueSubjects);
  const result = useQuery<DirectorySubjectResolutionResultDto>({
    queryKey: queryKeys.directorySubjectResolution(definitionId, subjectKey),
    queryFn: ({ signal }) => identityDirectoryApi.resolveSubjects(definitionId, uniqueSubjects, signal),
    enabled: enabled && definitionId.length > 0 && uniqueSubjects.length > 0,
    staleTime: 30_000,
  });
  return { ...result, data: result.data?.items ?? [] };
}

/** Löst gespeicherte Ordnerreferenzen als Batch auf; unbekannte IDs bleiben im Picker sichtbar. */
export function useFolderDirectorySubjectResolutions(
  folderId: string,
  subjects: SubjectRefDto[],
  enabled = true,
) {
  const uniqueSubjects = uniqueSubjectRefs(subjects);
  const subjectKey = subjectResolutionKey(uniqueSubjects);
  const result = useQuery<DirectorySubjectResolutionResultDto>({
    queryKey: queryKeys.folderDirectorySubjectResolution(folderId, subjectKey),
    queryFn: ({ signal }) => identityDirectoryApi.resolveFolderSubjects(folderId, uniqueSubjects, signal),
    enabled: enabled && folderId.length > 0 && uniqueSubjects.length > 0,
    staleTime: 30_000,
  });
  return { ...result, data: result.data?.items ?? [] };
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
  const uniqueSubjects = uniqueSubjectRefs(subjects);
  const subjectKey = subjectResolutionKey(uniqueSubjects);
  const result = useQuery<DirectorySubjectResolutionResultDto>({
    queryKey: [...queryKeys.identityDirectory, 'form', contextKey, fieldKey, 'resolve', subjectKey],
    queryFn: ({ signal }) => identityDirectoryApi.resolveFormSubjects(
      context!, fieldKey, uniqueSubjects, signal,
    ),
    enabled: enabled && Boolean(context) && fieldKey.length > 0 && uniqueSubjects.length > 0,
    staleTime: 30_000,
  });
  return { ...result, data: result.data?.items ?? [] };
}

function uniqueSubjectRefs(subjects: SubjectRefDto[]): SubjectRefDto[] {
  return subjects.filter((subject, index) => subjects.findIndex(
    (candidate) => candidate.kind === subject.kind && candidate.id === subject.id,
  ) === index);
}

function subjectResolutionKey(subjects: SubjectRefDto[]): string {
  return subjects.map((subject) => `${subject.kind}:${subject.id}`).join('|');
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

/**
 * Verwirft alles, was von einem Eingriff in eine laufende Instanz abhängt.
 *
 * Abbruch und Migration greifen an derselben Stelle an, deshalb steht die Liste hier
 * einmal: Instanzliste, Detail, Warteobjekte und Laufzeitdiagramm, die Aufgabenliste
 * (offene Aufgaben entfallen bzw. hängen danach an einem anderen Formular) — und die
 * Betriebssicht, denn deren Timer- und Diagnoselisten zählen genau diese Warteobjekte.
 */
function invalidateInstanceViews(
  queryClient: ReturnType<typeof useQueryClient>,
  cacheNamespace: string,
  sessionScope: string,
): void {
  void queryClient.invalidateQueries({ queryKey: queryKeys.instances });
  void queryClient.invalidateQueries({ queryKey: queryKeys.operations });
  void queryClient.invalidateQueries({
    queryKey: flowzerQueryKeys.instances(cacheNamespace, sessionScope),
  });
  void queryClient.invalidateQueries({
    queryKey: flowzerQueryKeys.userTasks(cacheNamespace, sessionScope),
  });
}

/**
 * Bricht eine laufende Instanz ab.
 *
 * Die API antwortet mit der bereits beendeten Instanz. Sie wird sofort in den Cache
 * geschrieben, damit das Detail nicht bis zum nächsten Abruf weiter „Wartet“ anzeigt und
 * den Abbruch ein zweites Mal anbietet.
 */
export function useCancelInstance() {
  const queryClient = useQueryClient();
  const { cacheNamespace, sessionScope } = useFlowzer();
  return useMutation({
    mutationFn: (instanceId: string) => instancesApi.cancel(instanceId),
    onSuccess: (instance) => {
      queryClient.setQueryData(queryKeys.instance(instance.instanceId), instance);
      invalidateInstanceViews(queryClient, cacheNamespace, sessionScope);
    },
  });
}

/**
 * Prüft folgenlos, welche der ausgewählten Instanzen sich auf die aktuell deployte
 * Version heben lassen.
 *
 * Bewusst ohne Nachladeintervall und ohne Frischezeit: Das Ergebnis wird zur
 * Betriebsentscheidung gelesen, nicht überwacht — und ein zwischenzeitliches Deployment
 * darf nie als noch gültige Vorschau aus dem Cache kommen.
 */
export function useInstanceMigrationPreview(
  instanceIds: string[] | undefined,
  flowNodeMapping?: Record<string, string>,
) {
  const ids = [...(instanceIds ?? [])].sort();
  return useQuery({
    queryKey: queryKeys.instanceMigrationPreview(ids, flowNodeMapping),
    queryFn: ({ signal }) => instancesApi.migrationPreview(ids, flowNodeMapping, signal),
    enabled: ids.length > 0,
    // Eine geänderte Zuordnung prüft dieselbe Auswahl erneut. Der bisherige Stand bleibt
    // solange stehen, damit der Zuordnungsblock unter der Hand des Bedienenden nicht
    // verschwindet — für eine andere Auswahl gilt er dagegen nie.
    placeholderData: (previous, previousQuery) =>
      isSameSelection(previousQuery?.queryKey, ids) ? previous : undefined,
    staleTime: 0,
    refetchInterval: false,
    // Die Grundlage einer Entscheidung wechselt nicht still unter dem Lesenden. Ob sie noch
    // gilt, prüft die API beim Migrieren (409), nicht ein Fokuswechsel des Fensters.
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false,
  });
}

/**
 * Ob der Schlüssel einer früheren Vorschau dieselben Instanzen meint.
 *
 * Nur dann darf ihr Ergebnis beim Wechsel der Zuordnung stehen bleiben; die Vorschau einer
 * anderen Auswahl nennt andere Versionen und wäre eine falsche Auskunft.
 */
function isSameSelection(queryKey: unknown, ids: readonly string[]): boolean {
  if (!Array.isArray(queryKey)) return false;

  const previousIds: unknown[] = Array.isArray(queryKey[1]) ? queryKey[1] : [];
  return previousIds.length === ids.length && previousIds.every((id, index) => id === ids[index]);
}

export interface MigrateInstancesInput {
  instanceIds: string[];
  targetDefinitionId: string;
  /** Zuordnung wartender Quellknoten auf Knoten der Zielversion; gilt für alle Instanzen. */
  flowNodeMapping?: Record<string, string>;
}

/** Hebt die Instanzen auf die Zielversion; danach veraltet dasselbe wie nach einem Abbruch. */
export function useMigrateInstances() {
  const queryClient = useQueryClient();
  const { cacheNamespace, sessionScope } = useFlowzer();
  return useMutation({
    mutationFn: ({ instanceIds, targetDefinitionId, flowNodeMapping }: MigrateInstancesInput) =>
      instancesApi.migrate(instanceIds, targetDefinitionId, flowNodeMapping),
    onSuccess: () => invalidateInstanceViews(queryClient, cacheNamespace, sessionScope),
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

/**
 * Die von Call Activities dieser Instanz gestarteten Kindinstanzen.
 *
 * Gleiche Machart wie {@link useInstance} und {@link useInstanceSubscriptions}: Die Anfrage
 * läuft erst mit einer Kennung, und der Zustand der Kinder ändert sich im selben Takt wie
 * der der Instanz selbst — eine fertig gewordene Kindinstanz soll die Elternansicht nicht
 * länger als andere Laufzeitdaten falsch zeigen.
 */
export function useInstanceChildren(instanceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.instanceChildren(instanceId ?? ''),
    queryFn: ({ signal }) => instancesApi.children(instanceId!, signal),
    enabled: Boolean(instanceId),
    refetchInterval: LIVE_REFETCH_MS,
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

export function useFormFolders(options?: QueryTuning<FormFolderDto[]>) {
  return useQuery({
    queryKey: queryKeys.formFolders(),
    queryFn: ({ signal }) => formsApi.listFolders(signal),
    staleTime: 30_000,
    ...options,
  });
}

export function useFormVersions(formId: string | undefined) {
  return useQuery<FormVersionSummaryDto[]>({
    queryKey: queryKeys.formVersions(formId ?? ''),
    queryFn: ({ signal }) => formsApi.listVersions(formId!, signal),
    enabled: Boolean(formId),
  });
}

export function useCreateFormFolder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (folder: FormFolderRequestDto) => formsApi.createFolder(folder),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.formFolders() }),
  });
}

export function useUpdateFormFolder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, folder }: { id: string; folder: FormFolderRequestDto }) =>
      formsApi.updateFolder(id, folder),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.formFolders() }),
  });
}

export function useDeleteFormFolder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => formsApi.deleteFolder(id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.formFolders() }),
  });
}

export function useMoveFormToFolder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ formId, folderId }: { formId: string; folderId: string | null }) =>
      formsApi.moveToFolder(formId, folderId),
    onSuccess: (form) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.formList() });
      queryClient.setQueryData([...queryKeys.formList(), null], (current: FormMetaDataDto[] | undefined) =>
        current?.map((item) => item.formId === form.formId ? form : item));
    },
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

/**
 * Die Vorschau ist ein read-only POST, weil das unveroeffentlichte Schema zu gross fuer
 * eine URL sein kann. TanStack Query sorgt trotzdem fuer Abbruch und Server-State-Lebenszyklus.
 */
export function useFormAuthoringPreview(
  formId: string | undefined,
  formData: string | undefined,
  enabled: boolean,
) {
  return useQuery<FormAuthoringPreviewDto>({
    queryKey: queryKeys.formPreview(formId ?? '', formData ?? ''),
    queryFn: ({ signal }) => formsApi.previewDraft(formId!, formData!, signal),
    enabled: enabled && Boolean(formId) && formData !== undefined,
    retry: false,
    staleTime: Number.POSITIVE_INFINITY,
    gcTime: 0,
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
        queryClient.invalidateQueries({ queryKey: queryKeys.formVersions(variables.formId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.formList() }),
        queryClient.invalidateQueries({ queryKey: [...queryKeys.forms, 'compatibility'] }),
      ]);
    },
  });
}

export function useSaveFormMeta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ formId, name, folderId }: { formId: string; name: string; folderId?: string | null }) =>
      formsApi.saveMeta(formId, name, folderId),
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

/* -------------------------------------------------- Wiederverwendbare Abschnitte */

export function useFormSections(options?: QueryTuning<FormSectionMetadataDto[]>) {
  return useQuery({
    queryKey: queryKeys.formSectionList(),
    queryFn: ({ signal }) => formSectionsApi.list(signal),
    staleTime: 30_000,
    ...options,
  });
}

export function useFormSectionVersions(sectionId: string | undefined) {
  return useQuery<FormSectionVersionSummaryDto[]>({
    queryKey: queryKeys.formSectionVersions(sectionId ?? ''),
    queryFn: ({ signal }) => formSectionsApi.listVersions(sectionId!, signal),
    enabled: Boolean(sectionId),
    staleTime: 30_000,
    retry: false,
  });
}

export function useFormSectionDraft(sectionId: string | undefined) {
  return useQuery<FormSectionAuthoringDraftDto>({
    queryKey: queryKeys.formSectionDraft(sectionId ?? ''),
    queryFn: ({ signal }) => formSectionsApi.getDraft(sectionId!, signal),
    enabled: Boolean(sectionId),
    retry: false,
  });
}

export function useSaveFormSectionDraft() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ sectionId, draft }: { sectionId: string; draft: SaveFormSectionAuthoringDraftRequestDto }) =>
      formSectionsApi.saveDraft(sectionId, draft),
    onSuccess: (saved) => {
      queryClient.setQueryData(queryKeys.formSectionDraft(saved.sectionId), saved);
      void queryClient.invalidateQueries({ queryKey: queryKeys.formSectionList() });
    },
  });
}

export function useDiscardFormSectionDraft() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ sectionId, expectedRevision }: { sectionId: string; expectedRevision: number }) =>
      formSectionsApi.deleteDraft(sectionId, expectedRevision),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.formSectionDraft(variables.sectionId) });
    },
  });
}

export function usePublishFormSectionDraft() {
  const queryClient = useQueryClient();
  return useMutation<FormSectionVersionDto, unknown, { sectionId: string; expectedRevision: number }>({
    mutationFn: ({ sectionId, expectedRevision }) => formSectionsApi.publishDraft(sectionId, expectedRevision),
    onSuccess: (_published, variables) => {
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.formSectionDraft(variables.sectionId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.formSectionVersions(variables.sectionId) }),
      ]);
    },
  });
}

export function useCreateFormSection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => formSectionsApi.create(name),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.formSectionList() }),
  });
}

export function useRenameFormSection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ sectionId, name }: { sectionId: string; name: string }) => formSectionsApi.rename(sectionId, name),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.formSectionList() }),
  });
}

/* ------------------------------------------------------------- KI-Verbindungen */

export function useAiConnections(options?: QueryTuning<AiConnectionDto[]>) {
  return useQuery({
    queryKey: queryKeys.aiConnectionList(),
    queryFn: ({ signal }) => aiConnectionsApi.list(signal),
    staleTime: 30_000,
    ...options,
  });
}

export function useAiTools(options?: QueryTuning<AiToolDto[]>) {
  return useQuery({
    queryKey: queryKeys.aiToolList(),
    queryFn: ({ signal }) => aiToolsApi.list(signal),
    staleTime: 5 * 60_000,
    ...options,
  });
}

export function useCreateAiConnection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateAiConnectionInput) => aiConnectionsApi.create(input),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.aiConnections }),
  });
}

export function useUpdateAiConnection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ connectionId, input }: { connectionId: string; input: UpdateAiConnectionInput }) =>
      aiConnectionsApi.update(connectionId, input),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.aiConnections }),
  });
}

export function useSetAiConnectionEnabled() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ connectionId, expectedRevision, enabled }: {
      connectionId: string;
      expectedRevision: number;
      enabled: boolean;
    }) => aiConnectionsApi.setEnabled(connectionId, expectedRevision, enabled),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.aiConnections }),
  });
}

/* ---------------------------------------------------------------- Entscheidungen */

export function useDecisions(options?: QueryTuning<DecisionDefinition[]>) {
  return useQuery({
    queryKey: queryKeys.decisionList(),
    queryFn: ({ signal }) => decisionsApi.list(signal),
    staleTime: 30_000,
    ...options,
  });
}

/** Die jüngste Version samt DMN-XML. Ohne Kennung bleibt die Abfrage aus. */
export function useDecision(decisionDefinitionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.decision(decisionDefinitionId ?? ''),
    queryFn: ({ signal }) => decisionsApi.get(decisionDefinitionId!, signal),
    enabled: Boolean(decisionDefinitionId),
  });
}

export function useDecisionVersions(decisionDefinitionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.decisionVersions(decisionDefinitionId ?? ''),
    queryFn: ({ signal }) => decisionsApi.listVersions(decisionDefinitionId!, signal),
    enabled: Boolean(decisionDefinitionId),
  });
}

export function useCreateDecision() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { name?: string; xml: string }) => decisionsApi.create(input),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.decisions }),
  });
}

/** Speichern ist immer eine neue Version; der Cache der alten wird deshalb verworfen. */
export function useUpdateDecision() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ decisionDefinitionId, input }: {
      decisionDefinitionId: string;
      input: { name?: string; xml: string };
    }) => decisionsApi.update(decisionDefinitionId, input),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.decisions }),
  });
}

export function useDeleteDecision() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (decisionDefinitionId: string) => decisionsApi.remove(decisionDefinitionId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.decisions }),
  });
}

/**
 * Der Trockenlauf wertet nur aus und ändert nichts — deshalb eine Mutation ohne
 * Invalidierung: Er soll genau dann laufen, wenn jemand ihn auslöst.
 */
export function useEvaluateDecision() {
  return useMutation({
    mutationFn: ({ decisionDefinitionId, decisionId, variables }: {
      decisionDefinitionId: string;
      decisionId: string;
      variables: Record<string, unknown>;
    }) => decisionsApi.evaluate(decisionDefinitionId, { decisionId, variables }),
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
