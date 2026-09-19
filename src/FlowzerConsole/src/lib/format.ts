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

const MINUTE_SECONDS = 60;
const HOUR_SECONDS = 60 * MINUTE_SECONDS;
const DAY_SECONDS = 24 * HOUR_SECONDS;

/**
 * Dauern der Auswertung (Sekunden) über die ganze Spanne, die ein Vorgang haben kann:
 * „42 s“, „3,5 min“, „2 h 15 min“, „3 d 4 h“.
 *
 * Die größere Einheit gibt den Ton an, die kleinere ergänzt nur — ab einer Stunde
 * interessiert niemanden mehr die Sekunde. Unter zehn Minuten steht eine Nachkommastelle,
 * weil dort der Unterschied zwischen 3 und 3,5 Minuten noch eine Aussage ist.
 *
 * `formatDuration` bleibt daneben bestehen: Es rechnet in Millisekunden und beschreibt
 * Maschinenzeiten (ein Timer-Tick), nicht Vorgangsdauern.
 */
export function formatDurationSeconds(seconds: number | null | undefined): string {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) return '—';

  if (seconds < MINUTE_SECONDS) return `${Math.round(seconds)} s`;

  const minutes = seconds / MINUTE_SECONDS;
  // Die Rundung entscheidet über die Einheit: 59,98 Minuten sind eine Stunde und nicht
  // „60 min“. Ohne diese Prüfung schriebe die nächste Stufe „1 h 60 min“.
  if (seconds < HOUR_SECONDS && Math.round(minutes) < 60) {
    const rounded = Math.round(minutes * 10) / 10;
    return minutes < 10 && !Number.isInteger(rounded)
      ? `${formatNumber(rounded)} min`
      : `${formatNumber(Math.round(minutes))} min`;
  }

  if (seconds < DAY_SECONDS) {
    const { major: hours, minor: restMinutes } = splitDuration(seconds, HOUR_SECONDS, MINUTE_SECONDS, 60);
    if (hours < 24) {
      return restMinutes === 0 ? `${formatNumber(hours)} h` : `${formatNumber(hours)} h ${restMinutes} min`;
    }
  }

  const { major: days, minor: hours } = splitDuration(seconds, DAY_SECONDS, HOUR_SECONDS, 24);
  return hours === 0 ? `${formatNumber(days)} d` : `${formatNumber(days)} d ${hours} h`;
}

/**
 * Teilt eine Dauer in große und kleine Einheit. Die gerundete kleine Einheit kann die
 * große vollmachen (59,7 min); dann wandert sie dorthin, statt als „1 h 60 min“ zu erscheinen.
 */
function splitDuration(
  seconds: number,
  majorSeconds: number,
  minorSeconds: number,
  minorPerMajor: number,
): { major: number; minor: number } {
  const major = Math.floor(seconds / majorSeconds);
  const minor = Math.round((seconds - major * majorSeconds) / minorSeconds);
  return minor === minorPerMajor ? { major: major + 1, minor: 0 } : { major, minor };
}

/** Workflow- und Formularversionen in der Schreibweise der Konsole, z. B. „v1.2". */
export function formatVersion(version: { major: number; minor: number } | null | undefined): string {
  return version ? `v${version.major}.${version.minor}` : 'v?';
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
