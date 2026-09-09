import type {
  CompleteUserTaskCommand,
  DirectorySubjectSearchOptions,
  DirectorySubjectSearchResult,
  ExtendedUserTask,
  FlowzerCompletionOptions,
  FlowzerForm,
  ProcessVariables,
  SaveUserTaskDraftCommand,
  UserTaskDraft,
} from '@flowzer/sdk';

/**
 * Neutraler Übergabevertrag an einen Host-Renderer. Das Basispaket interpretiert weder
 * Form.io-Schemas noch fachliche Felder und erweitert keine serverseitige Auswahlpolicy.
 */
export interface FlowzerTaskFormAdapterProps {
  task: ExtendedUserTask;
  form: FlowzerForm;
  draft: UserTaskDraft;
  data: ProcessVariables;
  onChange: (data: ProcessVariables) => void;
  searchSubjects: (
    fieldKey: string,
    options: DirectorySubjectSearchOptions,
  ) => Promise<DirectorySubjectSearchResult>;
  saveDraft: (command: SaveUserTaskDraftCommand) => Promise<UserTaskDraft>;
  complete: (
    command: CompleteUserTaskCommand,
    options: FlowzerCompletionOptions,
  ) => Promise<void>;
}
