import type { UserTaskWorkState } from '@flowzer/sdk';

import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/Icon';

interface TaskLifecyclePanelProps {
  state: UserTaskWorkState;
  busy: boolean;
  draftDirty: boolean;
  onClaim: (expectedRevision: number) => void;
  onRelease: (expectedRevision: number) => void;
  onAssign: (expectedRevision: number) => void;
  onDelegate: (expectedRevision: number) => void;
}

/** Zeigt die tatsächliche Laufzeitzuweisung und ausschließlich serverseitig erlaubte Aktionen. */
export function TaskLifecyclePanel({
  state,
  busy,
  draftDirty,
  onClaim,
  onRelease,
  onAssign,
  onDelegate,
}: TaskLifecyclePanelProps) {
  const assigned = state.claimed;
  const label = state.actualAssigneeDisplayName
    ?? state.actualAssignee?.id;
  const transferBlocked = busy || draftDirty;

  return (
    <section className="border-border bg-surface mt-[18px] flex flex-wrap items-center gap-3 rounded-[var(--r)] border px-4 py-3.5">
      <span className="bg-surface-2 text-accent grid h-9 w-9 flex-none place-items-center rounded-[10px]">
        <Icon name={assigned ? 'person' : 'inbox'} size={20} />
      </span>
      <div className="min-w-[180px] flex-1">
        <div className="text-[13.5px] font-semibold">
          {!assigned
            ? 'Offen zur Übernahme'
            : state.isAssignedToCurrentUser
              ? 'Von dir übernommen'
              : 'Bereits zugewiesen'}
        </div>
        <div className="text-muted mt-0.5 truncate text-[12.5px]">
          {assigned
            ? (label ?? 'Bearbeiter ist gesetzt')
            : state.canWork
              ? 'Du kannst sie direkt bearbeiten oder verbindlich für dich übernehmen.'
              : 'Nach der Übernahme kannst du Formular und Entwurf bearbeiten.'}
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-end gap-2">
        {state.canRelease && (
          <Button size="sm" variant="ghost" icon="undo" disabled={transferBlocked}
            title={draftDirty ? 'Entwurf zuerst speichern oder verwerfen' : undefined}
            onClick={() => onRelease(state.revision ?? 0)}>
            Zurückgeben
          </Button>
        )}
        {state.canDelegate && (
          <Button size="sm" variant="secondary" icon="person_add" disabled={transferBlocked}
            title={draftDirty ? 'Entwurf zuerst speichern oder verwerfen' : undefined}
            onClick={() => onDelegate(state.revision ?? 0)}>
            Delegieren
          </Button>
        )}
        {state.canAssign && (
          <Button size="sm" variant="secondary" icon="manage_accounts" disabled={transferBlocked}
            title={draftDirty ? 'Entwurf zuerst speichern oder verwerfen' : undefined}
            onClick={() => onAssign(state.revision ?? 0)}>
            Zuweisen
          </Button>
        )}
        {state.canClaim && (
          <Button size="sm" variant="primary" icon="touch_app" loading={busy}
            onClick={() => onClaim(state.revision ?? 0)}>
            Übernehmen
          </Button>
        )}
      </div>

      {draftDirty && (state.canRelease || state.canAssign || state.canDelegate) && (
        <p className="text-wait basis-full m-0 flex items-center gap-1.5 text-[12px]" role="status">
          <Icon name="warning" size={15} />
          Entwurf zuerst speichern oder verwerfen, bevor du die Aufgabe übergibst.
        </p>
      )}
    </section>
  );
}
