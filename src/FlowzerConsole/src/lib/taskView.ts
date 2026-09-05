import { endOfDay, endOfWeek, isValid, parseISO } from 'date-fns';

import type { Tone } from '@/components/ui/Chip';
import type { ExtendedUserTaskSubscriptionDto } from '@/lib/api/types';
import { formatTimestamp, parseApiDate } from '@/lib/format';

/**
 * Fälligkeitsangaben stammen aus `zeebe:taskSchedule/@dueDate` und sind im BPMN
 * frei belegbar: ein ISO-Zeitpunkt, eine ISO-Dauer oder ein FEEL-Ausdruck. Nur
 * die ersten beiden Formen lassen sich ohne Engine auswerten — bei allem anderen
 * wird der Rohwert angezeigt statt ein Datum zu erfinden.
 */
export function parseDueDate(
  dueDate: string | null | undefined,
  reference: Date,
): { date: Date | null; raw: string | null } {
  if (!dueDate || dueDate.trim().length === 0) return { date: null, raw: null };

  const value = dueDate.trim();

  // FEEL-Ausdrücke beginnen in Zeebe mit "=" und brauchen den Ausdrucks-Interpreter.
  if (value.startsWith('=')) return { date: null, raw: value };

  const isoDuration = parseIsoDuration(value);
  if (isoDuration !== null) return { date: new Date(reference.getTime() + isoDuration), raw: value };

  const parsed = parseISO(value);
  if (isValid(parsed)) return { date: parsed, raw: value };

  return { date: null, raw: value };
}

const DURATION_PATTERN = /^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?)?$/;

/** Wandelt eine ISO-8601-Dauer (z. B. `PT48H`) in Millisekunden um. */
export function parseIsoDuration(value: string): number | null {
  const match = DURATION_PATTERN.exec(value);
  if (!match || value === 'P') return null;

  const [, days, hours, minutes, seconds] = match;
  const total =
    Number(days ?? 0) * 86_400_000 +
    Number(hours ?? 0) * 3_600_000 +
    Number(minutes ?? 0) * 60_000 +
    Number(seconds ?? 0) * 1000;

  return total > 0 ? total : null;
}

export type DueBucket = 'overdue' | 'today' | 'week' | 'later' | 'undated';

export const DUE_BUCKET_LABEL: Record<DueBucket, string> = {
  overdue: 'Überfällig',
  today: 'Heute',
  week: 'Diese Woche',
  later: 'Später',
  undated: 'Ohne Termin',
};

export const DUE_BUCKET_TONE: Record<DueBucket, Tone> = {
  overdue: 'fail',
  today: 'wait',
  week: 'accent',
  later: 'muted',
  undated: 'muted',
};

export const DUE_BUCKET_ORDER: DueBucket[] = ['overdue', 'today', 'week', 'later', 'undated'];

export type Priority = 'Hoch' | 'Mittel' | 'Niedrig';

export const PRIORITY_TONE: Record<Priority, Tone> = {
  Hoch: 'fail',
  Mittel: 'wait',
  Niedrig: 'muted',
};

/** Normalisiert die freie Prioritätsangabe aus dem BPMN auf drei Stufen. */
export function normalizePriority(value: string | null | undefined): Priority | null {
  if (!value) return null;

  const normalized = value.trim().toLowerCase();
  if (['hoch', 'high', '1', 'urgent', 'kritisch'].includes(normalized)) return 'Hoch';
  if (['mittel', 'medium', 'normal', '2'].includes(normalized)) return 'Mittel';
  if (['niedrig', 'low', '3', 'gering'].includes(normalized)) return 'Niedrig';

  const numeric = Number(normalized);
  if (Number.isFinite(numeric)) {
    if (numeric >= 75) return 'Hoch';
    if (numeric >= 40) return 'Mittel';
    return 'Niedrig';
  }

  return null;
}

export interface TaskView {
  task: ExtendedUserTaskSubscriptionDto;
  id: string;
  title: string;
  workflowName: string;
  dueDate: Date | null;
  /** Rohwert der Fälligkeit, wenn sie sich nicht auswerten ließ. */
  dueRaw: string | null;
  dueLabel: string;
  dueBucket: DueBucket;
  priority: Priority | null;
  startedAt: Date | null;
  formKey: string | null;
}

/** Bereitet eine Aufgabe für die Darstellung auf. */
export function toTaskView(task: ExtendedUserTaskSubscriptionDto, now: Date = new Date()): TaskView {
  const { date, raw } = parseDueDate(task.dueDate, now);
  const bucket = dueBucket(date, now, raw);

  return {
    task,
    id: task.id,
    title: task.name?.trim() || task.token.currentFlowNodeId || 'Aufgabe',
    workflowName: task.definitionMetaName || task.processId,
    dueDate: date,
    dueRaw: date ? null : raw,
    dueLabel: date ? formatTimestamp(date) : (raw ?? 'ohne Termin'),
    dueBucket: bucket,
    priority: normalizePriority(task.priority),
    startedAt: parseApiDate(task.token.startTime),
    formKey: task.formKey ?? null,
  };
}

function dueBucket(date: Date | null, now: Date, raw: string | null): DueBucket {
  if (!date) return raw ? 'later' : 'undated';
  if (date.getTime() < now.getTime()) return 'overdue';
  if (date <= endOfDay(now)) return 'today';
  if (date <= endOfWeek(now, { weekStartsOn: 1 })) return 'week';
  return 'later';
}

const PRIORITY_RANK: Record<Priority, number> = { Hoch: 0, Mittel: 1, Niedrig: 2 };

/** Sortiert Aufgaben nach Dringlichkeit: Termin zuerst, dann Priorität. */
export function sortTasks(views: TaskView[]): TaskView[] {
  return [...views].sort((a, b) => {
    const bucketDelta = DUE_BUCKET_ORDER.indexOf(a.dueBucket) - DUE_BUCKET_ORDER.indexOf(b.dueBucket);
    if (bucketDelta !== 0) return bucketDelta;

    if (a.dueDate && b.dueDate) {
      const dueDelta = a.dueDate.getTime() - b.dueDate.getTime();
      if (dueDelta !== 0) return dueDelta;
    }

    const priorityDelta =
      (a.priority ? PRIORITY_RANK[a.priority] : 3) - (b.priority ? PRIORITY_RANK[b.priority] : 3);
    if (priorityDelta !== 0) return priorityDelta;

    return a.title.localeCompare(b.title, 'de');
  });
}

/** Gruppiert Aufgaben nach Fälligkeit — die Standardansicht des Dashboards. */
export function groupByDue(views: TaskView[]): { key: DueBucket; label: string; items: TaskView[] }[] {
  return DUE_BUCKET_ORDER.map((bucket) => ({
    key: bucket,
    label: DUE_BUCKET_LABEL[bucket],
    items: views.filter((view) => view.dueBucket === bucket),
  })).filter((group) => group.items.length > 0);
}

/** Gruppiert Aufgaben nach Prozess — die zweite Ansicht des Dashboards. */
export function groupByWorkflow(views: TaskView[]): { key: string; label: string; items: TaskView[] }[] {
  const byWorkflow = new Map<string, TaskView[]>();

  for (const view of views) {
    const existing = byWorkflow.get(view.workflowName);
    if (existing) existing.push(view);
    else byWorkflow.set(view.workflowName, [view]);
  }

  return [...byWorkflow.entries()]
    .sort(([nameA, itemsA], [nameB, itemsB]) => itemsB.length - itemsA.length || nameA.localeCompare(nameB, 'de'))
    .map(([name, items]) => ({ key: name, label: `${name} · ${items.length}`, items }));
}

/**
 * Wählt ein sprechendes Icon anhand des Fachbegriffs im Namen.
 * Reine Anzeigeheuristik — ohne Treffer bleibt es beim neutralen Symbol.
 */
export function iconForLabel(text: string): string {
  const haystack = text.toLowerCase();

  if (/rechnung|invoice|beleg|zahlung/.test(haystack)) return 'receipt_long';
  if (/urlaub|abwesen|vacation|freistellung/.test(haystack)) return 'event_available';
  if (/onboard|mitarbeit|personal|hr\b/.test(haystack)) return 'badge';
  if (/vertrag|nda|contract|recht/.test(haystack)) return 'gavel';
  if (/reise|travel|spesen/.test(haystack)) return 'flight_takeoff';
  if (/bestell|beschaff|einkauf|purchase/.test(haystack)) return 'shopping_cart';
  if (/freigab|genehm|approv/.test(haystack)) return 'how_to_reg';
  return 'assignment';
}

/** Icon für eine Aufgabe, abgeleitet aus Titel, Prozess und Formularnamen. */
export function taskIcon(view: TaskView): string {
  return iconForLabel(`${view.title} ${view.workflowName} ${view.formKey ?? ''}`);
}
