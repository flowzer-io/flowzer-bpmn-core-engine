import { completionOptions, FlowzerTransport } from './transport.js';
import type {
  AiConnection,
  AiTool,
  CompleteUserTaskCommand,
  CreateAiConnectionCommand,
  CreateFormSectionCommand,
  DirectorySubjectResolutionResult,
  DirectorySubjectSearchOptions,
  DirectorySubjectSearchResult,
  ExtendedUserTask,
  FlowzerCallOptions,
  FlowzerClientOptions,
  FlowzerCompletionOptions,
  FlowzerForm,
  FormSectionAuthoringDraft,
  FormSectionMetadata,
  FormSectionVersion,
  FormSectionVersionNumber,
  FormSectionVersionSummary,
  ProcessInstance,
  ProcessHistory,
  RuntimeDiagram,
  RenameFormSectionCommand,
  ReleaseUserTaskCommand,
  SaveFormSectionAuthoringDraftCommand,
  SaveUserTaskDraftCommand,
  SetAiConnectionEnabledCommand,
  SubjectRef,
  TaskAssigneeResolutionOptions,
  TaskAssigneeSearchOptions,
  TransferUserTaskCommand,
  UpdateAiConnectionCommand,
  UserTaskDraft,
  UserTaskRevisionCommand,
  UserTaskWorkState,
} from './types.js';

function segment(value: string) {
  return encodeURIComponent(value);
}

function revision(value: number, operation: string, positive = false) {
  if (!Number.isSafeInteger(value) || value < (positive ? 1 : 0)) {
    throw new TypeError(`${operation} requires a ${positive ? 'positive' : 'non-negative'} safe integer revision.`);
  }
  return value;
}

/** Kodiert nur eine explizite, kanonische major.minor-Fassung in den API-Pfad. */
function sectionVersionPath(version: FormSectionVersionNumber) {
  if (!version || !Number.isSafeInteger(version.major) || !Number.isSafeInteger(version.minor)
    || version.major < 0 || version.minor < 0) {
    throw new TypeError('A form-section version requires non-negative safe integer major and minor values.');
  }
  return `${version.major}.${version.minor}`;
}

/** Öffentlicher, hostneutraler Einstiegspunkt ohne globale Sitzung oder UI-Abhängigkeit. */
export class FlowzerClient {
  private readonly transport: FlowzerTransport;

  constructor(options: FlowzerClientOptions) {
    this.transport = new FlowzerTransport(options);
  }

  readonly userTasks = {
    list: async (options: FlowzerCallOptions = {}): Promise<ExtendedUserTask[]> =>
      (await this.transport.status<ExtendedUserTask[]>('/usertask', options)) ?? [],

    get: (userTaskId: string, options: FlowzerCallOptions = {}): Promise<ExtendedUserTask> =>
      this.transport.statusResult(`/usertask/${segment(userTaskId)}`, options),

    getForm: (userTaskId: string, options: FlowzerCallOptions = {}): Promise<FlowzerForm> =>
      this.transport.statusResult(`/usertask/${segment(userTaskId)}/form`, options),

    getDraft: (userTaskId: string, options: FlowzerCallOptions = {}): Promise<UserTaskDraft> =>
      this.transport.statusResult(`/usertask/${segment(userTaskId)}/draft`, options),

    saveDraft: (
      userTaskId: string,
      command: SaveUserTaskDraftCommand,
      options: FlowzerCallOptions = {},
    ): Promise<UserTaskDraft> => this.transport.statusResult(`/usertask/${segment(userTaskId)}/draft`, {
      method: 'PUT',
      body: command,
      signal: options.signal,
    }),

    deleteDraft: (
      userTaskId: string,
      expectedRevision: number,
      expectedTaskRevision?: number,
      options: FlowzerCallOptions = {},
    ): Promise<void> => this.transport.statusVoid(`/usertask/${segment(userTaskId)}/draft`, {
      method: 'DELETE',
      query: { expectedRevision, expectedTaskRevision },
      signal: options.signal,
    }),

    claim: (
      userTaskId: string,
      command: UserTaskRevisionCommand,
      options: FlowzerCallOptions = {},
    ): Promise<UserTaskWorkState> => this.taskAction(userTaskId, 'claim', command, options),

    release: (
      userTaskId: string,
      command: ReleaseUserTaskCommand,
      options: FlowzerCallOptions = {},
    ): Promise<UserTaskWorkState> => this.taskAction(userTaskId, 'release', command, options),

    assign: (
      userTaskId: string,
      command: TransferUserTaskCommand,
      options: FlowzerCallOptions = {},
    ): Promise<UserTaskWorkState> => this.taskAction(userTaskId, 'assign', command, options),

    delegate: (
      userTaskId: string,
      command: TransferUserTaskCommand,
      options: FlowzerCallOptions = {},
    ): Promise<UserTaskWorkState> => this.taskAction(userTaskId, 'delegate', command, options),

    searchAssignees: (
      userTaskId: string,
      options: TaskAssigneeSearchOptions,
    ): Promise<DirectorySubjectSearchResult> => this.transport.statusResult(
      `/identity-directory/user-tasks/${segment(userTaskId)}/assignees`,
      {
        query: { action: options.action, query: options.query, limit: options.limit },
        signal: options.signal,
      },
    ),

    resolveAssignees: (
      userTaskId: string,
      options: TaskAssigneeResolutionOptions,
    ): Promise<DirectorySubjectResolutionResult> => this.transport.statusResult(
      `/identity-directory/user-tasks/${segment(userTaskId)}/assignees/resolve`,
      {
        method: 'POST',
        query: { action: options.action },
        body: { subjects: options.subjects },
        signal: options.signal,
      },
    ),

    searchFormSubjects: (
      userTaskId: string,
      fieldKey: string,
      options: DirectorySubjectSearchOptions,
    ): Promise<DirectorySubjectSearchResult> => this.transport.statusResult(
      `/identity-directory/user-tasks/${segment(userTaskId)}/fields/${segment(fieldKey)}/subjects`,
      {
        query: { query: options.query, kind: options.kind, limit: options.limit },
        signal: options.signal,
      },
    ),

    resolveFormSubjects: (
      userTaskId: string,
      fieldKey: string,
      subjects: readonly SubjectRef[],
      options: FlowzerCallOptions = {},
    ): Promise<DirectorySubjectResolutionResult> => this.transport.statusResult(
      `/identity-directory/user-tasks/${segment(userTaskId)}/fields/${segment(fieldKey)}/subjects/resolve`,
      {
        method: 'POST',
        body: { subjects },
        signal: options.signal,
      },
    ),

    complete: async (
      command: CompleteUserTaskCommand,
      options: FlowzerCompletionOptions,
    ): Promise<void> => this.transport.statusVoid('/usertask', {
      method: 'POST',
      body: command,
      ...completionOptions(options),
    }),
  };

  readonly instances = {
    list: async (options: FlowzerCallOptions = {}): Promise<ProcessInstance[]> =>
      (await this.transport.status<ProcessInstance[]>('/instance', options)) ?? [],

    get: (instanceId: string, options: FlowzerCallOptions = {}): Promise<ProcessInstance> =>
      this.transport.statusResult(`/instance/${segment(instanceId)}`, options),

    /** Lädt die serverseitig berechtigte, datensparsame Prozesshistorie. */
    history: (instanceId: string, options: FlowzerCallOptions = {}): Promise<ProcessHistory> =>
      this.transport.statusResult(`/instance/${segment(instanceId)}/history`, options),

    /** Lädt die serverseitig bereinigte Laufzeitprojektion der gebundenen BPMN-Version. */
    runtimeDiagram: (instanceId: string, options: FlowzerCallOptions = {}): Promise<RuntimeDiagram> =>
      this.transport.statusResult(`/instance/${segment(instanceId)}/runtime-diagram`, options),
  };

  /**
   * Verwaltung der hostneutralen KI-Verbindungsmetadaten. Antworten enthalten
   * absichtlich weder Secret-Werte noch die beim Schreiben verwendete Referenz.
   */
  readonly aiConnections = {
    list: async (options: FlowzerCallOptions = {}): Promise<AiConnection[]> =>
      (await this.transport.status<AiConnection[]>('/ai/connection', options)) ?? [],

    get: (connectionId: string, options: FlowzerCallOptions = {}): Promise<AiConnection> =>
      this.transport.statusResult(`/ai/connection/${segment(connectionId)}`, options),

    create: (
      command: CreateAiConnectionCommand,
      options: FlowzerCallOptions = {},
    ): Promise<AiConnection> => this.transport.statusResult('/ai/connection', {
      method: 'POST', body: command, signal: options.signal,
    }),

    update: async (
      connectionId: string,
      command: UpdateAiConnectionCommand,
      options: FlowzerCallOptions = {},
    ): Promise<AiConnection> => {
      revision(command.expectedRevision, 'Updating an AI connection', true);
      return this.transport.statusResult(`/ai/connection/${segment(connectionId)}`, {
        method: 'PUT', body: command, signal: options.signal,
      });
    },

    setEnabled: async (
      connectionId: string,
      command: SetAiConnectionEnabledCommand,
      options: FlowzerCallOptions = {},
    ): Promise<AiConnection> => {
      revision(command.expectedRevision, 'Changing an AI connection', true);
      return this.transport.statusResult(`/ai/connection/${segment(connectionId)}/enabled`, {
        method: 'PUT', body: command, signal: options.signal,
      });
    },
  };

  /** Nur lesbarer Katalog der serverseitig installierten, typisierten Werkzeugversionen. */
  readonly aiTools = {
    list: async (options: FlowzerCallOptions = {}): Promise<AiTool[]> =>
      (await this.transport.status<AiTool[]>('/ai/tool', options)) ?? [],
  };

  /**
   * Modellierungs-API für wiederverwendbare Abschnitte. Alle Versionszugriffe
   * verlangen ein konkretes major.minor-Paar; eine implizite "latest"-Auflösung
   * existiert im SDK absichtlich nicht.
   */
  readonly formSections = {
    list: async (options: FlowzerCallOptions = {}): Promise<FormSectionMetadata[]> =>
      (await this.transport.status<FormSectionMetadata[]>('/form-section', options)) ?? [],

    get: (sectionId: string, options: FlowzerCallOptions = {}): Promise<FormSectionMetadata> =>
      this.transport.statusResult(`/form-section/${segment(sectionId)}`, options),

    create: (command: CreateFormSectionCommand, options: FlowzerCallOptions = {}): Promise<FormSectionMetadata> =>
      this.transport.statusResult('/form-section', {
        method: 'POST', body: command, signal: options.signal,
      }),

    rename: (
      sectionId: string,
      command: RenameFormSectionCommand,
      options: FlowzerCallOptions = {},
    ): Promise<FormSectionMetadata> => this.transport.statusResult(`/form-section/${segment(sectionId)}`, {
      method: 'PUT', body: command, signal: options.signal,
    }),

    listVersions: async (
      sectionId: string,
      options: FlowzerCallOptions = {},
    ): Promise<FormSectionVersionSummary[]> =>
      (await this.transport.status<FormSectionVersionSummary[]>(
        `/form-section/${segment(sectionId)}/versions`, options,
      )) ?? [],

    getVersion: async (
      sectionId: string,
      version: FormSectionVersionNumber,
      options: FlowzerCallOptions = {},
    ): Promise<FormSectionVersion> => {
      const versionPath = sectionVersionPath(version);
      return this.transport.statusResult(
        `/form-section/${segment(sectionId)}/versions/${versionPath}`,
        options,
      );
    },

    getDraft: (
      sectionId: string,
      options: FlowzerCallOptions = {},
    ): Promise<FormSectionAuthoringDraft> =>
      this.transport.statusResult(`/form-section/${segment(sectionId)}/draft`, options),

    saveDraft: async (
      sectionId: string,
      command: SaveFormSectionAuthoringDraftCommand,
      options: FlowzerCallOptions = {},
    ): Promise<FormSectionAuthoringDraft> => {
      revision(command.expectedRevision, 'Saving a form-section draft');
      return this.transport.statusResult(`/form-section/${segment(sectionId)}/draft`, {
        method: 'PUT', body: command, signal: options.signal,
      });
    },

    deleteDraft: async (
      sectionId: string,
      expectedRevision: number,
      options: FlowzerCallOptions = {},
    ): Promise<void> => {
      revision(expectedRevision, 'Deleting a form-section draft');
      return this.transport.statusVoid(`/form-section/${segment(sectionId)}/draft`, {
        method: 'DELETE', query: { expectedRevision }, signal: options.signal,
      });
    },

    publish: async (
      sectionId: string,
      expectedRevision: number,
      options: FlowzerCallOptions = {},
    ): Promise<FormSectionVersion> => {
      revision(expectedRevision, 'Publishing a form-section draft', true);
      return this.transport.statusResult(`/form-section/${segment(sectionId)}/publish`, {
        method: 'POST', body: { expectedRevision }, signal: options.signal,
      });
    },
  };

  private taskAction<TCommand>(
    userTaskId: string,
    action: 'claim' | 'release' | 'assign' | 'delegate',
    command: TCommand,
    options: FlowzerCallOptions,
  ) {
    return this.transport.statusResult<UserTaskWorkState>(`/usertask/${segment(userTaskId)}/${action}`, {
      method: 'POST',
      body: command,
      signal: options.signal,
    });
  }
}
