import { describe, expect, it } from 'vitest';
import { parseEmbedEntry, validStartSnapshot, validTaskSnapshot } from './EmbedSnapshot';
const start = { definitionId: '11111111-1111-1111-1111-111111111111', relatedDefinitionId: 'workflow',
  hostOrigin: 'https://host.test', form: { formData: '{"components":[]}' } };
describe('Zweckgebundene Embed-Einstiege', () => {
  // Testzweck: Historische Aufgabenfragments bleiben unverändert; nur der explizite
  // Startpräfix wählt den zweiten isolierten Read-only-Pfad, niemals eine freie URL.
  it('trennt Start und Aufgabe ohne freie Zieladresse', () => {
    expect(parseEmbedEntry('A'.repeat(43))).toEqual({ purpose: 'task', secret: 'A'.repeat(43) });
    expect(parseEmbedEntry('start.' + 'B'.repeat(43))).toEqual({ purpose: 'start', secret: 'B'.repeat(43) });
    for (const value of ['', 'A'.repeat(44), 'start.' + 'B'.repeat(42), 'https://evil.test', 'start.' + '/'.repeat(43)])
      expect(() => parseEmbedEntry(value)).toThrow();
  });
  // Testzweck: Eine Startantwort darf nicht mit künstlichen Task-/Draftdaten oder
  // mutierten Versions-/Hostwerten als erfolgreiche persönliche Startanzeige gelten.
  it('prüft den separaten Startsnapshot streng', () => {
    expect(validStartSnapshot(start)).toBe(true);
    for (const value of [{ ...start, definitionId: 'falsch' }, { ...start, definitionId: '00000000-0000-0000-0000-000000000000' },
      { ...start, relatedDefinitionId: '' }, { ...start, hostOrigin: 'https://host.test/path' },
      { ...start, userTaskId: start.definitionId }, { ...start, draft: {} }, { ...start, context: {} }])
      expect(validStartSnapshot(value)).toBe(false);
    expect(validTaskSnapshot(start)).toBe(false);
  });
});
