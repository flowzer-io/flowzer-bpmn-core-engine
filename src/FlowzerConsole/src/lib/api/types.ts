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
  /** Serverseitig verifizierter Abschlussakteur, unabhängig von Formulardaten. */
  completedByUserId?: string | null;
}

/** Entspricht `ProcessInstanceInfoDto`. */
export interface ProcessInstanceInfoDto {
  /** Ohne explizite Freigabe nur datensparsame Übersicht, keine Token-Diagnose. */
  canInspect?: boolean;
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

/** Versionierter, hostneutraler Vertrag für unterstützte BPMN-Elementarten. */
export interface BpmnCapabilityContract {
  contractVersion: string;
  elements: BpmnElementCapability[];
}

export interface BpmnElementCapability {
  elementType: string;
  modelable: boolean;
  parsable: boolean;
  executable: boolean;
}

/** Entspricht `BpmnMetaDefinitionDto`. */
export interface BpmnMetaDefinitionDto {
  definitionId: string;
  name: string;
  description?: string | null;
  /**
   * Ordner des Workflows; `null` heißt oberste Ebene. Beim Anlegen wählt der Wert den
   * Ordner, beim Ändern der Metadaten wird er ignoriert — verschoben wird über
   * `PUT /definition/meta/{id}/folder`.
   */
  folderId?: string | null;
}

/** Art einer Ordnerzuweisung. Entspricht den Zeichenketten aus `FolderMappingExtensions`. */
export const FOLDER_SUBJECT_KINDS = ['user', 'group'] as const;
export type FolderSubjectKind = (typeof FOLDER_SUBJECT_KINDS)[number];

/** Herkunft einer Ordnerzuweisung. Fehlende Werte bleiben aus Legacy-Antworten `text`. */
export type FolderReferenceMode = 'text' | 'directory';

/**
 * Die beiden Rollen eines Ordners. `editor` darf die Workflows darin ändern,
 * `steward` — die Fachverantwortung — zusätzlich Unterordner anlegen und delegieren.
 */
export const FOLDER_ROLES = ['editor', 'steward'] as const;
export type FolderRole = (typeof FOLDER_ROLES)[number];

/** Entspricht `FolderAssignmentDto`. */
export interface FolderAssignmentDto {
  subjectKind: FolderSubjectKind;
  subject: string;
  role: FolderRole;
  displayName?: string | null;
  /** Additiver Vertragswert: alte Antworten ohne Wert sind weiterhin Freitext. */
  referenceMode?: FolderReferenceMode;
  /** Bei `directory` die stabile, vom Verzeichnis bestätigte Identität. */
  subjectRef?: SubjectRefDto | null;
}

/** Entspricht `InheritedFolderAssignmentDto` — eine Zuweisung aus einem übergeordneten Ordner. */
export interface InheritedFolderAssignmentDto extends FolderAssignmentDto {
  inheritedFromId: string;
  inheritedFromName: string;
}

/** Entspricht `WorkflowFolderDto`. */
export interface WorkflowFolderDto {
  id: string;
  name: string;
  parentId?: string | null;
  description?: string | null;
  createdOn: string;
  assignments: FolderAssignmentDto[];
  inheritedAssignments: InheritedFolderAssignmentDto[];
  /** Darf die angemeldete Person hier Workflows anlegen und ändern? */
  mayEdit: boolean;
  /** Darf sie hier Unterordner anlegen und Zuständigkeiten pflegen? */
  mayDelegate: boolean;
  workflowCount: number;
}

/** Rumpf für `POST /folder` und `PUT /folder/{id}`. */
export interface WorkflowFolderRequestDto {
  name: string;
  parentId?: string | null;
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
  validationProfile?: string | null;
  id?: string | null;
  /**
   * Kennung im Formularbestand. Fehlt bei einem Formular, das im Workflow selbst liegt:
   * Es hat keinen Eintrag im Bestand und keine eigene Version.
   */
  formId?: string | null;
  /** Fehlt bei einem Formular aus dem Workflow — es ist mit dem Workflow versioniert. */
  version?: VersionDto | null;
  formData?: string | null;
}

/** Gemeinsamer Formularautoren-Entwurf oder die noch unveraenderte Veroeffentlichungsbasis. */
export interface FormAuthoringDraftDto {
  formId: string;
  revision: number;
  hasDraft: boolean;
  updatedAtUtc?: string | null;
  basedOnPublishedFormId?: string | null;
  basedOnVersion?: VersionDto | null;
  formData: string;
}

export interface SaveFormAuthoringDraftRequestDto {
  expectedRevision: number;
  formData: string;
}

/** Serverseitig expandierter, nicht persistierter Vorschau-Snapshot. */
export interface FormAuthoringPreviewDto {
  formData: string;
  validationProfile: string;
}

/** Hostneutraler Katalogeintrag eines wiederverwendbaren Formularabschnitts. */
export interface FormSectionMetadataDto {
  sectionId: string;
  name: string;
}

/** Datensparsame Auswahl einer unveränderlichen Abschnittsversion. */
export interface FormSectionVersionSummaryDto {
  id: string;
  sectionId: string;
  version: VersionDto;
}

/** Unveränderliche Abschnittsfassung inklusive Form.io-Schema. */
export interface FormSectionVersionDto extends FormSectionVersionSummaryDto {
  sectionData: string;
}

/** Revisionierter Abschnittsentwurf oder veröffentlichte Basis. */
export interface FormSectionAuthoringDraftDto {
  sectionId: string;
  revision: number;
  hasDraft: boolean;
  updatedAtUtc?: string | null;
  basedOnPublishedSectionId?: string | null;
  basedOnVersion?: VersionDto | null;
  sectionData: string;
}

export interface SaveFormSectionAuthoringDraftRequestDto {
  expectedRevision: number;
  sectionData: string;
}

export type FormCompatibilitySource = 'published' | 'draft';

/** Datensparsamer Inventareintrag; Schema und Scriptinhalt bleiben serverseitig. */
export interface FormCompatibilityItemDto {
  formId: string;
  formName: string;
  source: FormCompatibilitySource;
  publishedFormId?: string | null;
  version?: VersionDto | null;
  draftRevision?: number | null;
  compatible: boolean;
  validationProfile?: string | null;
  issueCode?: string | null;
}

/** Stabile Benutzer- oder Gruppenreferenz aus dem veröffentlichten Verzeichnis. */
export interface SubjectRefDto {
  kind: 'user' | 'group';
  id: string;
}

/** Aktive Verzeichnisidentität mit eindeutiger Anzeigeprojektion. */
export interface DirectorySubjectDto {
  subject: SubjectRefDto;
  displayName: string;
  detail: string;
  /** Status im aktuellen vollständig publizierten Verzeichnisstand. */
  isActive: boolean;
  /** Darf im gebundenen fachlichen Kontext erneut ausgewählt werden? */
  isSelectable: boolean;
}

/** Begrenzte Treffer aus genau einer atomar veröffentlichten Verzeichnisgeneration. */
export interface DirectorySubjectSearchResultDto {
  generationId: string;
  items: DirectorySubjectDto[];
}

/** Exakte Anzeigeauflösung bereits gespeicherter stabiler Referenzen. */
export interface DirectorySubjectResolutionResultDto {
  generationId: string;
  items: DirectorySubjectDto[];
}

/** Kontext, in dem ein Formularfeld Directory-Identitäten suchen darf. */
export type FormDirectorySearchContext =
  | { kind: 'startForm'; definitionId: string }
  | { kind: 'userTask'; taskId: string };

/** Persistente, benutzergebundene Meldung aus dem Server-Feed. */
export interface NotificationDto {
  id: string;
  userTaskId: string;
  kind: string;
  occurredAtUtc: string;
  readAtUtc: string | null;
  title: string;
  message: string;
  severity: 'info' | 'success' | 'warning' | 'error';
  href: string;
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

/** Stabile Providerfamilien des oeffentlichen KI-Verbindungsvertrags. */
export const AI_PROVIDER_KINDS = ['OpenAi', 'OpenAiCompatible', 'Anthropic'] as const;
export type AiProviderKind = (typeof AI_PROVIDER_KINDS)[number];

/** Explizite Datenflussgrenze; es gibt keinen stillen Wechsel zwischen lokal und Cloud. */
export const AI_PROCESSING_LOCATIONS = ['Cloud', 'Local'] as const;
export type AiProcessingLocation = (typeof AI_PROCESSING_LOCATIONS)[number];

export const AI_TOOL_SIDE_EFFECTS = ['ReadOnly', 'Write', 'Send'] as const;
export type AiToolSideEffect = (typeof AI_TOOL_SIDE_EFFECTS)[number];

export interface AiToolPermissionDto {
  toolId: string;
  toolVersion: number;
  allowPreApproval: boolean;
}

/** Nur lesbarer Vertrag einer fest auf dem Server registrierten Werkzeugversion. */
export interface AiToolDto {
  id: string;
  version: number;
  name: string;
  description: string;
  inputSchema: string;
  outputSchema: string;
  sideEffect: AiToolSideEffect;
  allowsPreApproval: boolean;
  contractHash: string;
}

/** Sichere Projektion ohne Secret-Wert und ohne Secret-Referenz. */
export interface AiConnectionDto {
  id: string;
  name: string;
  provider: AiProviderKind;
  location: AiProcessingLocation;
  baseAddress?: string | null;
  defaultModel: string;
  enabled: boolean;
  ready: boolean;
  revision: number;
  updatedAtUtc: string;
  allowedTools: AiToolPermissionDto[];
}

export interface CreateAiConnectionInput {
  name: string;
  provider: AiProviderKind;
  location: AiProcessingLocation;
  baseAddress?: string | null;
  defaultModel: string;
  secretReference: string;
  allowedTools?: AiToolPermissionDto[];
}

export interface UpdateAiConnectionInput extends Omit<CreateAiConnectionInput, 'secretReference'> {
  expectedRevision: number;
  /** Leer behaelt die vorhandene Referenz; sie wird nie aus einer Antwort vorbefuellt. */
  secretReference?: string;
}
