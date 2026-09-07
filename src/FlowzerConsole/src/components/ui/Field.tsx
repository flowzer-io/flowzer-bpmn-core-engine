import { forwardRef, type InputHTMLAttributes, type ReactNode } from 'react';

import { cn } from '@/lib/cn';

import { Icon } from './Icon';

/**
 * Feldbeschriftung im Eigenschaften-Panel: klein, gesperrt, monospaced.
 *
 * Mit `htmlFor` wird daraus eine echte Beschriftung, die auf ihr Feld zeigt — ohne sie
 * bleibt ein Eingabefeld fuer Vorlesewerkzeuge namenlos. Ohne `htmlFor` beschriftet sie
 * eine Gruppe und bleibt deshalb ein neutrales Element.
 */
export function FieldLabel({
  children,
  className,
  htmlFor,
}: {
  children: ReactNode;
  className?: string;
  htmlFor?: string;
}) {
  const style = cn(
    'text-muted mb-1.5 block font-mono text-[10.5px] font-medium tracking-[0.06em] uppercase',
    className,
  );

  if (htmlFor) {
    return (
      <label htmlFor={htmlFor} className={style}>
        {children}
      </label>
    );
  }

  return <div className={style}>{children}</div>;
}

export const TextInput = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  function TextInput({ className, ...rest }, ref) {
    return (
      <input
        ref={ref}
        className={cn(
          'bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5',
          'text-[13.5px] outline-none',
          'focus:border-accent',
          className,
        )}
        {...rest}
      />
    );
  },
);

interface SearchInputProps extends InputHTMLAttributes<HTMLInputElement> {
  wrapperClassName?: string;
}

export const SearchInput = forwardRef<HTMLInputElement, SearchInputProps>(function SearchInput(
  { className, wrapperClassName, ...rest },
  ref,
) {
  return (
    <div
      className={cn(
        'bg-surface border-border focus-within:border-border-strong flex items-center gap-2 rounded-[var(--r-sm)] border px-3 py-2.5',
        wrapperClassName,
      )}
    >
      <Icon name="search" size={18} className="text-faint" />
      <input
        ref={ref}
        type="search"
        className={cn(
          'text-text min-w-0 flex-1 border-none bg-transparent text-[13.5px] outline-none',
          '[&::-webkit-search-cancel-button]:appearance-none',
          className,
        )}
        {...rest}
      />
    </div>
  );
});
