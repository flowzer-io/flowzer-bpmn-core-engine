import { FlowzerApiError } from '@flowzer/sdk';

import { formatTimestamp } from '@/lib/format';
import type { TaskDraftLoadState, TaskDraftSaveState } from '@/lib/taskDraft';

import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/Icon';
import { InlineSpinner } from '@/components/ui/States';

interface TaskDraftStatusProps {
  loadState: TaskDraftLoadState;
  saveState: TaskDraftSaveState;
  isRefreshing: boolean;
  isSaving: boolean;
  isDiscarding: boolean;
  hasDraft: boolean;
  updatedAtUtc: string | null;
  error: unknown;
  onSave: () => void;
  onDiscard: () => void;
  onLoadServer: () => void;
  dirty: boolean;
}

/** Status- und Aktionsleiste des serverseitigen Aufgabenentwurfs. */
export function TaskDraftStatus({
  loadState,
  saveState,
  isRefreshing,
  isSaving,
  isDiscarding,
  hasDraft,
  updatedAtUtc,
  error,
  onSave,
  onDiscard,
  onLoadServer,
  dirty,
}: TaskDraftStatusProps) {
  if (loadState === 'loading') return <InlineSpinner label="Entwurf wird geladen …" />;

  if (loadState === 'error') {
    return (
      <div className="text-fail flex items-center gap-2 text-[12.5px]" role="alert">
        <Icon name="error" size={17} />
        <span>{error instanceof Error ? error.message : 'Entwurf konnte nicht geladen werden.'}</span>
        <Button size="sm" variant="ghost" icon="refresh" onClick={onLoadServer}>
          Erneut laden
        </Button>
      </div>
    );
  }

  const label = isSaving
    ? 'Entwurf wird gespeichert …'
    : saveState === 'saved'
      ? `Entwurf gespeichert${updatedAtUtc ? ` · ${formatTimestamp(updatedAtUtc)}` : ''}`
        : saveState === 'conflict'
          ? 'Konflikt beim Speichern'
        : saveState === 'error'
          ? 'Speichern fehlgeschlagen'
          : dirty
            ? 'Ungespeicherte Eingaben'
            : hasDraft
              ? 'Entwurf gespeichert'
              : 'Kein gespeicherter Entwurf';

  return (
    <div className="flex flex-wrap items-center justify-between gap-2 text-[12.5px]">
      <span className={saveState === 'conflict' || saveState === 'error' ? 'text-fail' : 'text-muted'}>
        {isRefreshing && <Icon name="refresh" size={15} className="mr-1 align-[-3px] animate-spin" />}
        {label}
      </span>
      <div className="flex items-center gap-2">
        <Button size="sm" variant="ghost" icon="delete" loading={isDiscarding} onClick={onDiscard} disabled={!dirty && !hasDraft}>
          Verwerfen
        </Button>
        <Button size="sm" variant="secondary" icon="save" loading={isSaving} onClick={onSave} disabled={!dirty}>
          Speichern
        </Button>
      </div>
    </div>
  );
}

/** Konflikt bleibt sichtbar, bis die Person den Serverstand ausdrücklich übernimmt. */
export function TaskDraftConflictBanner({
  error,
  loading,
  onLoadServer,
}: {
  error: unknown;
  loading: boolean;
  onLoadServer: () => void;
}) {
  return (
    <div className="border-wait bg-wait/10 text-text mb-4 flex flex-wrap items-center justify-between gap-3 rounded-[var(--r)] border px-3.5 py-3" role="alert">
      <div className="flex min-w-0 items-start gap-2.5">
        <Icon name="error" size={19} className="text-wait mt-0.5 flex-none" />
        <div>
          <div className="text-[13.5px] font-semibold">Der Entwurf wurde zwischenzeitlich geändert.</div>
          <div className="text-muted mt-0.5 text-[12.5px]">
            Deine Eingaben bleiben erhalten. Lade den Serverstand ausdrücklich, um den Konflikt zu lösen.
            {error instanceof FlowzerApiError && error.message ? ` (${error.message})` : ''}
          </div>
        </div>
      </div>
      <Button size="sm" variant="secondary" icon="download" loading={loading} onClick={onLoadServer}>
        Serverstand laden
      </Button>
    </div>
  );
}
