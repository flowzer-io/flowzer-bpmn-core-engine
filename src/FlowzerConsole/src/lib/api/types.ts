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
  /** Version des Workflows, an die die Instanz gebunden ist; null, wenn die Definition fehlt. */
  definitionVersion?: VersionDto | null;
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
  /** Elterninstanz, wenn diese Instanz von einer Call Activity gestartet wurde. */
  parentInstanceId?: string | null;
  /** Das an der Call Activity wartende Token der Elterninstanz. */
  parentTokenId?: string | null;
}

/**
 * Entspricht `CalledInstanceDto` — eine von einer Call Activity gestartete Kindinstanz.
 *
 * Antwort von `GET /instance/{instanceId}/children`. Die Rechteprüfung ist dieselbe wie bei
 * der Instanzansicht; ohne das Recht antwortet die API mit 404.
 */
export interface CalledInstanceDto {
  instanceId: string;
  relatedDefinitionId: string;
  relatedDefinitionName: string;
  definitionVersion?: VersionDto | null;
  state: ProcessInstanceState;
  /** Knoten-Id der Call Activity im Elternprozess; fehlt bei historischen Ständen. */
  callActivityFlowNodeId?: string | null;
}

/**
 * Entspricht `InstanceMigrationFindingDto`.
 *
 * `code` ist bewusst offen typisiert: Die Engine darf Gründe ergänzen, ohne dass die
 * Konsole bricht. Unbekannte Codes zeigt die Oberfläche als `message` an.
 */
export interface InstanceMigrationFindingDto {
  code: string;
  flowNodeId?: string | null;
  /** Technische Begründung der API auf Englisch — nur der Rückfall für neue Codes. */
  message: string;
}

/** Entspricht `InstanceMigrationPreviewItemDto`. */
export interface InstanceMigrationPreviewItemDto {
  instanceId: string;
  migratable: boolean;
  /** Nicht leer genau dann, wenn `migratable` falsch ist. */
  problems: InstanceMigrationFindingDto[];
  /** Folgen, die der Betrieb vor der Migration kennen muss, z. B. ein verworfener Entwurf. */
  notices: InstanceMigrationFindingDto[];
}

/**
 * Entspricht `MigrationFlowNodeDto` — ein Knoten als Quelle oder Ziel einer Zuordnung.
 *
 * `type` ist die BPMN-Elementart als schlichter Name, z. B. `UserTask` oder
 * `ExclusiveGateway`. Quelle und Ziel müssen dieselbe tragen.
 */
export interface MigrationFlowNodeDto {
  id: string;
  /** Fehlt ganz, wenn der Knoten im Modell unbenannt ist — die API laesst leere Felder weg. */
  name?: string | null;
  type: string;
}

/** Entspricht dem `mapping` der Vorschau: was von Hand zuzuordnen ist und wohin. */
export interface InstanceMigrationMappingDto {
  /** Wartende Quellknoten, die es in der Zielversion nicht gibt und die noch kein Ziel haben. */
  required: MigrationFlowNodeDto[];
  /** Die Knoten der Zielversion, aus denen gewählt werden kann. */
  targets: MigrationFlowNodeDto[];
}

/** Entspricht `InstanceMigrationPreviewDto` — die folgenlose Prüfung vor der Migration. */
export interface InstanceMigrationPreviewDto {
  relatedDefinitionId: string;
  relatedDefinitionName: string;
  /** Versions-Guid, an die die geprüften Instanzen gebunden sind. */
  sourceDefinitionId: string;
  sourceVersion: VersionDto | null;
  /** Versions-Guid der aktuell deployten Fassung; einziges zulässiges Ziel. */
  targetDefinitionId: string;
  targetVersion: VersionDto;
  /** Grundlage der Zuordnung von Hand; die Zuordnung selbst gehört zur Anfrage. */
  mapping: InstanceMigrationMappingDto;
  instances: InstanceMigrationPreviewItemDto[];
}

/** Entspricht `InstanceMigrationResultItemDto`. */
export interface InstanceMigrationResultItemDto {
  instanceId: string;
  migrated: boolean;
  problems: InstanceMigrationFindingDto[];
}

/** Entspricht `InstanceMigrationResultDto`; Teilerfolge sind möglich. */
export interface InstanceMigrationResultDto {
  targetDefinitionId: string;
  targetVersion: VersionDto;
  instances: InstanceMigrationResultItemDto[];
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
  /**
   * Aufbewahrungsfrist beendeter Instanzen dieses Workflows in Tagen. `null` übernimmt den
   * installationsweiten Wert, `0` heißt ausdrücklich „nie löschen". Wird beim Anlegen und
   * beim Ändern der Metadaten ausgewertet.
   */
  retentionDays?: number | null;
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
  folderId?: string | null;
}

/** Hierarchischer Ordner der gemeinsamen Formularbibliothek. */
export interface FormFolderDto {
  id: string;
  parentId?: string | null;
  name: string;
}

export interface FormFolderRequestDto {
  parentId?: string | null;
  name: string;
}

/** Datensparsame Auswahl einer unveränderlichen Formularversion. */
export interface FormVersionSummaryDto {
  id: string;
  formId: string;
  version: VersionDto;
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
  email?: string | null;
  username?: string | null;
  firstName?: string | null;
  lastName?: string | null;
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
  /** Abbrüche zählen getrennt von den Fehlern; sie sind ein regulärer Ausgang. */
  cancelledInstances: number;
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

/**
 * Entspricht `InstanceRetentionDiagnosticsDto`. Enthält bewusst keine Instanzkennungen:
 * Der Betrieb sieht, dass und wie viel gelöscht wurde, nicht wessen Vorgang.
 */
export interface InstanceRetentionDiagnosticsDto {
  enabled: boolean;
  /** Installationsweite Frist in Tagen; `null`, solange keine gesetzt ist. */
  days?: number | null;
  pollIntervalMinutes: number;
  batchSize: number;
  status: string;
  serviceStartedAtUtc?: string | null;
  lastRunStartedAtUtc?: string | null;
  lastRunCompletedAtUtc?: string | null;
  lastSuccessfulRunAtUtc?: string | null;
  lastFailedRunAtUtc?: string | null;
  lastRunDurationMs?: number | null;
  lastDeletedInstances: number;
  successfulRunCount: number;
  failedRunCount: number;
  totalDeletedInstances: number;
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
  /** Der Scrape-Endpunkt ist anonym und darf nur im Containernetz erreichbar sein. */
  prometheusEnabled: boolean;
  prometheusPath?: string | null;
  serviceName: string;
  serviceVersion: string;
}

/** Entspricht `OperationsConnectorDto`. */
export interface OperationsConnectorDto {
  name: string;
  jobType: string;
  enabled: boolean;
  lastRunAtUtc?: string | null;
  processedJobs: number;
  failedJobs: number;
  /** Die Meldung des Konnektors, niemals ein aufgelöstes Secret. */
  lastErrorMessage?: string | null;
}

/** Entspricht `OperationsDiagnosticsDto`. */
export interface OperationsDiagnosticsDto {
  checkedAtUtc: string;
  environment: string;
  storage: OperationsStorageSnapshotDto;
  timerScheduler: TimerSchedulerDiagnosticsDto;
  retention: InstanceRetentionDiagnosticsDto;
  instrumentation: OperationsInstrumentationDto;
  observability: OperationsObservabilityDto;
  /**
   * Auch abgeschaltete Konnektoren stehen hier: „nicht aktiviert“ ist eine Aussage fürs
   * Betriebsbild, ein gar nicht aufgeführter Konnektor wäre keine.
   */
  connectors: OperationsConnectorDto[];
}

/** Zeitraumgrenzen einer Auswertung; beide Angaben sind freiwillig (Server-Standard: 30 Tage). */
export interface AnalyticsRangeQuery {
  from?: string;
  to?: string;
}

/** Streuungsmaße einer gemessenen Dauer — alle Werte in Sekunden. */
export interface DurationStatisticsDto {
  sampleCount: number;
  medianSeconds: number;
  p90Seconds: number;
  meanSeconds: number;
  maxSeconds: number;
}

/** Ein Workflow des Zeitraums, aufgeschlüsselt nach Ausgang der Instanzen. */
export interface WorkflowAnalyticsSummaryDto {
  metaDefinitionId: string;
  name: string;
  totalCount: number;
  runningCount: number;
  completedCount: number;
  cancelledCount: number;
  failedCount: number;
  /** null, solange keine Instanz des Zeitraums abgeschlossen ist. */
  cycleTime: DurationStatisticsDto | null;
}

/** Antwort von `GET /operations/analytics/workflows`. */
export interface WorkflowAnalyticsOverviewDto {
  fromUtc: string;
  toUtc: string;
  workflows: WorkflowAnalyticsSummaryDto[];
}

/** Ein Schritt der Definition mit seiner Wartezeit und den aktuell wartenden Token. */
export interface FlowNodeAnalyticsDto {
  flowNodeId: string;
  name: string | null;
  executionCount: number;
  waitingTokenCount: number;
  /** null, wenn im Zeitraum kein Durchlauf vollständig beobachtet wurde. */
  waitTime: DurationStatisticsDto | null;
}

/** Ein Tag der Zeitreihe; `day` ist ein reines Datum („2026-09-19“, C# `DateOnly`). */
export interface AnalyticsDayPointDto {
  day: string;
  startedCount: number;
  finishedCount: number;
}

/** Antwort von `GET /operations/analytics/workflows/{metaDefinitionId}`. */
export interface WorkflowAnalyticsDetailDto {
  fromUtc: string;
  toUtc: string;
  summary: WorkflowAnalyticsSummaryDto;
  /** null = alle Versionen. */
  definitionId: string | null;
  /** Version, aus der die Knotennamen stammen; null, wenn keine lesbar war. */
  namingDefinitionId: string | null;
  /** Bereits serverseitig nach Median-Wartezeit absteigend sortiert. */
  nodes: FlowNodeAnalyticsDto[];
  timeline: AnalyticsDayPointDto[];
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

/* ------------------------------------------------------- Eingehende Ausloeser */

/**
 * Art eines eingehenden Ausloesers: Er startet entweder einen Workflow oder stellt
 * eine Nachricht an eine laufende Instanz zu.
 *
 * Anders als bei den KI-Verbindungen kommen diese Aufzaehlungen als Zeichenketten
 * aus der API; eine Normalisierung von Zahlen ist deshalb nicht noetig.
 */
export type InboundTriggerKind = 'start' | 'message';

/** Woher die Prozessvariablen kommen: aus einzelnen Feldern oder aus dem ganzen Body. */
export type InboundTriggerVariablesMode = 'fields' | 'body';

export interface InboundTriggerDto {
  id: string;
  /** Oeffentlicher Teil der Aufrufadresse `POST /trigger/{key}`. */
  key: string;
  name: string;
  kind: InboundTriggerKind;
  /** Nur bei `kind === 'start'` gesetzt. */
  definitionId?: string | null;
  /** Nur bei `kind === 'message'` gesetzt. */
  messageName?: string | null;
  /** Nur bei `kind === 'message'`: Pfad zum Korrelationswert im Body, z. B. `order.id`. */
  correlationKeyPath?: string | null;
  variablesMode: InboundTriggerVariablesMode;
  allowedFields: string[];
  enabled: boolean;
  createdAt: string;
  lastUsedAt?: string | null;
  useCount: number;
  lastFailureAt?: string | null;
  /**
   * Kurzer fester Grund der letzten Ablehnung: `disabled`, `timestamp`, `signature`,
   * `payload`, `correlation-key` oder `not-deployed`. Enthaelt nie Daten des Aufrufers.
   */
  lastFailureReason?: string | null;
}

/**
 * Antwort auf Anlegen und Rotieren. Das Geheimnis wird genau einmal uebertragen und
 * darf deshalb weder in den Query-Cache der Liste noch in eine andere Ansicht gelangen.
 */
export interface InboundTriggerSecretDto {
  trigger: InboundTriggerDto;
  secret: string;
}

export interface CreateInboundTriggerInput {
  name: string;
  kind: InboundTriggerKind;
  definitionId?: string;
  messageName?: string;
  correlationKeyPath?: string;
  variablesMode: InboundTriggerVariablesMode;
  allowedFields?: string[];
}

/** Die Art bleibt fest: `PUT` ignoriert sie, deshalb steht sie hier gar nicht erst. */
export interface UpdateInboundTriggerInput {
  name: string;
  enabled: boolean;
  definitionId?: string;
  messageName?: string;
  correlationKeyPath?: string;
  variablesMode: InboundTriggerVariablesMode;
  allowedFields?: string[];
}

/* ------------------------------------------------------------------ Prozesspakete */

/**
 * Die Arten installationsgebundener Bezüge eines Pakets. `directoryUser`, `directoryGroup`
 * und `aiConnection` müssen beim Import zugeordnet werden; die übrigen sind Hinweise
 * darauf, was die Zielinstallation bereitstellen muss.
 */
export type ProcessPackageReferenceKind =
  | 'directoryUser'
  | 'directoryGroup'
  | 'aiConnection'
  | 'jobType'
  | 'secret'
  | 'calledProcess'
  | 'calledDecision';

export interface ProcessPackageWorkflowDto {
  definitionId: string;
  name: string;
  description?: string | null;
  version: string;
  processIds: string[];
  /** `deployed` oder `draft` — ein Entwurf hat keine unveränderlichen Formularbindungen. */
  source: 'deployed' | 'draft';
}

export interface ProcessPackageFormDto {
  formId?: string | null;
  name: string;
  revision?: string | null;
  formKey: string;
  file: string;
  /** Ein Formular aus dem Diagramm; es reist im BPMN mit. */
  embedded: boolean;
}

export interface ProcessPackageReferenceDto {
  id: string;
  kind: ProcessPackageReferenceKind;
  elementId: string;
  elementName?: string | null;
  /** Anzeigename oder technischer Name — niemals eine Personenkennung, nie ein Secret-Wert. */
  label: string;
  requiresMapping: boolean;
}

export interface ProcessPackageManifestDto {
  format: string;
  formatVersion: number;
  exportedAt: string;
  flowzerVersion: string;
  bpmnCapabilitiesContract: number;
  formsContract: string;
  workflow: ProcessPackageWorkflowDto;
  forms: ProcessPackageFormDto[];
  references: ProcessPackageReferenceDto[];
}

export interface ProcessPackageCandidateDto {
  id: string;
  label: string;
  hint?: string | null;
}

export interface ProcessPackageReferenceOptionsDto {
  reference: ProcessPackageReferenceDto;
  candidates: ProcessPackageCandidateDto[];
  /** Ein gleichnamiger Eintrag dieser Installation; vorbelegt, aber nie still angewandt. */
  suggestedId?: string | null;
}

export interface ProcessPackageFindingDto {
  code: string;
  message: string;
  elementId?: string | null;
}

export interface ProcessPackageConflictDto {
  definitionId: string;
  name: string;
  latestVersion?: string | null;
  mayCreateNewVersion: boolean;
}

export interface ProcessPackagePreviewDto {
  manifest: ProcessPackageManifestDto;
  deployableHere: boolean;
  formsContractSupported: boolean;
  problems: ProcessPackageFindingDto[];
  notices: ProcessPackageFindingDto[];
  references: ProcessPackageReferenceOptionsDto[];
  conflict?: ProcessPackageConflictDto | null;
}

/** Zielentscheidung und Zuordnungen, mit denen importiert wird. */
export interface ProcessPackageMappingDto {
  mode: 'new' | 'newVersionOf';
  definitionId?: string | null;
  folderId?: string | null;
  name?: string | null;
  references?: Record<string, string>;
}

export interface ProcessPackageImportedFormDto {
  formKey: string;
  name: string;
  formId?: string | null;
  revision?: string | null;
  outcome: 'created' | 'reused' | 'revised' | 'embedded';
}

export interface ProcessPackageImportResultDto {
  definitionId: string;
  name: string;
  versionId: string;
  version: VersionDto;
  forms: ProcessPackageImportedFormDto[];
  appliedReferences: ProcessPackageReferenceDto[];
  notices: ProcessPackageFindingDto[];
}
