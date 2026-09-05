/**
 * TypeScript-Spiegel der DTOs aus `src/WebApiEngine.Shared`.
 *
 * Die Typen werden bewusst von Hand gepflegt, damit der Client auch ohne
 * laufende API typsicher ist. `npm run api:types` erzeugt zusätzlich ein
 * generiertes Schema aus dem Swagger-Dokument, gegen das sich Abweichungen
 * prüfen lassen.
 */

/** Entspricht `ApiStatusResult` bzw. `ApiStatusResult<T>`. */
export interface ApiStatusResult<T = never> {
  successful: boolean;
  errorMessage?: string | null;
  result?: T | null;
}

/** Entspricht `VersionDto`. */
export interface VersionDto {
  major: number;
  minor: number;
}

/** Entspricht `FlowNodeStateDto` — die Reihenfolge muss zur C#-Enum passen. */
export const FLOW_NODE_STATES = [
  'Ready',
  'Active',
  'Completing',
  'WaitingForLoopEnd',
  'Completed',
  'Failing',
  'Terminating',
  'Failed',
  'Terminated',
  'Withdrawn',
  'Compensating',
  'Compensated',
  'Merged',
] as const;
export type FlowNodeState = (typeof FLOW_NODE_STATES)[number];

/** Entspricht `ProcessInstanceStateDto` — die Reihenfolge muss zur C#-Enum passen. */
export const PROCESS_INSTANCE_STATES = [
  'Initialized',
  'Running',
  'Waiting',
  'Completing',
  'Completed',
  'Failing',
  'Failed',
  'Terminating',
  'Terminated',
  'Compensating',
  'Compensated',
] as const;
export type ProcessInstanceState = (typeof PROCESS_INSTANCE_STATES)[number];

/**
 * Das serialisierte BPMN-Flow-Element eines Tokens. Die Engine liefert hier
 * ein ExpandoObject, dessen Felder je Elementtyp variieren — deshalb bleibt
 * der Typ offen und wird über die Helfer in `flowElement.ts` gelesen.
 */
export interface FlowElement {
  Id?: string;
  Name?: string;
  /** Bei UserTask/ServiceTask: der Form-Key bzw. der Job-Typ. */
  Implementation?: string;
  FlowzerAssignee?: string;
  FlowzerCandidateGroups?: string;
  FlowzerCandidateUsers?: string;
  FlowzerDueDate?: string;
  FlowzerFollowUpDate?: string;
  FlowzerPriority?: string;
  [key: string]: unknown;
}

export type ProcessVariables = Record<string, unknown>;

/** Entspricht `TokenDto`. */
export interface TokenDto {
  id: string;
  state: FlowNodeState;
  currentFlowNodeId?: string | null;
  currentFlowElement?: FlowElement | null;
  variables?: ProcessVariables | null;
  outputData?: ProcessVariables | null;
  previousTokenId?: string | null;
  parentTokenId?: string | null;
  /** Ergänzt durch die Console-API: Startzeitpunkt des Tokens (UTC). */
  startTime?: string | null;
  /** Ergänzt durch die Console-API: letzter Statuswechsel (UTC). */
  lastStateChangeTime?: string | null;
}

/** Entspricht `ProcessInstanceInfoDto`. */
export interface ProcessInstanceInfoDto {
  instanceId: string;
  definitionId: string;
  relatedDefinitionId: string;
  relatedDefinitionName: string;
  messageSubscriptionCount: number;
  signalSubscriptionCount: number;
  userTaskSubscriptionCount: number;
  serviceSubscriptionCount: number;
  state: ProcessInstanceState;
  tokens: TokenDto[];
  /** Ergänzt durch die Console-API: Startzeitpunkt der Instanz (UTC). */
  startedAt?: string | null;
  /** Ergänzt durch die Console-API: Endzeitpunkt der Instanz (UTC). */
  finishedAt?: string | null;
}

/** Entspricht `BpmnDefinitionDto`. */
export interface BpmnDefinitionDto {
  id: string;
  definitionId: string;
  previousGuid?: string | null;
  hash: string;
  savedByUser: string;
  savedOn: string;
  deployedByUser?: string | null;
  deployedOn?: string | null;
  version: VersionDto;
}

/** Entspricht `BpmnMetaDefinitionDto`. */
export interface BpmnMetaDefinitionDto {
  definitionId: string;
  name: string;
  description?: string | null;
}

/** Entspricht `ExtendedBpmnMetaDefinitionDto`. */
export interface ExtendedBpmnMetaDefinitionDto extends BpmnMetaDefinitionDto {
  latestVersion?: VersionDto | null;
  latestVersionDateTime: string;
  deployedId?: string | null;
  deployedVersion?: VersionDto | null;
  deployedVersionDateTime: string;
}

/** Entspricht `FormMetaDataDto`. */
export interface FormMetaDataDto {
  formId: string;
  name: string;
}

/** Entspricht `FormDto`. `formData` enthält das Form.io-Schema als JSON-String. */
export interface FormDto {
  id?: string | null;
  formId: string;
  version: VersionDto;
  formData?: string | null;
}

/** Entspricht `UserTaskSubscriptionDto`. */
export interface UserTaskSubscriptionDto {
  id: string;
  name: string;
  token: TokenDto;
  userCandidates: string[];
  userGroups: string[];
  currenAssignedUser?: string | null;
  processInstanceId?: string | null;
  definitionId: string;
  processId: string;
}

/** Entspricht `ExtendedUserTaskSubscriptionDto`. */
export interface ExtendedUserTaskSubscriptionDto extends UserTaskSubscriptionDto {
  definitionMetaName: string;
  definitionVersion: VersionDto;
  /** Ergänzt durch die Console-API: aufgelöster Form-Key des User-Tasks. */
  formKey?: string | null;
  /** Ergänzt durch die Console-API: Fälligkeitsangabe aus dem BPMN-Modell. */
  dueDate?: string | null;
  followUpDate?: string | null;
  priority?: string | null;
}

/** Entspricht `TimerSubscriptionDto`. */
export interface TimerSubscriptionDto {
  id: string;
  dueAt: string;
  flowNodeId: string;
  processId: string;
  relatedDefinitionId: string;
  definitionId: string;
  processInstanceId?: string | null;
  tokenId?: string | null;
  remainingOccurrences?: number | null;
  kind: string;
}

/** Entspricht `MessageDefinitionDto`. */
export interface MessageDefinitionDto {
  name: string;
  flowzerId?: string | null;
  flowzerCorrelationKey?: string | null;
}

/** Entspricht `MessageSubscriptionDto`. */
export interface MessageSubscriptionDto {
  message: MessageDefinitionDto;
  processId: string;
  relatedDefinitionId: string;
  definitionId: string;
  processInstanceId?: string | null;
}

/** Entspricht `SignalSubscriptionDto`. */
export interface SignalSubscriptionDto {
  signal: string;
  processId: string;
  relatedDefinitionId: string;
  definitionId: string;
  processInstanceId?: string | null;
}

/** Entspricht `MessageDto`. */
export interface MessageDto {
  name: string;
  correlationKey?: string | null;
  variables?: ProcessVariables | null;
  timeToLive?: number;
  instanceId?: string | null;
}

/** Entspricht `UserTaskResultDto`. */
export interface UserTaskResultDto {
  flowNodeId: string;
  tokenId: string;
  processInstanceId?: string | null;
  data?: ProcessVariables | null;
}

/** Entspricht `HealthStatusDto`. */
export interface HealthStatusDto {
  status: string;
  checkedAtUtc: string;
  environment: string;
  storage: string;
}

/** Entspricht `OperationsStorageSnapshotDto`. */
export interface OperationsStorageSnapshotDto {
  storageRootHint: string;
  totalDefinitions: number;
  activeDefinitions: number;
  definitionMetadataEntries: number;
  formMetadataEntries: number;
  totalInstances: number;
  activeInstances: number;
  completedInstances: number;
  failedInstances: number;
  pendingMessages: number;
  pendingTimers: number;
  openUserTasks: number;
  pendingSignals: number;
  pendingServices: number;
}

/** Entspricht `TimerSchedulerDiagnosticsDto`. */
export interface TimerSchedulerDiagnosticsDto {
  enabled: boolean;
  pollIntervalSeconds: number;
  status: string;
  serviceStartedAtUtc?: string | null;
  lastTickStartedAtUtc?: string | null;
  lastTickCompletedAtUtc?: string | null;
  lastSuccessfulTickAtUtc?: string | null;
  lastFailedTickAtUtc?: string | null;
  lastTickDurationMs?: number | null;
  lastProcessedTimers: number;
  successfulTickCount: number;
  failedTickCount: number;
  totalProcessedTimers: number;
  lastErrorMessage?: string | null;
}

/** Entspricht `OperationsInstrumentationDto`. */
export interface OperationsInstrumentationDto {
  meterName: string;
  activitySourceName: string;
  notes: string;
}

/** Entspricht `OperationsObservabilityDto`. */
export interface OperationsObservabilityDto {
  enabled: boolean;
  consoleExporterEnabled: boolean;
  otlpExporterEnabled: boolean;
  otlpEndpointHint?: string | null;
  otlpProtocol?: string | null;
  otlpHeadersHint?: string | null;
  serviceName: string;
  serviceVersion: string;
}

/** Entspricht `OperationsDiagnosticsDto`. */
export interface OperationsDiagnosticsDto {
  checkedAtUtc: string;
  environment: string;
  storage: OperationsStorageSnapshotDto;
  timerScheduler: TimerSchedulerDiagnosticsDto;
  instrumentation: OperationsInstrumentationDto;
  observability: OperationsObservabilityDto;
}
