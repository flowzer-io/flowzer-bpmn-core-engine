import { describe, expect, it } from 'vitest';

import {
  changedCorrections,
  formatCorrectionDraft,
  parseCorrections,
  parseRetries,
} from './jobRetry';

describe('Eingabekorrektur einer Freigabe', () => {
  // Testzweck: Ein leeres Feld heißt „nichts korrigieren". Würde daraus ein leeres Objekt,
  // schickte die Oberfläche bei jeder Freigabe eine Korrektur mit, die keine ist.
  it('liest ein leeres Feld als „keine Korrektur"', () => {
    expect(parseCorrections('')).toEqual({});
    expect(parseCorrections('   \n ')).toEqual({});
  });

  // Testzweck: Genannte Felder gehen als Objekt an die API; sie mischt sie in die vorhandenen
  // Eingaben hinein.
  it('liest ein JSON-Objekt als Korrektur', () => {
    expect(parseCorrections('{ "iban": "DE99", "betrag": 42 }')).toEqual({
      variables: { iban: 'DE99', betrag: 42 },
    });
  });

  // Testzweck: Kaputtes JSON blockiert mit einem Hinweis, statt als Text an die API zu gehen
  // und dort in einer technischen Fehlermeldung zu enden.
  it('nennt ungültiges JSON als Grund', () => {
    expect(parseCorrections('{ "iban": ').error).toMatch(/kein gültiges JSON/);
  });

  // Testzweck: Ein Array oder ein blanker Wert ist kein Feld-zu-Wert-Paar. Abgeschickt würde
  // er still nichts bewirken — der Fehler fiele erst auf, wenn der Worker wieder scheitert.
  it.each(['[1, 2]', '"nur Text"', '42', 'null'])('lehnt %s ab, weil es kein Objekt ist', (text) => {
    expect(parseCorrections(text).error).toMatch(/JSON-Objekt/);
  });
});

describe('Anzahl Versuche einer Freigabe', () => {
  // Testzweck: Die Oberfläche prüft dieselben Grenzen wie die API, damit die Freigabe nicht
  // erst nach dem Absenden an einem 400 scheitert.
  it.each(['1', '100', '7'])('nimmt %s an', (text) => {
    expect(parseRetries(text).retries).toBe(Number(text));
  });

  it.each(['0', '-1', '101', '1,5', '1.5', '', 'zwei'])('lehnt %s ab', (text) => {
    expect(parseRetries(text).error).toMatch(/von 1 bis 100/);
  });
});

describe('Vorbelegung des Korrekturfelds', () => {
  // Testzweck: Das Feld zeigt die aktuellen Eingaben, damit der Betrieb korrigiert statt
  // abtippt. Ohne Eingaben bleibt es leer — ein vorgegebenes `{}` wäre eine Korrektur ohne
  // Inhalt und würde bei jeder Freigabe mitgeschickt.
  it('setzt die vorhandenen Eingaben, sonst nichts', () => {
    expect(formatCorrectionDraft({ iban: 'DE00' })).toBe('{\n  "iban": "DE00"\n}');
    expect(formatCorrectionDraft({})).toBe('');
    expect(formatCorrectionDraft(null)).toBe('');
    expect(formatCorrectionDraft(undefined)).toBe('');
  });
});

describe('Was eine Freigabe als Korrektur mitschickt', () => {
  // Testzweck: Das Feld ist mit allen Eingaben vorbelegt. Unverändert abgeschickt wären sie
  // alle eine Korrektur, und die Freigabespur nennte jeden Schlüssel als überschrieben. Nur
  // geänderte und neue Felder gehen mit; ohne Änderung geht gar keine Korrektur mit.
  it('schickt nur geänderte und neue Felder', () => {
    const original = { iban: 'DE00', amount: 10, address: { city: 'Bonn' } };

    expect(changedCorrections(original, { iban: 'DE00', amount: 10, address: { city: 'Bonn' } })).toBeUndefined();
    expect(changedCorrections(original, { iban: 'DE02', amount: 10 })).toEqual({ iban: 'DE02' });
    expect(changedCorrections(original, { iban: 'DE00', note: 'neu' })).toEqual({ note: 'neu' });
    expect(changedCorrections(original, { address: { city: 'Köln' } })).toEqual({ address: { city: 'Köln' } });
    expect(changedCorrections(null, { iban: 'DE00' })).toEqual({ iban: 'DE00' });
    expect(changedCorrections(original, undefined)).toBeUndefined();
  });
});
