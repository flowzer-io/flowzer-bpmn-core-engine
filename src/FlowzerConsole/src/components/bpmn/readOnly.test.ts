import { describe, expect, it } from 'vitest';

import { READ_ONLY_MODULE } from './readOnly';

/** Liest einen didi-Eintrag der Form `['value', …]` aus dem Modul. */
function value<T>(name: string): T {
  const entry = READ_ONLY_MODULE[name] as [string, T];
  return entry[1];
}

describe('READ_ONLY_MODULE', () => {
  // Testzweck: Die Regelquelle der Ansicht lehnt jede Modellieraktion ab. bpmn-js fragt
  // vor Verschieben, Anlegen, Verbinden, Loeschen und Groesse aendern `rules.allowed` —
  // ein einziges `true` an dieser Stelle machte das Diagramm wieder bearbeitbar.
  it('erlaubt keine Modellieraktion', () => {
    const rules = value<{ allowed: (action: string) => boolean }>('rules');

    for (const action of [
      'elements.move',
      'shape.create',
      'shape.resize',
      'elements.delete',
      'connection.create',
      'connection.updateWaypoints',
    ]) {
      expect(rules.allowed(action), `"${action}" darf ohne Modelliererrolle nicht erlaubt sein.`).toBe(
        false,
      );
    }
  });

  // Testzweck: Palette und Beschriftungseditor bekommen keinen Anbieter. Ohne Anbieter
  // zeichnet diagram-js die Palette gar nicht erst, und ein Doppelklick oeffnet kein
  // Eingabefeld am Element.
  it('nimmt Palette und Beschriftungseditor ihren Anbieter', () => {
    expect(value('paletteProvider')).toBeNull();
    expect(value('labelEditingProvider')).toBeNull();
  });

  // Testzweck: Das Kontextpad geht nicht auf. Ohne Anbieter waere es zwar leer, diagram-js
  // oeffnete aber trotzdem eine leere Sprechblase am gewaehlten Element.
  it('haelt das Kontextpad geschlossen', () => {
    const [kind, provider] = READ_ONLY_MODULE.flowzerReadOnly as [
      string,
      { (eventBus: unknown): void; $inject: string[] },
    ];
    expect(kind).toBe('type');
    expect(provider.$inject).toEqual(['eventBus']);

    const handlers = new Map<string, () => unknown>();
    provider({ on: (event: string, callback: () => unknown) => handlers.set(event, callback) });

    expect(handlers.get('contextPad.open.allowed')?.()).toBe(false);
  });
});
