import type {
  DirectorySubjectSearchOptions,
  DirectorySubjectResolutionResult,
  DirectorySubjectSearchResult,
  SubjectRef,
  TaskAssigneeSearchOptions,
} from '@flowzer/sdk';

import type {
  BoundDirectorySubjectAdapter,
} from '@/components/bpmn/properties/DirectorySubjectPicker';
import type { SearchAssignees } from '@/components/tasks/TaskLifecycleDialog';
import type {
  DirectorySubjectDto,
  DirectorySubjectSearchResultDto,
  SubjectRefDto,
} from '@/lib/api/types';

type SearchTaskFormSubjects = (
  fieldKey: string,
  options: DirectorySubjectSearchOptions,
) => Promise<DirectorySubjectSearchResult>;

type ResolveTaskFormSubjects = (
  fieldKey: string,
  subjects: readonly SubjectRef[],
  options?: { signal?: AbortSignal | undefined },
) => Promise<DirectorySubjectResolutionResult>;

type SearchTaskAssignees = (
  options: TaskAssigneeSearchOptions,
) => Promise<DirectorySubjectSearchResult>;

/**
 * Bindet den öffentlichen Task-Client an ein konkretes Aufgabenformular. Der
 * verschachtelte Form.io-Root kann nur noch den im Schema enthaltenen Feldschlüssel
 * weiterreichen; Task-ID und API-Vertrag bleiben in der Console-Komposition.
 */
export function createTaskFormDirectoryAdapter(
  taskId: string,
  searchSubjects: SearchTaskFormSubjects,
  resolveSubjects: ResolveTaskFormSubjects,
): BoundDirectorySubjectAdapter {
  return {
    cacheKey: ['user-task-form', taskId],
    search: async (fieldKey, options) => normalizeSearchResult(await searchSubjects(fieldKey, {
      query: options.query,
      kind: options.kind,
      limit: 20,
      signal: options.signal,
    })),
    resolve: async (fieldKey, subjects, signal) => normalizeResolutionResult(
      await resolveSubjects(fieldKey, subjects, { signal }),
    ).items,
  };
}

/** Bindet die öffentliche Suche an genau eine vom Dialog gewählte Lifecycle-Aktion. */
export function createTaskAssigneeSearch(
  action: 'assign' | 'delegate',
  searchAssignees: SearchTaskAssignees,
): SearchAssignees {
  return async ({ query, signal }) => normalizeSearchResult(await searchAssignees({
    action,
    query,
    limit: 20,
    signal,
  }));
}

/** Normalisiert nur die zwei veröffentlichten Subject-Arten an der Paketgrenze. */
function normalizeSearchResult(result: DirectorySubjectSearchResult): DirectorySubjectSearchResultDto {
  return {
    generationId: result.generationId,
    items: (result.items ?? []).flatMap((item): DirectorySubjectDto[] => {
      const subject = normalizeSubject(item.subject);
      if (!subject) return [];
      return [{
        subject,
        displayName: item.displayName?.trim() || subject.id,
        detail: item.detail?.trim() || subject.id,
        isActive: item.isActive !== false,
        isSelectable: item.isSelectable !== false,
      }];
    }),
  };
}

function normalizeResolutionResult(result: DirectorySubjectResolutionResult): DirectorySubjectSearchResultDto {
  return normalizeSearchResult(result);
}

function normalizeSubject(subject: SubjectRef): SubjectRefDto | null {
  if (subject.kind !== 'user' && subject.kind !== 'group') return null;
  return { kind: subject.kind, id: subject.id };
}
