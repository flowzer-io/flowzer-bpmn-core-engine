import type { ProcessVariables } from '@/lib/api/types';

/** Die Grenzen, die auch die API prüft; die Oberfläche soll nicht erst am 400 scheitern. */
export const MIN_RETRIES = 1;
export const MAX_RETRIES = 100;

export interface CorrectionsResult {
  variables?: ProcessVariables;
  error?: string;
}

/**
 * Liest die Eingabekorrektur eines Freigabedialogs.
 *
 * Leer heißt „nichts korrigieren", nicht „alles löschen": Die API mischt die genannten
 * Schlüssel in die vorhandenen Eingaben, und ein leeres Feld nennt keinen. Ein Array oder ein
 * blanker Wert wäre dagegen kein Feld-zu-Wert-Paar und würde still nichts bewirken — deshalb
 * wird er hier abgelehnt statt abgeschickt.
 */
export function parseCorrections(text: string): CorrectionsResult {
  if (text.trim().length === 0) return {};

  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { error: 'Das ist kein gültiges JSON. Bitte den Text prüfen.' };
  }

  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return {
      error: 'Erwartet wird ein JSON-Objekt aus Feldnamen und Werten, etwa { "iban": "DE…" }.',
    };
  }

  return { variables: parsed as ProcessVariables };
}

export interface RetriesResult {
  retries?: number;
  error?: string;
}

/** Die Anzahl Versuche als ganze Zahl im erlaubten Bereich; sonst der Grund. */
export function parseRetries(text: string): RetriesResult {
  const value = Number(text.trim());
  if (text.trim().length === 0 || !Number.isInteger(value) || value < MIN_RETRIES || value > MAX_RETRIES) {
    return { error: `Erlaubt ist eine ganze Zahl von ${MIN_RETRIES} bis ${MAX_RETRIES}.` };
  }

  return { retries: value };
}

/**
 * Der Vorschlag für das Korrekturfeld: die aktuellen Eingaben des Auftrags, schön gesetzt.
 * Ohne Eingaben bleibt das Feld leer — ein vorgegebenes `{}` wäre eine Korrektur, die keine ist.
 */
export function formatCorrectionDraft(variables: ProcessVariables | null | undefined): string {
  if (!variables || Object.keys(variables).length === 0) return '';

  return JSON.stringify(variables, null, 2);
}

/**
 * Nur das, was sich gegenüber den vorbelegten Eingaben geändert hat.
 *
 * Das Feld ist mit allen aktuellen Eingaben vorbelegt; unverändert abgeschickt wären sie alle
 * eine „Korrektur", und die Freigabespur nennte jeden Schlüssel als überschrieben. Verglichen
 * wird über die JSON-Form, damit gleiche Objekte gleich zählen; ein neuer Schlüssel zählt immer.
 * Ohne Änderung kommt `undefined` heraus — die Freigabe schickt dann keine Korrektur mit.
 */
export function changedCorrections(
  original: ProcessVariables | null | undefined,
  corrections: ProcessVariables | undefined,
): ProcessVariables | undefined {
  if (!corrections) return undefined;

  const changed: ProcessVariables = {};
  for (const [key, value] of Object.entries(corrections)) {
    const before = original && key in original ? original[key] : undefined;
    if (!(original && key in original) || JSON.stringify(before) !== JSON.stringify(value)) {
      changed[key] = value;
    }
  }

  return Object.keys(changed).length === 0 ? undefined : changed;
}
