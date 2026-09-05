import { cn } from '@/lib/cn';

import { toneSurface } from './Chip';

export interface SegmentedOption<T extends string> {
  value: T;
  label: string;
  /** Optionale Zählmarke rechts vom Label (wie bei den Instanz-Filtern). */
  count?: number;
}

interface SegmentedProps<T extends string> {
  options: readonly SegmentedOption<T>[];
  value: T;
  onChange: (value: T) => void;
  className?: string;
  'aria-label'?: string;
}

/**
 * Der segmentierte Umschalter aus dem Design — eingesetzt für die Dashboard-Ansicht
 * („Liste“/„Nach Prozess“) und die Instanz-Filter.
 */
export function Segmented<T extends string>({
  options,
  value,
  onChange,
  className,
  'aria-label': ariaLabel,
}: SegmentedProps<T>) {
  return (
    <div
      role="tablist"
      aria-label={ariaLabel}
      className={cn('bg-surface-2 flex gap-[3px] rounded-[var(--r-sm)] p-[3px]', className)}
    >
      {options.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(option.value)}
            className={cn(
              'inline-flex cursor-pointer items-center gap-[7px] rounded-md border-none px-3 py-1.5',
              'text-[13px] font-semibold transition-colors duration-150',
              active ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text bg-transparent',
            )}
          >
            {option.label}
            {option.count !== undefined && (
              <span
                className="min-w-[18px] rounded-full px-1.5 text-center text-[11px] font-bold"
                style={
                  active
                    ? { background: toneSurface('accent', 15), color: 'var(--accent)' }
                    : { background: 'var(--border)', color: 'var(--muted)' }
                }
              >
                {option.count}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
