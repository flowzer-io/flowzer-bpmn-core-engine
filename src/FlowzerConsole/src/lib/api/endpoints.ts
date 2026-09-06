import { request, requestStatus, requestStatusResult } from './client';
import { normalizeInstance } from './normalize';
import type {
  BpmnDefinitionDto,
  BpmnMetaDefinitionDto,
  ExtendedBpmnMetaDefinitionDto,
  ExtendedUserTaskSubscriptionDto,
  FormDto,
  FormMetaDataDto,
  HealthStatusDto,
  MessageDto,
  MessageSubscriptionDto,
  OperationsDiagnosticsDto,
  ProcessInstanceInfoDto,
  ProcessVariables,
  SignalSubscriptionDto,
  TimerSubscriptionDto,
  TokenDto,
  UserTaskResultDto,
  VersionDto,
} from './types';

/** Alle Aufrufe gegen die Flowzer-API, gruppiert nach Controller. */

export const definitionsApi = {
  /** `GET /definition/meta` — Katalog aller Prozessdefinitionen. */
  listMeta: (signal?: AbortSignal) =>
    requestStatusResult<ExtendedBpmnMetaDefinitionDto[]>('/definition/meta', { signal }),

  /** `GET /definition/meta/{id}` */
  getMeta: (definitionId: string, signal?: AbortSignal) =>
    requestStatusResult<BpmnMetaDefinitionDto>(`/definition/meta/${encodeURIComponent(definitionId)}`, { signal }),

  /** `POST /definition/meta` — legt einen neuen Katalogeintrag an. */
  createMeta: (dto: BpmnMetaDefinitionDto) =>
    requestStatusResult<BpmnMetaDefinitionDto>('/definition/meta', { method: 'POST', body: dto }),

  /** `PUT /definition/meta` — benennt einen Katalogeintrag um. */
  updateMeta: (dto: BpmnMetaDefinitionDto) =>
    requestStatusResult<BpmnMetaDefinitionDto>('/definition/meta', { method: 'PUT', body: dto }),

  /** `GET /definition` — alle gespeicherten Versionen. */
  listVersions: (signal?: AbortSignal) => requestStatusResult<BpmnDefinitionDto[]>('/definition', { signal }),

  /** `GET /definition/meta/{id}/latest` — neueste Version einer Definition. */
  getLatest: (definitionId: string, signal?: AbortSignal) =>
    requestStatusResult<BpmnDefinitionDto>(`/definition/meta/${encodeURIComponent(definitionId)}/latest`, { signal }),

  /** `GET /definition/xml/{guid}` — BPMN-XML einer konkreten Version. */
  getXml: (versionGuid: string, signal?: AbortSignal) =>
    request<string>(`/definition/xml/${encodeURIComponent(versionGuid)}`, { signal, asText: true }),

  /**
   * `POST /definition/new` — erzeugt eine leere Definition inklusive Katalogeintrag.
   * Der Name wird mitgegeben: Die Oberflaeche fragt ihn, bevor sie anlegt.
   */
  create: (name: string) =>
    requestStatusResult<BpmnMetaDefinitionDto>('/definition/new', {
      method: 'POST',
      query: { name },
    }),

  /** `DELETE /definition/meta/{id}` — loescht Katalogeintrag, alle Versionen und deren XML. */
  deleteMeta: (definitionId: string) =>
    requestStatus(`/definition/meta/${encodeURIComponent(definitionId)}`, { method: 'DELETE' }),

  /** `POST /definition` — speichert BPMN-XML als neue Version (ohne Deploy). */
  save: (xml: string, previousGuid?: string) =>
    requestStatusResult<BpmnDefinitionDto>('/definition', {
      method: 'POST',
      rawBody: xml,
      contentType: 'application/xml',
      query: { previousGuid },
    }),

  /** `POST /definition/deploy` — speichert und aktiviert die Definition. */
  deploy: (xml: string, previousGuid?: string) =>
    requestStatusResult<BpmnDefinitionDto>('/definition/deploy', {
      method: 'POST',
      rawBody: xml,
      contentType: 'application/xml',
      query: { previousGuid },
    }),

  /** `POST /definition/meta/{id}/instance` — startet eine Instanz. */
  startInstance: async (definitionId: string) => {
    const instance = await requestStatusResult<ProcessInstanceInfoDto>(
      `/definition/meta/${encodeURIComponent(definitionId)}/instance`,
      { method: 'POST' },
    );
    return normalizeInstance(instance);
  },
};

// Alle Instanz-Endpunkte antworten in `ApiStatusResult<T>`.
export const instancesApi = {
  /** `GET /instance` */
  list: async (signal?: AbortSignal) => {
    const instances = await requestStatusResult<ProcessInstanceInfoDto[]>('/instance', { signal });
    return (instances ?? []).map(normalizeInstance);
  },

  /** `GET /instance/{id}` */
  get: async (instanceId: string, signal?: AbortSignal) => {
    const instance = await requestStatusResult<ProcessInstanceInfoDto>(`/instance/${instanceId}`, { signal });
    return normalizeInstance(instance);
  },

  /** `GET /instance/{id}/subscription/messages` */
  messageSubscriptions: (instanceId: string, signal?: AbortSignal) =>
    requestStatusResult<MessageSubscriptionDto[]>(`/instance/${instanceId}/subscription/messages`, { signal }),

  /** `GET /instance/{id}/subscription/signals` */
  signalSubscriptions: (instanceId: string, signal?: AbortSignal) =>
    requestStatusResult<SignalSubscriptionDto[]>(`/instance/${instanceId}/subscription/signals`, { signal }),

  /** `GET /instance/{id}/subscription/timers` */
  timerSubscriptions: (instanceId: string, signal?: AbortSignal) =>
    requestStatusResult<TimerSubscriptionDto[]>(`/instance/${instanceId}/subscription/timers`, { signal }),

  /** `GET /instance/{id}/subscription/services` */
  serviceSubscriptions: (instanceId: string, signal?: AbortSignal) =>
    requestStatusResult<TokenDto[]>(`/instance/${instanceId}/subscription/services`, { signal }),

  /** `GET /instance/{id}/subscription/userTasks` */
  userTaskSubscriptions: (instanceId: string, signal?: AbortSignal) =>
    requestStatusResult<TokenDto[]>(`/instance/${instanceId}/subscription/userTasks`, { signal }),
};

export const userTasksApi = {
  /** `GET /usertask` — offene Aufgaben des angemeldeten Benutzers. */
  list: (signal?: AbortSignal) =>
    requestStatusResult<ExtendedUserTaskSubscriptionDto[]>('/usertask', { signal }),

  /** `GET /usertask/{id}/form` — Formular zu einer Aufgabe (serverseitig aufgelöst). */
  getForm: (userTaskId: string, signal?: AbortSignal) =>
    requestStatusResult<FormDto>(`/usertask/${userTaskId}/form`, { signal }),

  /** `POST /usertask` — schließt eine Aufgabe mit Ergebnisdaten ab. */
  complete: (result: UserTaskResultDto) => requestStatus('/usertask', { method: 'POST', body: result }),
};

export const formsApi = {
  /** `GET /form/meta` — alle Formulare, optional nach Namen gefiltert. */
  listMeta: (search?: string, signal?: AbortSignal) =>
    requestStatusResult<FormMetaDataDto[]>('/form/meta', { query: { search }, signal }),

  /** `GET /form/meta/{formId}` */
  getMeta: (formId: string, signal?: AbortSignal) =>
    requestStatusResult<FormMetaDataDto>(`/form/meta/${formId}`, { signal }),

  /** `POST /form/meta/{formId}` — legt Metadaten an oder aktualisiert sie. */
  saveMeta: (formId: string, name: string) =>
    requestStatus(`/form/meta/${formId}`, { method: 'POST', body: { formId, name } }),

  /** `DELETE /form/meta/{formId}` — loescht das Formular samt allen Versionen. */
  deleteMeta: (formId: string) =>
    requestStatus(`/form/meta/${encodeURIComponent(formId)}`, { method: 'DELETE' }),

  /** `GET /form/{formId}/latest` — neueste Version des Formulars. */
  getLatest: (formId: string, signal?: AbortSignal) =>
    requestStatusResult<FormDto>(`/form/${formId}/latest`, { signal }),

  /** `GET /form/{formId}/{major}.{minor}` — konkrete Version. */
  getVersion: (formId: string, version: VersionDto, signal?: AbortSignal) =>
    requestStatusResult<FormDto>(`/form/${formId}/${version.major}.${version.minor}`, { signal }),

  /** `POST /form` — speichert eine neue Formularversion. */
  save: (form: { formId: string; formData: string; version?: VersionDto }) =>
    requestStatusResult<FormDto>('/form', {
      method: 'POST',
      body: { formId: form.formId, formData: form.formData, version: form.version ?? { major: 0, minor: 1 } },
    }),

  /** `POST /form/result` — reicht Formulardaten für einen User-Task ein. */
  submitResult: (result: UserTaskResultDto) =>
    requestStatus('/form/result', { method: 'POST', body: result }),
};

export const messagesApi = {
  /** `POST /message` — korreliert eine Nachricht in laufende Instanzen. */
  publish: (message: MessageDto) => requestStatusResult<string>('/message', { method: 'POST', body: message }),
};

export const operationsApi = {
  /** `GET /operations/diagnostics` */
  diagnostics: (signal?: AbortSignal) =>
    requestStatusResult<OperationsDiagnosticsDto>('/operations/diagnostics', { signal }),

  /** `GET /timer` — alle offenen Timer der Engine. */
  timers: (signal?: AbortSignal) => requestStatusResult<TimerSubscriptionDto[]>('/timer', { signal }),

  /** `GET /health` */
  health: (signal?: AbortSignal) => requestStatusResult<HealthStatusDto>('/health', { signal }),

  /** `GET /health/ready` */
  readiness: (signal?: AbortSignal) => requestStatusResult<HealthStatusDto>('/health/ready', { signal }),
};

export type { ProcessVariables };
