import { forwardRef, type ButtonHTMLAttributes } from 'react';

import { cn } from '@/lib/cn';

import { Icon } from './Icon';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
type Size = 'sm' | 'md';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  /** Material-Symbols-Name, links vom Label. */
  icon?: string;
  loading?: boolean;
}

const VARIANTS: Record<Variant, string> = {
  primary:
    'bg-accent text-accent-ink border-transparent shadow-card hover:brightness-[1.06] disabled:hover:brightness-100',
  secondary: 'bg-transparent text-text border-border hover:border-border-strong',
  ghost: 'bg-transparent text-muted border-transparent hover:bg-surface-2 hover:text-text',
  danger: 'bg-transparent text-muted border-border hover:border-fail hover:text-fail',
};

const SIZES: Record<Size, string> = {
  sm: 'px-3 py-2 text-[13.5px] gap-1.5',
  md: 'px-4 py-2.5 text-sm gap-2',
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'secondary', size = 'md', icon, loading = false, className, children, disabled, ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      type="button"
      disabled={disabled || loading}
      className={cn(
        'inline-flex shrink-0 cursor-pointer items-center justify-center rounded-[var(--r-sm)] border font-semibold',
        'transition-[background-color,border-color,color,filter] duration-150',
        'disabled:cursor-not-allowed disabled:opacity-55',
        VARIANTS[variant],
        SIZES[size],
        className,
      )}
      {...rest}
    >
      {loading ? (
        <Icon name="progress_activity" size={size === 'sm' ? 17 : 19} className="animate-spin" />
      ) : (
        icon && <Icon name={icon} size={size === 'sm' ? 17 : 19} />
      )}
      {children}
    </button>
  );
});
