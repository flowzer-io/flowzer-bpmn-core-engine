import type { ProcessVariables } from '@/lib/api/types';

const MAX_INPUT_LENGTH = 100_000;
const UNSAFE_KEYS = new Set(['__proto__', 'constructor', 'prototype']);

/** Testdaten gelangen in eine Drittanbieter-Bibliothek: Objektform und sichere Grenzen erzwingen. */
export function parseFormPreviewData(text: string): ProcessVariables {
  if (text.length > MAX_INPUT_LENGTH) throw new Error('Die JSON-Eingabe darf höchstens 100.000 Zeichen enthalten.');
  let data: unknown;
  try {
    data = JSON.parse(text);
  } catch {
    // Parsertexte können vertrauliche Feldinhalte enthalten. Nur eine feste Meldung anzeigen.
    throw new Error('Ungültiges JSON. Bitte Anführungszeichen, Kommas und Klammern prüfen.');
  }
  if (data === null || typeof data !== 'object' || Array.isArray(data)) {
    throw new Error('Die JSON-Eingabe muss ein Objekt mit Feldschlüsseln sein, zum Beispiel {"reason":"Urlaub"}.');
  }
  validateValue(data, 0);
  return data as ProcessVariables;
}

function validateValue(value: unknown, depth: number): void {
  if (depth > 64) throw new Error('Die JSON-Eingabe ist zu tief verschachtelt (höchstens 64 Ebenen).');
  if (typeof value === 'number' && !Number.isFinite(value)) throw new Error('Eine Zahl liegt außerhalb des unterstützten Wertebereichs.');
  if (value === null || typeof value !== 'object') return;
  for (const [key, child] of Object.entries(value)) {
    if (UNSAFE_KEYS.has(key)) throw new Error('Die JSON-Eingabe enthält einen nicht erlaubten technischen Schlüssel.');
    validateValue(child, depth + 1);
  }
}
