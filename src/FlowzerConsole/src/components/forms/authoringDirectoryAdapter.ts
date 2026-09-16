import type { BoundDirectorySubjectAdapter } from '@/components/bpmn/properties/DirectorySubjectPicker';
import { requestStatusResult } from '@/lib/api/client';
import type { DirectorySubjectSearchResultDto, DirectorySubjectResolutionResultDto } from '@/lib/api/types';

/** Rollenbeschränkte Autorensuche; keine Ersatzautorisierung für laufende Aufgaben. */
export function authoringDirectoryAdapter(formId: string, formData?: string): BoundDirectorySubjectAdapter {
  const base = `/identity-directory/authoring-forms/${encodeURIComponent(formId)}/subjects`;
  const context = (fieldKey: string) => formData ? { formData, fieldKey } : {};
  return {
    cacheKey: ['form-authoring', formId, formData ?? 'configuration'],
    search: (fieldKey, { query, kind, signal }) => requestStatusResult<DirectorySubjectSearchResultDto>(`${base}/search`, {
      method: 'POST', body: { ...context(fieldKey), query, kind }, signal,
    }),
    resolve: async (fieldKey, subjects, signal) => (await requestStatusResult<DirectorySubjectResolutionResultDto>(`${base}/resolve`, {
      method: 'POST', body: { ...context(fieldKey), subjects }, signal,
    })).items,
  };
}
