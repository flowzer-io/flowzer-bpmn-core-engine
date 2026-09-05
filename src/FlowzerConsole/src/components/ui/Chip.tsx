import type { ReactNode } from 'react';

import { cn, mix } from '@/lib/cn';

/** Zustandstöne des Designs. */
export type Tone = 'accent' | 'run' | 'done' | 'fail' | 'wait' | 'muted';

const TONE_VARIABLE: Record<Tone, string> = {
  accent: '--accent',
  run: '--run',
  done: '--done',
  fail: '--fail',
  wait: '--wait',
  muted: '--muted',
};

export function toneColor(tone: Tone): string {
  return `var(${TONE_VARIABLE[tone]})`;
}

export function toneSurface(tone: Tone, percent = 14): string {
  return mix(TONE_VARIABLE[tone], percent);
}

interface ChipProps {
  tone?: Tone;
  className?: string;
  children: ReactNode;
}

/** Der eingefärbte Status-Pill aus dem Design (`chip()` im Original). */
export function Chip({ tone = 'muted', className, children }: ChipProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[11px] font-semibold whitespace-nowrap',
        className,
      )}
      style={{ background: toneSurface(tone), color: toneColor(tone) }}
    >
      {children}
    </span>
  );
}

interface DotProps {
  tone?: Tone;
  size?: number;
  halo?: boolean;
  className?: string;
}

export function Dot({ tone = 'accent', size = 8, halo = false, className }: DotProps) {
  return (
    <span
      className={cn('inline-block shrink-0 rounded-full', className)}
      style={{
        width: size,
        height: size,
        background: toneColor(tone),
        boxShadow: halo ? `0 0 0 3px ${toneSurface(tone, 18)}` : undefined,
      }}
    />
  );
}
