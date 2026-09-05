import type { CSSProperties } from 'react';

import { cn } from '@/lib/cn';

import { ICON_PATHS } from './icons.gen';

interface IconProps {
  /** Name eines Material-Symbols-Icons, z. B. `space_dashboard`. */
  name: string;
  className?: string;
  /** Kantenlänge in Pixeln. */
  size?: number;
  title?: string;
  /** Ergänzende Inline-Styles, z. B. eine Zustandsfarbe aus den Tokens. */
  style?: CSSProperties;
}

/**
 * Icons werden als eingebettete SVG-Pfade gerendert (Quelle: Material Symbols,
 * Apache-2.0). Die Ligatur-Schrift wäre mit 3,6 MB unverhältnismäßig — die
 * Konsole nutzt keine 80 Symbole. Neue Icons werden über
 * `scripts/generate-icons.mjs` ergänzt.
 */
export function Icon({ name, className, size = 20, title, style }: IconProps) {
  const path = ICON_PATHS[name];

  if (!path) {
    // Ein fehlendes Icon darf die Oberfläche nicht sprengen: Platzhalter in der
    // erwarteten Größe, im Dev-Betrieb zusätzlich eine Konsolenwarnung.
    if (import.meta.env.DEV) {
      console.warn(`[Icon] "${name}" fehlt in icons.gen.ts — Namen in scripts/generate-icons.mjs ergänzen.`);
    }
    return <span aria-hidden className={cn('inline-block shrink-0', className)} style={{ width: size, height: size }} />;
  }

  return (
    <svg
      viewBox="0 -960 960 960"
      width={size}
      height={size}
      fill="currentColor"
      role={title ? 'img' : 'presentation'}
      aria-hidden={title ? undefined : true}
      aria-label={title}
      className={cn('inline-block shrink-0 select-none', className)}
      style={style}
      dangerouslySetInnerHTML={{ __html: title ? `<title>${title}</title>${path}` : path }}
    />
  );
}
