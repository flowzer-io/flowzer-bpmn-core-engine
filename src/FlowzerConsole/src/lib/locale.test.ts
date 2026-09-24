import { describe, expect, it } from 'vitest';

import { browserFormLanguage, resolveFormLanguage } from './locale';

function browser(languages: readonly string[], language = languages[0] ?? '') {
  return { languages, language };
}

describe('browserFormLanguage', () => {
  // Testzweck: Ein deutscher Browser bekommt deutsche Formulare und Kalender.
  it('wählt Deutsch für de-DE', () => {
    expect(browserFormLanguage(browser(['de-DE', 'de']))).toBe('de');
  });

  // Testzweck: Ein englischer Browser bekommt die englischen Texte von Form.io und flatpickr.
  it('wählt Englisch für en-US', () => {
    expect(browserFormLanguage(browser(['en-US']))).toBe('en');
  });

  // Testzweck: Eine Sprache, die die Formulare nicht beherrschen, fällt auf Deutsch zurück —
  // die Sprache der Konsole — statt auf das englische Form.io-Standardverhalten.
  it('fällt bei Französisch auf Deutsch zurück', () => {
    expect(browserFormLanguage(browser(['fr']))).toBe('de');
  });

  // Testzweck: Ohne jede Sprachangabe des Browsers bleibt es bei Deutsch.
  it('fällt ohne Angabe auf Deutsch zurück', () => {
    expect(browserFormLanguage(browser([], ''))).toBe('de');
    expect(browserFormLanguage(null)).toBe('de');
  });

  // Testzweck: Ältere Browser kennen nur `navigator.language`; auch das wird ausgewertet.
  it('nutzt navigator.language, wenn navigator.languages leer ist', () => {
    expect(browserFormLanguage({ languages: [], language: 'en-GB' })).toBe('en');
  });
});

describe('resolveFormLanguage', () => {
  // Testzweck: Die Liste wird der Reihe nach durchsucht; die erste unterstützte Sprache gilt.
  it('nimmt die erste unterstützte Sprache der Liste', () => {
    expect(resolveFormLanguage(['fr-FR', 'en-GB', 'de-DE'])).toBe('en');
    expect(resolveFormLanguage(['it', 'de-AT'])).toBe('de');
  });

  // Testzweck: Schreibweisen wie `EN_us` oder Leerzeichen dürfen die Erkennung nicht stören.
  it('erkennt die Hauptsprache unabhängig von Schreibweise', () => {
    expect(resolveFormLanguage([' EN_us '])).toBe('en');
    expect(resolveFormLanguage([null, undefined, ''])).toBe('de');
  });
});
