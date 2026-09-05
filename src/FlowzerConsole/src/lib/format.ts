import { format, formatDistanceToNowStrict, isThisYear, isToday, isYesterday } from 'date-fns';
import { de } from 'date-fns/locale';

/**
 * Datums- und Zahlformatierung für die deutschsprachige Oberfläche.
 * Die API liefert UTC-Zeitstempel ohne Zonenangabe — sie werden hier explizit
 * als UTC interpretiert, damit die Anzeige in lokaler Zeit stimmt.
 */

export function parseApiDate(value: string | null | undefined): Date | null {
  if (!value) return null;

  // ASP.NET serialisiert `DateTime` ohne Zonen-Suffix. Ohne "Z" würde der Browser
  // den Wert als Ortszeit lesen und die Anzeige um den UTC-Offset verschieben.
  const normalized = /(?:Z|[+-]\d{2}:?\d{2})$/.test(value) ? value : `${value}Z`;
  const parsed = new Date(normalized);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/** „Heute, 09:14“ · „Gestern, 16:41“ · „2. Juli, 14:05“ */
export function formatTimestamp(value: string | Date | null | undefined): string {
  const date = value instanceof Date ? value : parseApiDate(value);
  if (!date) return '—';

  if (isToday(date)) return `Heute, ${format(date, 'HH:mm')}`;
  if (isYesterday(date)) return `Gestern, ${format(date, 'HH:mm')}`;
  if (isThisYear(date)) return format(date, "d. MMMM, HH:mm", { locale: de });
  return format(date, 'dd.MM.yyyy, HH:mm', { locale: de });
}

/** Kurzform ohne Uhrzeit, z. B. für Kartenfüße: „vor 2 Tagen“. */
export function formatRelative(value: string | Date | null | undefined): string {
  const date = value instanceof Date ? value : parseApiDate(value);
  if (!date) return '—';
  return `vor ${formatDistanceToNowStrict(date, { locale: de })}`;
}

/** Restlaufzeit in die Zukunft: „in 1 Tag“, „überfällig“. */
export function formatDueIn(value: string | Date | null | undefined): string {
  const date = value instanceof Date ? value : parseApiDate(value);
  if (!date) return '—';

  const deltaMs = date.getTime() - Date.now();
  if (deltaMs <= 0) return 'überfällig';
  return `in ${formatDistanceToNowStrict(date, { locale: de })}`;
}

export function formatTime(value: string | Date | null | undefined): string {
  const date = value instanceof Date ? value : parseApiDate(value);
  return date ? format(date, 'HH:mm') : '—';
}

const NUMBER_FORMAT = new Intl.NumberFormat('de-DE');

export function formatNumber(value: number | null | undefined): string {
  return typeof value === 'number' ? NUMBER_FORMAT.format(value) : '—';
}

export function formatDuration(milliseconds: number | null | undefined): string {
  if (typeof milliseconds !== 'number') return '—';
  if (milliseconds < 1000) return `${Math.round(milliseconds)} ms`;
  if (milliseconds < 60_000) return `${(milliseconds / 1000).toFixed(1)} s`;
  return `${Math.round(milliseconds / 60_000)} min`;
}

/** Kürzt technische Guids auf eine lesbare Instanzkennung („A3F9-2E7“ im Design). */
export function shortId(id: string | null | undefined): string {
  if (!id) return '—';
  const compact = id.replace(/-/g, '').toUpperCase();
  return `${compact.slice(0, 4)}-${compact.slice(4, 7)}`;
}

/** Formatiert einen Prozessvariablenwert für die monospaced Anzeige. */
export function formatVariableValue(value: unknown): string {
  if (value === null) return 'null';
  if (value === undefined) return 'undefined';
  if (typeof value === 'string') return `"${value}"`;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

export function greetingForNow(now: Date = new Date()): string {
  const hour = now.getHours();
  if (hour < 5) return 'Gute Nacht';
  if (hour < 11) return 'Guten Morgen';
  if (hour < 18) return 'Guten Tag';
  return 'Guten Abend';
}

/** „Freitag · 4. Juli 2026“ */
export function formatTodayLabel(now: Date = new Date()): string {
  return format(now, "EEEE · d. MMMM yyyy", { locale: de });
}
