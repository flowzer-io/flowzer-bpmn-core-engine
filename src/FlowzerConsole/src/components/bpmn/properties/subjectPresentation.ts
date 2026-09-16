import type { SubjectRefDto } from '@/lib/api/types';
interface PresentableSubject {
  subject: SubjectRefDto; displayName: string; detail: string;
  email?: string | null; username?: string | null; firstName?: string | null; lastName?: string | null;
}
/** Ausschließlich Anzeige ändern, niemals die gespeicherte SubjectRef. Gruppen behalten ihren Pfad. */
export function presentSubject<T extends PresentableSubject>(item: T, fields?: Record<string, boolean>): T {
  if (!fields || item.subject.kind === 'group') return item;
  const values = [
    fields.name && item.displayName, fields.email && item.email, fields.username && item.username,
    fields.firstName && item.firstName, fields.lastName && item.lastName,
  ].filter((value): value is string => typeof value === 'string' && value.length > 0);
  return { ...item, displayName: values[0] ?? item.displayName, detail: values.slice(1).join(' · ') };
}
