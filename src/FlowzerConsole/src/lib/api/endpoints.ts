import { request, requestOptionalStatusResult, requestStatus, requestStatusResult } from './client';
import { normalizeInstance } from './normalize';
import type {
  BpmnDefinitionDto,
  BpmnCapabilityContract,
  BpmnMetaDefinitionDto,
  ExtendedBpmnMetaDefinitionDto,
  FormDto,
  FormAuthoringDraftDto,
  FormAuthoringPreviewDto,
  FormCompatibilityItemDto,
  SaveFormAuthoringDraftRequestDto,
  FormMetaDataDto,
  HealthStatusDto,
  MessageDto,
  MessageSubscriptionDto,
  NotificationDto,
  OperationsDiagnosticsDto,
  ProcessInstanceInfoDto,
  ProcessVariables,
  SignalSubscriptionDto,
  TimerSubscriptionDto,
  TokenDto,
  VersionDto,
  WorkflowFolderDto,
  WorkflowFolderRequestDto,
  FolderAssignmentDto,
  DirectorySubjectSearchResultDto,
  DirectorySubjectResolutionResultDto,
  FormDirectorySearchContext,
  SubjectRefDto,
  FormSectionMetadataDto,
  FormSectionVersionSummaryDto,
  FormSectionVersionDto,
  FormSectionAuthoringDraftDto,
  SaveFormSectionAuthoringDraftRequestDto,
} from './types';

/** Alle Aufrufe gegen die Flowzer-API, gruppiert nach Controller. */

export const definitionsApi = {
  /** `GET /definition/capabilities` — versionierter, hostneutraler BPMN-Vertrag. */
  capabilities: (signal?: AbortSignal) =>
    requestStatusResult<BpmnCapabilityContract>('/definition/capabilities', { signal }),

  /** `POST /definition/validate` — prüft XML vor einer schreibenden Mutation. */
  validate: (xml: string) =>
    requestStatusResult<BpmnCapabilityContract>('/definition/validate', {
      method: 'POST',
      rawBody: xml,
      contentType: 'application/xml',
    }),

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
   * `folderId` ohne Wert legt auf oberster Ebene an.
   */
  create: (name: string, folderId?: string | null) =>
    requestStatusResult<BpmnMetaDefinitionDto>('/definition/new', {
      method: 'POST',
      query: { name, folderId: folderId ?? undefined },
    }),

  /**
   * `PUT /definition/meta/{id}/folder` — verschiebt einen Workflow.
   * Ohne `folderId` landet er auf der obersten Ebene.
   */
  moveToFolder: (definitionId: string, folderId: string | null) =>
    requestStatusResult<BpmnMetaDefinitionDto>(
      `/definition/meta/${encodeURIComponent(definitionId)}/folder`,
      { method: 'PUT', query: { folderId: folderId ?? undefined } },
    ),

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

  /**
   * `GET /definition/meta/{id}/start-form` — das Formular, das ausfuellt, wer den Workflow
   * startet. `null` heisst: Der Workflow startet ohne Eingabe (die API antwortet mit 204).
   */
  getStartForm: (definitionId: string, signal?: AbortSignal) =>
    requestOptionalStatusResult<FormDto>(
      `/definition/meta/${encodeURIComponent(definitionId)}/start-form`,
      { signal },
    ),

  /**
   * `POST /definition/meta/{id}/instance` — startet eine Instanz.
   *
   * Ohne `variables` geht der Aufruf wie bisher ohne Rumpf hinaus; ein Workflow mit
   * Startformular braucht sie, ein leeres Objekt eingeschlossen.
   */
  startInstance: async (definitionId: string, variables?: ProcessVariables) => {
    const instance = await requestStatusResult<ProcessInstanceInfoDto>(
      `/definition/meta/${encodeURIComponent(definitionId)}/instance`,
      variables === undefined ? { method: 'POST' } : { method: 'POST', body: { variables } },
    );
    return normalizeInstance(instance);
  },
};

/** Workflowgebundene Suche nach aktiven, stabil referenzierten Identitäten. */
export const identityDirectoryApi = {
  searchSubjects: (
    definitionId: string,
    query: string,
    kind: 'user' | 'group',
    signal?: AbortSignal,
  ) =>
    requestStatusResult<DirectorySubjectSearchResultDto>(
      `/identity-directory/workflows/${encodeURIComponent(definitionId)}/subjects`,
      { query: { query, kind, limit: 20 }, signal },
    ),

  /** Löst nur die genannten stabilen IDs im bearbeitbaren Workflowkontext auf. */
  resolveSubjects: (
    definitionId: string,
    subjects: readonly SubjectRefDto[],
    signal?: AbortSignal,
  ) => requestStatusResult<DirectorySubjectResolutionResultDto>(
    `/identity-directory/workflows/${encodeURIComponent(definitionId)}/subjects/resolve`,
    { method: 'POST', body: { subjects }, signal },
  ),

  /** Sucht aktive Identitäten, die am konkreten Workflow-Ordner delegiert werden dürfen. */
  searchFolderSubjects: (
    folderId: string,
    query: string,
    kind: 'all' | 'user' | 'group' = 'all',
    signal?: AbortSignal,
  ) =>
    requestStatusResult<DirectorySubjectSearchResultDto>(
      `/identity-directory/folders/${encodeURIComponent(folderId)}/subjects`,
      { query: { query, kind, limit: 20 }, signal },
    ),

  /** Historische Anzeigeauflösung im delegierbaren Ordnerkontext. */
  resolveFolderSubjects: (
    folderId: string,
    subjects: readonly SubjectRefDto[],
    signal?: AbortSignal,
  ) => requestStatusResult<DirectorySubjectResolutionResultDto>(
    `/identity-directory/folders/${encodeURIComponent(folderId)}/subjects/resolve`,
    { method: 'POST', body: { subjects }, signal },
  ),

  /** Sucht nur im gebundenen Start- oder Aufgabenformular, nie im globalen Verzeichnis. */
  searchFormSubjects: (
    context: FormDirectorySearchContext,
    fieldKey: string,
    query: string,
    kind: 'all' | 'user' | 'group' = 'all',
    signal?: AbortSignal,
  ) => {
    const path = context.kind === 'startForm'
      ? `/identity-directory/start-forms/${encodeURIComponent(context.definitionId)}`
      : `/identity-directory/user-tasks/${encodeURIComponent(context.taskId)}`;
    return requestStatusResult<DirectorySubjectSearchResultDto>(
      `${path}/fields/${encodeURIComponent(fieldKey)}/subjects`,
      { query: { query, kind, limit: 20 }, signal },
    );
  },

  /** Löst historische Werte nur gegen das serverseitig gebundene Formularfeld auf. */
  resolveFormSubjects: (
    context: FormDirectorySearchContext,
    fieldKey: string,
    subjects: readonly SubjectRefDto[],
    signal?: AbortSignal,
  ) => {
    const path = context.kind === 'startForm'
      ? `/identity-directory/start-forms/${encodeURIComponent(context.definitionId)}`
      : `/identity-directory/user-tasks/${encodeURIComponent(context.taskId)}`;
    return requestStatusResult<DirectorySubjectResolutionResultDto>(
      `${path}/fields/${encodeURIComponent(fieldKey)}/subjects/resolve`,
      { method: 'POST', body: { subjects }, signal },
    );
  },
};

/** Ordner des Workflow-Katalogs und die Zuständigkeiten daran. */
export const foldersApi = {
  /** `GET /folder` — der ganze Baum als flache Liste, inklusive der eigenen Rechte. */
  list: async (signal?: AbortSignal) =>
    (await requestStatusResult<WorkflowFolderDto[]>('/folder', { signal })) ?? [],

  /** `POST /folder` */
  create: (folder: WorkflowFolderRequestDto) =>
    requestStatusResult<WorkflowFolderDto>('/folder', { method: 'POST', body: folder }),

  /** `PUT /folder/{id}` — benennt um und verschiebt. */
  update: (id: string, folder: WorkflowFolderRequestDto) =>
    requestStatusResult<WorkflowFolderDto>(`/folder/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: folder,
    }),

  /** `DELETE /folder/{id}` — nur für leere Ordner. */
  remove: (id: string) =>
    requestStatus(`/folder/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  /** `PUT /folder/{id}/assignments` — setzt die Zuweisungen vollständig neu. */
  updateAssignments: (id: string, assignments: FolderAssignmentDto[]) =>
    requestStatusResult<WorkflowFolderDto>(`/folder/${encodeURIComponent(id)}/assignments`, {
      method: 'PUT',
      body: { assignments },
    }),
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

export const formsApi = {
  /** Datensparsames Inventar veroeffentlichter Fassungen und Autorenentwuerfe. */
  compatibility: (needsMigration?: boolean, signal?: AbortSignal) =>
    requestStatusResult<FormCompatibilityItemDto[]>('/form/compatibility', {
      query: { needsMigration },
      signal,
    }),

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

  /** Autorenentwurf oder Basis der neuesten Veroeffentlichung. */
  getDraft: (formId: string, signal?: AbortSignal) =>
    requestStatusResult<FormAuthoringDraftDto>(`/form/${encodeURIComponent(formId)}/draft`, { signal }),

  /** Revisionierten Autorenentwurf speichern. */
  saveDraft: (formId: string, draft: SaveFormAuthoringDraftRequestDto) =>
    requestStatusResult<FormAuthoringDraftDto>(`/form/${encodeURIComponent(formId)}/draft`, {
      method: 'PUT',
      body: draft,
    }),

  /** Autorenentwurf bei passender Revision verwerfen. */
  deleteDraft: (formId: string, expectedRevision: number) =>
    request<void>(`/form/${encodeURIComponent(formId)}/draft`, {
      method: 'DELETE',
      query: { expectedRevision },
    }),

  /** Erwarteten Entwurf als naechste unveraenderliche Version veroeffentlichen. */
  publishDraft: (formId: string, expectedRevision: number) =>
    requestStatusResult<FormDto>(`/form/${encodeURIComponent(formId)}/publish`, {
      method: 'POST',
      body: { expectedRevision },
    }),

  /** Lokalen Autorenstand ohne Persistenz wie bei der Veroeffentlichung expandieren. */
  previewDraft: (formId: string, formData: string, signal?: AbortSignal) =>
    requestStatusResult<FormAuthoringPreviewDto>(`/form/${encodeURIComponent(formId)}/preview`, {
      method: 'POST',
      body: { formData },
      signal,
    }),

};

/** Hostneutrale Bibliothek versionierter, wiederverwendbarer Formularabschnitte. */
export const formSectionsApi = {
  list: (signal?: AbortSignal) =>
    requestStatusResult<FormSectionMetadataDto[]>('/form-section', { signal }),
  create: (name: string) =>
    requestStatusResult<FormSectionMetadataDto>('/form-section', { method: 'POST', body: { name } }),
  rename: (sectionId: string, name: string) =>
    requestStatusResult<FormSectionMetadataDto>(`/form-section/${encodeURIComponent(sectionId)}`, {
      method: 'PUT', body: { name },
    }),
  listVersions: (sectionId: string, signal?: AbortSignal) =>
    requestStatusResult<FormSectionVersionSummaryDto[]>(
      `/form-section/${encodeURIComponent(sectionId)}/versions`, { signal },
    ),
  getVersion: (sectionId: string, version: VersionDto, signal?: AbortSignal) =>
    requestStatusResult<FormSectionVersionDto>(
      `/form-section/${encodeURIComponent(sectionId)}/versions/${version.major}.${version.minor}`, { signal },
    ),
  getDraft: (sectionId: string, signal?: AbortSignal) =>
    requestStatusResult<FormSectionAuthoringDraftDto>(
      `/form-section/${encodeURIComponent(sectionId)}/draft`, { signal },
    ),
  saveDraft: (sectionId: string, draft: SaveFormSectionAuthoringDraftRequestDto) =>
    requestStatusResult<FormSectionAuthoringDraftDto>(
      `/form-section/${encodeURIComponent(sectionId)}/draft`, { method: 'PUT', body: draft },
    ),
  deleteDraft: (sectionId: string, expectedRevision: number) =>
    request<void>(`/form-section/${encodeURIComponent(sectionId)}/draft`, {
      method: 'DELETE', query: { expectedRevision },
    }),
  publishDraft: (sectionId: string, expectedRevision: number) =>
    requestStatusResult<FormSectionVersionDto>(
      `/form-section/${encodeURIComponent(sectionId)}/publish`, {
        method: 'POST', body: { expectedRevision },
      },
    ),
};

export const messagesApi = {
  /** `POST /message` — korreliert eine Nachricht in laufende Instanzen. */
  publish: (message: MessageDto) => requestStatusResult<string>('/message', { method: 'POST', body: message }),
};

export const notificationsApi = {
  /** `GET /notifications` — persistenter Feed der angemeldeten Person. */
  list: (signal?: AbortSignal) => requestStatusResult<NotificationDto[]>('/notifications', { signal }),

  /** `POST /notifications/{id}/read` — idempotentes Lesestatus-Update. */
  markRead: (id: string) =>
    requestStatus(`/notifications/${encodeURIComponent(id)}/read`, { method: 'POST' }),
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
