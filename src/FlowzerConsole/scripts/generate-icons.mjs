/**
 * Erzeugt `src/components/ui/icons.gen.ts` aus `@material-symbols/svg-400`.
 *
 * Hintergrund: Die Ligatur-Variante von Material Symbols liefert eine 3,6-MB-Schrift
 * mit allen ~3.000 Glyphen. Die Konsole braucht davon knapp 80. Statt der Schrift
 * werden deshalb nur die benötigten Pfaddaten eingebettet.
 *
 * Neues Icon hinzufügen: Namen in ICONS ergänzen und `npm run icons` ausführen.
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const projectRoot = resolve(here, '..');
const sourceDir = resolve(projectRoot, 'node_modules/@material-symbols/svg-400/outlined');
const target = resolve(projectRoot, 'src/components/ui/icons.gen.ts');

const ICONS = [
  'account_tree',
  'add',
  'adjust',
  'ads_click',
  'api',
  'arrow_back',
  'arrow_forward',
  'assignment',
  'back_hand',
  'badge',
  'bolt',
  'calendar_today',
  'call_merge',
  'call_split',
  'check',
  'check_circle',
  'chevron_right',
  'cloud_off',
  'code',
  'content_copy',
  'contrast',
  'crop_square',
  'dark_mode',
  'data_object',
  'database',
  'description',
  'download',
  'edit',
  'error',
  'event_available',
  'explore_off',
  'filter_alt_off',
  'flight_takeoff',
  'gavel',
  'home',
  'how_to_reg',
  'hub',
  'inbox',
  'info',
  'inventory_2',
  'left_panel_close',
  'left_panel_open',
  'light_mode',
  'mail',
  'monitoring',
  'more_horiz',
  'notifications',
  'notifications_active',
  'notifications_off',
  'person',
  'play_circle',
  'progress_activity',
  'radio_button_checked',
  'receipt_long',
  'redo',
  'refresh',
  'remove',
  'rocket_launch',
  'rule',
  'save',
  'schedule',
  'schema',
  'search',
  'search_off',
  'send',
  'sensors',
  'settings',
  'shopping_cart',
  'smart_toy',
  'space_dashboard',
  'stop_circle',
  'task_alt',
  'timeline',
  'today',
  'undo',
  'unfold_more',
  'upload',
  'warning',
];

/** Schneidet den Inhalt zwischen den <svg>-Tags heraus. */
function innerSvg(markup) {
  const match = /<svg[^>]*>([\s\S]*)<\/svg>/.exec(markup);
  if (!match) throw new Error('Unerwartetes SVG-Format.');
  return match[1].trim().replace(/\s+/g, ' ');
}

const entries = [];
const missing = [];

for (const name of [...new Set(ICONS)].sort()) {
  try {
    entries.push([name, innerSvg(readFileSync(resolve(sourceDir, `${name}.svg`), 'utf8'))]);
  } catch {
    missing.push(name);
  }
}

if (missing.length > 0) {
  console.error(`Nicht gefunden in @material-symbols/svg-400/outlined: ${missing.join(', ')}`);
  process.exitCode = 1;
}

const body = entries.map(([name, markup]) => `  '${name}': '${markup.replace(/'/g, "\\'")}',`).join('\n');

const output = `// Automatisch erzeugt von scripts/generate-icons.mjs — nicht von Hand bearbeiten.
// Quelle: @material-symbols/svg-400 (Apache-2.0). Neues Icon: Namen im Skript
// ergänzen und \`npm run icons\` ausführen.

/** Alle Icons der Konsole als SVG-Inhalt (viewBox "0 -960 960 960"). */
export const ICON_PATHS: Record<string, string> = {
${body}
};

export type IconName = keyof typeof ICON_PATHS;
`;

mkdirSync(dirname(target), { recursive: true });
writeFileSync(target, output, 'utf8');

console.log(`${entries.length} Icons nach ${target} geschrieben.`);
