import type {
  AnalyticsDayPointDto,
  AnalyticsRangeQuery,
  FlowNodeAnalyticsDto,
  WorkflowAnalyticsSummaryDto,
} from '@/lib/api/types';

/**
 * Die Rechenregeln der Auswertungsseiten — Zeitraum, Balkenbreiten und die Ableitung
 * einer Zeile aus einer Antwort der API.
 *
 * Sie stehen hier ohne React, weil sie Randfälle tragen, die man einzeln prüfen können
 * muss: ein leerer Datensatz, ein Zeitraum ohne jede Instanz, eine Verteilung, in der
 * jeder Wert null ist. In JSX versteckt wären sie nur über das Rendern erreichbar.
 *
 * Eine Regel gilt ausdrücklich nicht hier: Die Reihenfolge der Schritte bestimmt der
 * Server (Engpass zuerst). Die Konsole übernimmt sie unverändert — würde sie noch einmal
 * sortieren, zeigte die Seite eine andere Rangfolge als die Auswertung berechnet hat.
 */

/** Suchparameter der Route `/analytics` (beide Seiten teilen sie). */
export interface AnalyticsSearch {
  from?: string;
  to?: string;
  days?: number;
}

/** Zeitraumknöpfe der Oberfläche. */
export const RANGE_PRESETS = [7, 30, 90] as const;

/** Voreinstellung, wenn die Adresse nichts anderes sagt. */
export const DEFAULT_RANGE_DAYS = 30;

/** Obergrenze, mit der eine abwegige Adresse (`?days=99999`) nicht zur 422 der API führt. */
const MAX_RANGE_DAYS = 366;

const DAY_MS = 24 * 60 * 60 * 1000;

export interface ResolvedRange {
  /** Untergrenze als ISO-8601 mit Zonenangabe, wie die API sie erwartet. */
  fromIso: string;
  toIso: string;
  /** Die gewählte Spanne in Tagen, oder `custom` bei zwei eigenen Datumsangaben. */
  days: number | 'custom';
}

/**
 * Bestimmt den auszuwertenden Zeitraum aus den Suchparametern.
 *
 * `now` ist ein Parameter, damit der Test nicht von der Uhr des Rechners abhängt.
 * Ein eigener Zeitraum zählt nur mit beiden Grenzen: Eine einzelne Angabe wäre eine
 * halboffene Spanne, deren zweites Ende die Oberfläche stillschweigend erfinden müsste.
 */
export function resolveRange(search: AnalyticsSearch, now: Date): ResolvedRange {
  const fromIso = dayStartIso(search.from);
  const toIso = dayEndIso(search.to);

  if (fromIso && toIso) return { fromIso, toIso, days: 'custom' };

  const days = normalizeRangeDays(search.days);
  return {
    fromIso: new Date(now.getTime() - days * DAY_MS).toISOString(),
    toIso: now.toISOString(),
    days,
  };
}

/**
 * Die Grenzen als Query der API. Die Konsole schickt immer beide mit, auch bei der
 * Voreinstellung: So wertet ein Neuladen denselben Zeitraum aus wie die erste Anzeige.
 */
export function rangeQuery(range: ResolvedRange): AnalyticsRangeQuery {
  return { from: range.fromIso, to: range.toIso };
}

/** „01.09.2026 – 19.09.2026“ — die Grenzen des ausgewerteten Zeitraums als Text. */
export function rangeLabel(range: ResolvedRange): string {
  return `${germanDay(range.fromIso)} – ${germanDay(range.toIso)}`;
}

function germanDay(iso: string): string {
  const [year, month, day] = dateInputValue(iso).split('-');
  return year && month && day ? `${day}.${month}.${year}` : iso;
}

/** Der Zeitraumknopf, der zur Adresse gehört. */
export function rangeSelection(range: ResolvedRange): string {
  return range.days === 'custom' ? 'custom' : String(range.days);
}

/** Wert für ein `<input type="date">`: nur der Tag, ohne Uhrzeit und Zone. */
export function dateInputValue(iso: string | null | undefined): string {
  if (!iso) return '';
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? '' : parsed.toISOString().slice(0, 10);
}

/**
 * Breite eines CSS-Balkens in Prozent.
 *
 * Ohne Maßstab (`max === 0`, etwa wenn im Zeitraum nichts gemessen wurde) ist die
 * ehrliche Antwort ein leerer Balken und keine Division durch null.
 */
export function barWidthPercent(value: number, max: number): number {
  if (!Number.isFinite(value) || !Number.isFinite(max)) return 0;
  if (max <= 0 || value <= 0) return 0;

  return Math.round(Math.min(100, (value / max) * 100) * 100) / 100;
}

/** Zustandston eines Ausgangs; ein Abbruch ist regulär und deshalb nie rot. */
export type OutcomeTone = 'run' | 'done' | 'wait' | 'fail';

export interface OutcomeSlice {
  key: 'running' | 'completed' | 'cancelled' | 'failed';
  label: string;
  value: number;
  tone: OutcomeTone;
}

/**
 * Die Verteilung einer Zeile nach Ausgang, in fester Reihenfolge.
 *
 * Der Abbruch bekommt den Ton „wait“ und nicht „fail“: Er ist ein gewollter Ausgang,
 * dem niemand nachgehen muss — genau wie in der Instanzliste und im Betriebsbild.
 */
export function outcomeSlices(summary: WorkflowAnalyticsSummaryDto): OutcomeSlice[] {
  return [
    { key: 'running', label: 'laufend', value: summary.runningCount, tone: 'run' },
    { key: 'completed', label: 'abgeschlossen', value: summary.completedCount, tone: 'done' },
    { key: 'cancelled', label: 'abgebrochen', value: summary.cancelledCount, tone: 'wait' },
    { key: 'failed', label: 'gescheitert', value: summary.failedCount, tone: 'fail' },
  ];
}

/** Summe der Ausgänge — Maßstab des Verteilungsbalkens, auch wenn `totalCount` abweicht. */
export function outcomeTotal(summary: WorkflowAnalyticsSummaryDto): number {
  return outcomeSlices(summary).reduce((sum, slice) => sum + slice.value, 0);
}

/**
 * Der Maßstab des Wartezeitdiagramms: der höchste p90 im Datensatz entspricht 100 %.
 * Fehlt er (kein vollständiger Durchlauf), zählt der Median desselben Schritts.
 */
export function waitTimeScaleMax(nodes: readonly FlowNodeAnalyticsDto[]): number {
  return nodes.reduce(
    (max, node) => Math.max(max, node.waitTime?.p90Seconds ?? node.waitTime?.medianSeconds ?? 0),
    0,
  );
}

/** Der höchste Tageswert der Zeitreihe — gestartet und beendet teilen sich eine Skala. */
export function timelineScaleMax(timeline: readonly AnalyticsDayPointDto[]): number {
  return timeline.reduce((max, point) => Math.max(max, point.startedCount, point.finishedCount), 0);
}

/** Anzeigename eines Schritts; ohne Namen bleibt die technische Kennung. */
export function flowNodeLabel(node: FlowNodeAnalyticsDto): string {
  const name = node.name?.trim();
  return name && name.length > 0 ? name : node.flowNodeId;
}

/**
 * Tagesbeschriftung der Zeitreihe („19.09.“).
 *
 * Bewusst ohne `Date`: `day` ist ein reines Datum. Über einen Zeitstempel geparst, läge
 * es in einer Zone und könnte westlich von Greenwich als Vortag erscheinen.
 */
export function dayLabel(day: string): string {
  const [, month, dayOfMonth] = day.split('-');
  return month && dayOfMonth ? `${dayOfMonth}.${month}.` : day;
}

/** Begrenzt die Spanne auf etwas, das die API auch auswerten kann. */
function normalizeRangeDays(days: number | undefined): number {
  if (typeof days !== 'number' || !Number.isFinite(days)) return DEFAULT_RANGE_DAYS;

  const rounded = Math.round(days);
  if (rounded < 1) return DEFAULT_RANGE_DAYS;
  return Math.min(rounded, MAX_RANGE_DAYS);
}

/** „2026-09-01“ wird zum Tagesbeginn, ein vollständiger Zeitstempel bleibt er selbst. */
function dayStartIso(value: string | undefined): string | null {
  return normalizeBoundary(value, 'T00:00:00.000Z');
}

/** Die Obergrenze schließt den genannten Tag ein — sonst fehlte der letzte Tag der Wahl. */
function dayEndIso(value: string | undefined): string | null {
  return normalizeBoundary(value, 'T23:59:59.999Z');
}

function normalizeBoundary(value: string | undefined, suffix: string): string | null {
  if (typeof value !== 'string' || value.trim().length === 0) return null;

  const candidate = /^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}${suffix}` : value;
  const parsed = new Date(candidate);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}
