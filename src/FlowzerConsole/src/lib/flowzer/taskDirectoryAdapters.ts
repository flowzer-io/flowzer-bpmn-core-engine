import type {
  DirectorySubjectSearchOptions,
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
): BoundDirectorySubjectAdapter {
  return {
    cacheKey: ['user-task-form', taskId],
    search: async (fieldKey, options) => normalizeSearchResult(await searchSubjects(fieldKey, {
      query: options.query,
      kind: options.kind,
      limit: 20,
      signal: options.signal,
    })),
    resolve: async (fieldKey, subjects, signal) => {
      const results = await Promise.all(subjects.map((subject) => searchSubjects(fieldKey, {
        query: subject.id,
        kind: subject.kind,
        limit: 20,
        signal,
      })));
      return uniqueDirectorySubjects(results.flatMap((result) => normalizeSearchResult(result).items))
        .filter((candidate) => subjects.some((subject) => sameSubject(candidate.subject, subject)));
    },
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
      }];
    }),
  };
}

function normalizeSubject(subject: SubjectRef): SubjectRefDto | null {
  if (subject.kind !== 'user' && subject.kind !== 'group') return null;
  return { kind: subject.kind, id: subject.id };
}

function sameSubject(left: SubjectRefDto, right: SubjectRefDto): boolean {
  return left.kind === right.kind && left.id === right.id;
}

function uniqueDirectorySubjects(subjects: DirectorySubjectDto[]): DirectorySubjectDto[] {
  return subjects.filter((subject, index) => subjects.findIndex(
    (candidate) => sameSubject(candidate.subject, subject.subject),
  ) === index);
}
