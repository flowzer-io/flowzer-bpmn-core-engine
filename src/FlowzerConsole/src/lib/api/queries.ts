import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseQueryOptions,
} from '@tanstack/react-query';

import { definitionsApi, formsApi, instancesApi, operationsApi, userTasksApi } from './endpoints';
import type {
  BpmnMetaDefinitionDto,
  ExtendedBpmnMetaDefinitionDto,
  ExtendedUserTaskSubscriptionDto,
  FormDto,
  FormMetaDataDto,
  OperationsDiagnosticsDto,
  ProcessInstanceInfoDto,
  TimerSubscriptionDto,
  UserTaskResultDto,
  VersionDto,
} from './types';

/** Zentrale Query-Keys — verhindert Tippfehler beim Invalidieren. */
export const queryKeys = {
  definitions: ['definitions'] as const,
  definitionMeta: () => [...queryKeys.definitions, 'meta'] as const,
  definitionLatest: (definitionId: string) => [...queryKeys.definitions, 'latest', definitionId] as const,
  definitionXml: (versionGuid: string) => [...queryKeys.definitions, 'xml', versionGuid] as const,

  instances: ['instances'] as const,
  instanceList: () => [...queryKeys.instances, 'list'] as const,
  instance: (instanceId: string) => [...queryKeys.instances, 'detail', instanceId] as const,
  instanceSubscriptions: (instanceId: string) =>
    [...queryKeys.instances, 'subscriptions', instanceId] as const,

  userTasks: ['userTasks'] as const,
  userTaskList: () => [...queryKeys.userTasks, 'list'] as const,
  userTaskForm: (userTaskId: string) => [...queryKeys.userTasks, 'form', userTaskId] as const,

  forms: ['forms'] as const,
  formList: () => [...queryKeys.forms, 'list'] as const,
  form: (formId: string) => [...queryKeys.forms, 'detail', formId] as const,

  operations: ['operations'] as const,
  diagnostics: () => [...queryKeys.operations, 'diagnostics'] as const,
  timers: () => [...queryKeys.operations, 'timers'] as const,
  health: () => [...queryKeys.operations, 'health'] as const,
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
    mutationFn: (name: string) => definitionsApi.create(name),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.definitionMeta() });
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
    },
  });
}

export function useStartInstance() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (definitionId: string) => definitionsApi.startInstance(definitionId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.instances });
      void queryClient.invalidateQueries({ queryKey: queryKeys.userTasks });
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

/* ------------------------------------------------------------------- Aufgaben */

export function useUserTasks(options?: QueryTuning<ExtendedUserTaskSubscriptionDto[]>) {
  return useQuery({
    queryKey: queryKeys.userTaskList(),
    queryFn: ({ signal }) => userTasksApi.list(signal),
    refetchInterval: LIVE_REFETCH_MS,
    ...options,
  });
}

export function useUserTaskForm(userTaskId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.userTaskForm(userTaskId ?? ''),
    queryFn: ({ signal }) => userTasksApi.getForm(userTaskId!, signal),
    enabled: Boolean(userTaskId),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useCompleteUserTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (result: UserTaskResultDto) => userTasksApi.complete(result),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.userTasks });
      void queryClient.invalidateQueries({ queryKey: queryKeys.instances });
      void queryClient.invalidateQueries({ queryKey: queryKeys.operations });
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
  return useMutation({
    mutationFn: (formId: string) => formsApi.deleteMeta(formId),
    onSuccess: (_data, formId) => {
      // Die Detailabfrage des geloeschten Formulars wird nicht nur ungueltig, sie ist
      // gegenstandslos — sonst bleibt sie als Leiche im Zwischenspeicher liegen.
      queryClient.removeQueries({ queryKey: queryKeys.form(formId) });
      // Nicht nur die Liste: Aufgaben loesen ihr Formular ueber den Namen auf, die
      // Aufgabenansicht muss ein geloeschtes also neu bewerten.
      void queryClient.invalidateQueries({ queryKey: queryKeys.forms });
      void queryClient.invalidateQueries({ queryKey: queryKeys.userTasks });
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
