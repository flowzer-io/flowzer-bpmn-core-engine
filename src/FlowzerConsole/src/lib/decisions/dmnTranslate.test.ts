import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

import { translateDmn, translatedDmnTexts } from './dmnTranslate';

/** Alle festen Texte, die dmn-js in der installierten Fassung durch `translate` schickt. */
function literalTranslateCalls(): Set<string> {
  const roots = [
    'dmn-js-decision-table',
    'dmn-js-drd',
    'dmn-js-shared',
    'dmn-js-literal-expression',
    'dmn-js-boxed-expression',
  ].map((name) => path.join('node_modules', name, 'lib'));
  const found = new Set<string>();
  const walk = (directory: string) => {
    for (const entry of readdirSync(directory)) {
      const full = path.join(directory, entry);
      if (statSync(full).isDirectory()) walk(full);
      else if (full.endsWith('.js')) {
        for (const match of readFileSync(full, 'utf8').matchAll(/translate\((['"])(.+?)\1/g)) {
          found.add(match[2]!);
        }
      }
    }
  };
  roots.forEach(walk);
  return found;
}

describe('Deutsche Beschriftung fuer dmn-js', () => {
  // Testzweck: Bekannte Texte werden uebersetzt, Platzhalter eingesetzt.
  it('uebersetzt und setzt Platzhalter ein', () => {
    expect(translateDmn('Hit policy:')).toBe('Trefferregel:');
    expect(translateDmn('Unique')).toBe('Eindeutig');
    expect(translateDmn('Function kind: {kind}', { kind: 'FEEL' })).toBe('Funktionsart: FEEL');
  });

  // Testzweck: Ein unbekannter Text bleibt stehen statt zu verschwinden; auch seine
  // Platzhalter werden eingesetzt, damit keine rohen Klammern sichtbar werden.
  it('laesst Unbekanntes lesbar stehen', () => {
    expect(translateDmn('Brand new label')).toBe('Brand new label');
    expect(translateDmn('Hello {name}', { name: 'Welt' })).toBe('Hello Welt');
    expect(translateDmn('Hello {name}')).toBe('Hello {name}');
  });

  // Testzweck: Jeder feste Text der installierten dmn-js-Fassung ist uebersetzt. Bringt ein
  // Update neue Texte mit, faellt das hier auf und nicht erst als englischer Fleck in der
  // Oberflaeche.
  it('deckt alle festen Texte der installierten dmn-js-Fassung ab', () => {
    const covered = new Set(translatedDmnTexts);
    const missing = [...literalTranslateCalls()].filter((text) => !covered.has(text));
    expect(missing).toEqual([]);
  });
});
