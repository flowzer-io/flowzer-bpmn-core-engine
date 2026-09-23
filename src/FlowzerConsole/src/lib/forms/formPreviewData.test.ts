import { describe, expect, it } from 'vitest';
import { parseFormPreviewData } from './formPreviewData';

describe('parseFormPreviewData', () => {
  // Testzweck: Verschachtelte Eingabedaten und typisierte Referenzen bleiben unverändert.
  it('liest ein JSON-Objekt mit Feldwerten', () => {
    const value = { reason: 'Urlaub', address: { city: 'Bocholt' }, people: [{ kind: 'user', id: 'example' }], days: 3, urgent: false };
    expect(parseFormPreviewData(JSON.stringify(value))).toEqual(value);
  });

  // Testzweck: Keine stillschweigende Typumwandlung oder Syntaxkorrektur von Testdaten.
  it.each(['{', '[]', 'null', '"text"', '42', 'true'])('weist ungültige Objekt-Eingabe %s zurück', text => {
    expect(() => parseFormPreviewData(text)).toThrow();
  });

  // Testzweck: Prototype-Schlüssel dürfen nicht in Form.io-Merge-Pfade gelangen.
  it.each(['__proto__', 'constructor', 'prototype'])('weist verschachtelten Spezialschlüssel %s zurück', key => {
    expect(() => parseFormPreviewData(`{"rows":[{"${key}":{}}]}`)).toThrow(/Schlüssel/);
    expect(Object.prototype).not.toHaveProperty('polluted');
  });

  // Testzweck: Begrenzte Testdaten verhindern Überlastung und Zahlen, die als null serialisiert würden.
  it('begrenzt Größe, Tiefe und nicht endliche Zahlen', () => {
    expect(() => parseFormPreviewData(' '.repeat(100_001))).toThrow(/100.000/);
    expect(() => parseFormPreviewData('{"number":1e999}')).toThrow(/Zahl/);
    expect(() => parseFormPreviewData('{"nested":'.repeat(66) + '0' + '}'.repeat(66))).toThrow(/verschachtelt/);
  });
});
