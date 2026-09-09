import type { components } from './schema.generated.js';

/** Direkt aus dem versionierten OpenAPI-Snapshot abgeleitete öffentliche DTOs. */
export type ExtendedUserTask = components['schemas']['ExtendedUserTaskSubscriptionDto'];
export type FlowzerForm = components['schemas']['FormDto'];
export type UserTaskDraft = components['schemas']['UserTaskDraftDto'];
export type UserTaskWorkState = components['schemas']['UserTaskWorkStateDto'];
export type ProcessInstance = components['schemas']['ProcessInstanceInfoDto'];
export type RuntimeDiagram = components['schemas']['RuntimeDiagramDto'];
export type SubjectRef = components['schemas']['SubjectRefDto'];
export type DirectorySubject = components['schemas']['DirectorySubjectDto'];
export type DirectorySubjectSearchResult = components['schemas']['DirectorySubjectSearchResultDto'];
export type DirectorySubjectResolutionResult = components['schemas']['DirectorySubjectResolutionResultDto'];
/** Sichere Verbindungsprojektion ohne Secret-Wert oder Secret-Referenz. */
export type AiConnection = components['schemas']['AiConnectionDto'];
export type AiProviderKind = components['schemas']['AiProviderKindDto'];
export type AiProcessingLocation = components['schemas']['AiProcessingLocationDto'];

/** Eingabevertrag zum Anlegen einer KI-Verbindung; Secret-Referenzen sind nur schreibbar. */
export interface CreateAiConnectionCommand {
  name: string;
  provider: AiProviderKind;
  location: AiProcessingLocation;
  baseAddress?: string | null;
  defaultModel: string;
  secretReference: string;
}

/** Revisionsgebundener Eingabevertrag; eine fehlende Secret-Referenz behält die bisherige bei. */
export interface UpdateAiConnectionCommand {
  expectedRevision: number;
  name: string;
  provider: AiProviderKind;
  location: AiProcessingLocation;
  baseAddress?: string | null;
  defaultModel: string;
  secretReference?: string | null;
}

export interface SetAiConnectionEnabledCommand {
  expectedRevision: number;
  enabled: boolean;
}

export type ProcessVariables = Record<string, unknown>;

/** Hostneutraler Katalogeintrag eines wiederverwendbaren Formularabschnitts. */
export interface FormSectionMetadata {
  sectionId: string;
  name: string;
}

/** Eine konkrete Abschnittsversion. Das SDK kennt bewusst keinen "latest"-Wert. */
export interface FormSectionVersionNumber {
  major: number;
  minor: number;
}

/** Datensparsame Auswahl einer unveränderlichen Abschnittsfassung. */
export interface FormSectionVersionSummary {
  id: string;
  sectionId: string;
  version: FormSectionVersionNumber;
}

/** Vollständige, unveränderliche Abschnittsfassung für autorisierte Modellierung. */
export interface FormSectionVersion extends FormSectionVersionSummary {
  sectionData: string;
}

/** Revisionsgebundener Autorenentwurf eines Abschnitts. */
export interface FormSectionAuthoringDraft {
  sectionId: string;
  revision: number;
  hasDraft: boolean;
  updatedAtUtc?: string | null;
  basedOnPublishedSectionId?: string | null;
  basedOnVersion?: FormSectionVersionNumber | null;
  sectionData: string;
}

export interface CreateFormSectionCommand {
  name: string;
}

export interface RenameFormSectionCommand {
  name: string;
}

/** Compare-and-swap-Eingabe für einen Abschnittsentwurf. */
export interface SaveFormSectionAuthoringDraftCommand {
  expectedRevision: number;
  sectionData: string;
}

/** Ein datensparsamer, serverseitig autorisierter Eintrag im Prozessverlauf. */
export type ProcessHistoryAction = 'claim' | 'release' | 'assign' | 'delegate' | 'complete';

export interface ProcessHistoryEntry {
  id: string;
  userTaskId: string;
  flowNodeId: string;
  action: ProcessHistoryAction;
  revision: number;
  occurredAtUtc: string;
}

/** Öffentliche History-Projektion einer Prozessinstanz. */
export interface ProcessHistory {
  instanceId: string;
  events: ProcessHistoryEntry[];
}

/** Eingabe für den idempotenten Abschluss einer offenen Human Task. */
export interface CompleteUserTaskCommand {
  flowNodeId: string;
  tokenId: string;
  processInstanceId?: string | null;
  /** Verhindert den Abschluss eines zwischenzeitlich neu zugewiesenen Tasks. */
  expectedTaskRevision: number;
  actionId?: string | null;
  data?: ProcessVariables | null;
}

export interface SaveUserTaskDraftCommand {
  expectedRevision: number;
  expectedTaskRevision?: number;
  data: ProcessVariables;
}

export interface UserTaskRevisionCommand {
  expectedRevision: number;
}

export interface ReleaseUserTaskCommand extends UserTaskRevisionCommand {
  reason: string;
}

export interface TransferUserTaskCommand extends ReleaseUserTaskCommand {
  assignee: SubjectRef;
}

/** Maschinenlesbarer RFC-7807-Vertrag einschließlich Flowzer-Erweiterungen. */
export interface FlowzerProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  successful?: boolean;
  errorMessage?: string | null;
  traceId?: string;
  code?: string;
  expectedRevision?: number;
  currentRevision?: number;
  errors?: Record<string, string[]> | null;
  [key: string]: unknown;
}

export interface FlowzerCallOptions {
  signal?: AbortSignal | undefined;
}

export interface DirectorySubjectSearchOptions extends FlowzerCallOptions {
  query: string;
  kind?: 'all' | 'user' | 'group' | undefined;
  limit?: number | undefined;
}

export interface TaskAssigneeSearchOptions extends FlowzerCallOptions {
  action: 'assign' | 'delegate';
  query: string;
  limit?: number | undefined;
}

/** Begrenzte historische Anzeigeauflösung im Kontext einer Lifecycle-Aktion. */
export interface TaskAssigneeResolutionOptions extends FlowzerCallOptions {
  action: 'assign' | 'delegate';
  subjects: readonly SubjectRef[];
}

export interface FlowzerCompletionOptions extends FlowzerCallOptions {
  /** Über Wiederholungen stabiler Schlüssel; für neue SDK-Clients bewusst verpflichtend. */
  idempotencyKey: string;
}

export interface FlowzerCsrfToken {
  headerName: string;
  requestToken: string;
}

export type FlowzerAuth =
  | {
    kind: 'bearer';
    /** Muss bei jedem Request einen aktuellen, auf Flowzer begrenzten Token liefern. */
    getAccessToken: () => string | Promise<string>;
  }
  | {
    kind: 'cookie';
    /** Der Host kontrolliert CSRF-Abruf und -Caching; das SDK speichert keine Sitzung. */
    getCsrfToken: () => FlowzerCsrfToken | Promise<FlowzerCsrfToken>;
  };

export interface FlowzerClientOptions {
  baseUrl: string;
  auth?: FlowzerAuth | undefined;
  fetch?: typeof globalThis.fetch | undefined;
  onUnauthorized?: (() => void) | undefined;
}
