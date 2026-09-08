import type { components } from './schema.generated.js';

/** Direkt aus dem versionierten OpenAPI-Snapshot abgeleitete öffentliche DTOs. */
export type ExtendedUserTask = components['schemas']['ExtendedUserTaskSubscriptionDto'];
export type FlowzerForm = components['schemas']['FormDto'];
export type UserTaskDraft = components['schemas']['UserTaskDraftDto'];
export type UserTaskWorkState = components['schemas']['UserTaskWorkStateDto'];
export type ProcessInstance = components['schemas']['ProcessInstanceInfoDto'];
export type SubjectRef = components['schemas']['SubjectRefDto'];
export type DirectorySubjectSearchResult = components['schemas']['DirectorySubjectSearchResultDto'];

export type ProcessVariables = Record<string, unknown>;

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
