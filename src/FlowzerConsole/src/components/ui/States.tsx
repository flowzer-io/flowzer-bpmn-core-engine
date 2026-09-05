import type { ReactNode } from 'react';

import { ApiError } from '@/lib/api/client';
import { cn } from '@/lib/cn';

import { Button } from './Button';
import { EmptyState } from './Card';
import { Icon } from './Icon';

/** Platzhalterfläche während des Ladens — hält das Layout stabil. */
export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('bg-surface-2 animate-pulse rounded-[var(--r-sm)]', className)} />;
}

export function LoadingRows({ rows = 4, className }: { rows?: number; className?: string }) {
  return (
    <div className={cn('flex flex-col gap-2 p-4', className)}>
      {Array.from({ length: rows }, (_, index) => (
        <Skeleton key={index} className="h-12 w-full" />
      ))}
    </div>
  );
}

export function InlineSpinner({ label }: { label?: string }) {
  return (
    <div className="text-muted flex items-center gap-2 text-[13.5px]">
      <Icon name="progress_activity" size={18} className="animate-spin" />
      {label ?? 'Wird geladen …'}
    </div>
  );
}

interface ErrorStateProps {
  error: unknown;
  onRetry?: () => void;
  title?: string;
  className?: string;
}

/**
 * Einheitliche Fehleranzeige. Sie unterscheidet bewusst zwischen „API nicht
 * erreichbar“ und fachlichen Fehlern, weil das im Betrieb die häufigste
 * Rückfrage ist.
 */
export function ErrorState({ error, onRetry, title, className }: ErrorStateProps) {
  const unreachable = error instanceof ApiError && error.status === 0;
  const message = error instanceof Error ? error.message : 'Unbekannter Fehler.';

  return (
    <EmptyState
      className={className}
      icon={unreachable ? 'cloud_off' : 'error'}
      title={title ?? (unreachable ? 'Keine Verbindung zur Flowzer-API' : 'Daten konnten nicht geladen werden')}
      description={
        unreachable ? (
          <>
            Prüfe, ob die Web-API läuft und <code className="font-mono text-[12.5px]">VITE_FLOWZER_API_URL</code>{' '}
            korrekt gesetzt ist.
          </>
        ) : (
          message
        )
      }
      action={
        onRetry ? (
          <Button icon="refresh" onClick={onRetry}>
            Erneut versuchen
          </Button>
        ) : undefined
      }
    />
  );
}

/**
 * Kombiniert Lade-, Fehler- und Leerzustand einer Query an einer Stelle,
 * damit jede Seite dasselbe Verhalten zeigt.
 */
interface QueryBoundaryProps<T> {
  isPending: boolean;
  error: unknown;
  data: T | undefined;
  onRetry?: () => void;
  loading?: ReactNode;
  empty?: ReactNode;
  isEmpty?: (data: T) => boolean;
  children: (data: T) => ReactNode;
}

export function QueryBoundary<T>({
  isPending,
  error,
  data,
  onRetry,
  loading,
  empty,
  isEmpty,
  children,
}: QueryBoundaryProps<T>) {
  if (isPending) return <>{loading ?? <LoadingRows />}</>;
  if (error) return <ErrorState error={error} onRetry={onRetry} />;
  if (data === undefined) return <ErrorState error={new Error('Die API lieferte keine Daten.')} onRetry={onRetry} />;
  if (empty && isEmpty?.(data)) return <>{empty}</>;
  return <>{children(data)}</>;
}
