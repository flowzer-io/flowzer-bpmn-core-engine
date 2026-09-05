import type { HTMLAttributes, ReactNode } from 'react';

import { cn } from '@/lib/cn';

import { Icon } from './Icon';

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
}

/** Die Grundfläche des Designs: helle Karte, feine Kante, weicher Schatten. */
export function Card({ className, children, ...rest }: CardProps) {
  return (
    <div
      className={cn(
        'bg-surface border-border shadow-card overflow-hidden rounded-[var(--r-lg)] border',
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  );
}

interface CardHeaderProps {
  title: ReactNode;
  icon?: string;
  iconClassName?: string;
  actions?: ReactNode;
  className?: string;
  /** Kopfzeile leicht abgesetzt (wie bei den Betriebs-Panels). */
  filled?: boolean;
}

export function CardHeader({
  title,
  icon,
  iconClassName,
  actions,
  className,
  filled = false,
}: CardHeaderProps) {
  return (
    <div
      className={cn(
        'border-border flex items-center gap-2.5 border-b px-[18px] py-[15px]',
        filled && 'bg-surface-2',
        className,
      )}
    >
      {icon && <Icon name={icon} size={19} className={cn('text-accent', iconClassName)} />}
      <div className="font-display min-w-0 flex-1 text-[15.5px] font-semibold">{title}</div>
      {actions}
    </div>
  );
}

/** Kleine, gesperrte Monospace-Überschrift („PROZESSVARIABLEN“). */
export function SectionLabel({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        'font-mono text-[11px] font-semibold tracking-[0.1em] uppercase',
        'text-muted',
        className,
      )}
    >
      {children}
    </div>
  );
}

interface EmptyStateProps {
  icon: string;
  title: string;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}

export function EmptyState({ icon, title, description, action, className }: EmptyStateProps) {
  return (
    <div
      className={cn(
        'flex flex-col items-center gap-2.5 px-6 py-[52px] text-center',
        className,
      )}
    >
      <span className="bg-surface-2 text-faint grid h-[46px] w-[46px] place-items-center rounded-full">
        <Icon name={icon} size={24} />
      </span>
      <div className="text-[15px] font-semibold">{title}</div>
      {description && <div className="text-muted max-w-[340px] text-[13.5px]">{description}</div>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}
