// @vitest-environment node
// Der Test liest Quelldateien von der Platte; unter jsdom wäre import.meta.url keine Dateiadresse.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { ICON_PATHS } from './icons.gen';

const SRC_ROOT = fileURLToPath(new URL('../../', import.meta.url));

/** Alle .ts-/.tsx-Quelldateien unterhalb von src, ohne Tests und ohne die generierte Icon-Datei. */
function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    if (!/\.tsx?$/.test(path) || /\.test\.tsx?$/.test(path) || path.endsWith('icons.gen.ts')) return [];
    return [path];
  });
}

/**
 * Wo im Code literale Icon-Namen stehen. Jede Regel liefert in Gruppe 1 entweder direkt
 * einen Namen oder (bei Ausdrücken) den Text, aus dem die Namen gelesen werden.
 */
/** Form.io-Komponentendefinitionen tragen Builder-Icons von Form.io, keine Material-Symbole. */
const FORMIO_COMPONENT = /components\/forms\/[^/]*Component\.tsx?$/;

const RULES: { name: string; pattern: RegExp; literalsInside?: boolean; skip?: RegExp }[] = [
  // <Icon name="…"> und jede *icon="…"-Prop (icon, confirmIcon, …), die ihr Symbol an Icon durchreicht
  { name: 'JSX-Prop mit Literal', pattern: /(?:<Icon\b[^>]*?\bname|\b[A-Za-z]*[iI]con)\s*=\s*"([a-z0-9_]+)"/g },
  // name={a ? 'x' : 'y'} bzw. icon={…}: nur die Literale hinter ? und :, nicht die Vergleichswerte
  { name: 'JSX-Prop mit Ausdruck', pattern: /(?:<Icon\b[^>]*?\bname|\b[A-Za-z]*[iI]con)\s*=\s*\{([^}]*)\}/g, literalsInside: true },
  // Objekteinträge wie { icon: 'x' } in Menüs, Tabs und Paletten
  { name: 'Objekteintrag icon:', pattern: /\bicon:\s*'([a-z0-9_]+)'/g, skip: FORMIO_COMPONENT },
  // Zuordnungstabellen wie const TYPE_ICONS = { task: 'crop_square', … }
  { name: 'Zuordnungstabelle', pattern: /\b[A-Z_]+_ICONS?\b[^=]*=\s*\{([^}]*)\}/g, literalsInside: true },
];

/**
 * Literale hinter `?` oder `:` innerhalb eines Ausdrucks bzw. Objekts. Vergleichswerte wie
 * in `theme === 'dark' ? …` stehen hinter `===` und bleiben bewusst außen vor.
 */
function literalsIn(expression: string): string[] {
  return [...expression.matchAll(/[?:]\s*'([a-z0-9_]+)'/g)].map((match) => match[1] ?? '').filter(Boolean);
}

describe('Icon-Verwendung', () => {
  it('jeder literal verwendete Icon-Name ist in icons.gen.ts erzeugt', () => {
    // Testzweck: Ein Icon-Name ohne erzeugten Pfad rendert in Produktion nur einen leeren
    // Platzhalter; der Test macht die Lücke in der CI sichtbar statt erst in der Oberfläche.
    const missing: string[] = [];
    let checked = 0;

    for (const file of sourceFiles(SRC_ROOT)) {
      const source = readFileSync(file, 'utf8');
      for (const rule of RULES) {
        if (rule.skip?.test(file)) continue;
        for (const match of source.matchAll(rule.pattern)) {
          const captured = match[1] ?? '';
          const names = rule.literalsInside ? literalsIn(captured) : [captured];
          for (const name of names) {
            checked += 1;
            if (!(name in ICON_PATHS)) missing.push(`${name} (${relative(SRC_ROOT, file)}, ${rule.name})`);
          }
        }
      }
    }

    // Schutz gegen leerlaufende Regeln: Die Konsole verwendet deutlich mehr als 100 Icon-Stellen.
    expect(checked).toBeGreaterThan(100);
    expect(missing, 'Namen in scripts/generate-icons.mjs ergänzen und `npm run icons` ausführen').toEqual([]);
  });
});
