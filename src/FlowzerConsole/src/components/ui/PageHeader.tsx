import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

interface PageHeaderProps {
  title: string;
  description?: ReactNode;
  /** Kleine Monospace-Zeile über dem Titel (z. B. das Tagesdatum). */
  eyebrow?: ReactNode;
  actions?: ReactNode;
  className?: string;
}

export function PageHeader({ title, description, eyebrow, actions, className }: PageHeaderProps) {
  return (
    <div className={cn('mb-5 flex flex-col items-start gap-3 sm:flex-row sm:items-end sm:justify-between sm:gap-4', className)}>
      <div className="min-w-0">
        {eyebrow && (
          <div className="text-muted font-mono text-[11.5px] tracking-[0.13em] uppercase">{eyebrow}</div>
        )}
        <h1 className="font-display m-0 mt-1.5 text-[26px] font-semibold tracking-[-0.02em]">{title}</h1>
        {description && <div className="text-muted mt-1 text-sm">{description}</div>}
      </div>
      {actions && <div className="flex w-full shrink-0 flex-wrap items-center gap-2 sm:w-auto">{actions}</div>}
    </div>
  );
}

/** Der Standard-Seitenrahmen: zentriert, maximal 1200 px breit. */
export function PageContainer({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn('mx-auto w-full max-w-[1200px] px-4 pt-5 pb-[60px] md:px-[34px] md:pt-[26px]', className)}>
      {children}
    </div>
  );
}
