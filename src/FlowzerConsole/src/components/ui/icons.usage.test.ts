// @vitest-environment node
// Der Test liest Quelldateien von der Platte; unter jsdom wäre import.meta.url keine Dateiadresse.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { ICON_PATHS } from './icons.gen';

const SRC_ROOT = fileURLToPath(new URL('../../', import.meta.url));

/** Alle .tsx-Dateien unterhalb von src, ohne Tests und ohne die generierte Icon-Datei. */
function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    return path.endsWith('.tsx') && !path.endsWith('.test.tsx') ? [path] : [];
  });
}

/**
 * Literale Icon-Namen im JSX: `<Icon name="…">` sowie jede `icon="…"`-Prop, mit der
 * Karten, Abschnitte und Schaltflächen ihr Symbol an die Icon-Komponente durchreichen.
 */
const ICON_LITERAL = /(?:<Icon\b[^>]*?\bname|\bicon)="([a-z0-9_]+)"/g;

describe('Icon-Verwendung', () => {
  it('jeder literal verwendete Icon-Name ist in icons.gen.ts erzeugt', () => {
    // Testzweck: Ein Icon-Name ohne erzeugten Pfad rendert in Produktion nur einen leeren
    // Platzhalter; der Test macht die Lücke in der CI sichtbar statt erst in der Oberfläche.
    const missing: string[] = [];

    for (const file of sourceFiles(SRC_ROOT)) {
      const source = readFileSync(file, 'utf8');
      for (const match of source.matchAll(ICON_LITERAL)) {
        const name = match[1];
        if (name && !(name in ICON_PATHS)) missing.push(`${name} (${relative(SRC_ROOT, file)})`);
      }
    }

    expect(missing, 'Namen in scripts/generate-icons.mjs ergänzen und `npm run icons` ausführen').toEqual([]);
  });
});
