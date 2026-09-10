import { completionOptions, FlowzerTransport } from './transport.js';
import type {
  CompleteUserTaskCommand,
  DirectorySubjectSearchOptions,
  DirectorySubjectSearchResult,
  ExtendedUserTask,
  FlowzerCallOptions,
  FlowzerClientOptions,
  FlowzerCompletionOptions,
  FlowzerForm,
  ProcessInstance,
  ProcessHistory,
  ReleaseUserTaskCommand,
  SaveUserTaskDraftCommand,
  TaskAssigneeSearchOptions,
  TransferUserTaskCommand,
  UserTaskDraft,
  UserTaskRevisionCommand,
  UserTaskWorkState,
} from './types.js';

function segment(value: string) {
  return encodeURIComponent(value);
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
