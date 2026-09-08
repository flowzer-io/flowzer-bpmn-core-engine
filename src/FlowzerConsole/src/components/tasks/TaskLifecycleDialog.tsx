import { useEffect, useId, useState } from 'react';

import {
  DirectorySubjectPicker,
  type DirectorySubjectSelection,
} from '@/components/bpmn/properties/DirectorySubjectPicker';
import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import { ApiError } from '@/lib/api/client';
import type { UserTaskLifecycleCommand } from '@/lib/api/queries';

type DialogAction = 'release' | 'assign' | 'delegate';

interface TaskLifecycleDialogProps {
  open: boolean;
  action: DialogAction;
  taskId: string;
  /** Wird von der öffnenden Aktion eingefangen und nicht durch Polling ersetzt. */
  expectedRevision: number;
  busy: boolean;
  error: unknown;
  onOpenChange: (open: boolean) => void;
  onSubmit: (command: UserTaskLifecycleCommand) => void;
  onUseCurrentRevision?: () => void;
}

const TITLES: Record<DialogAction, string> = {
  release: 'Aufgabe zurückgeben',
  assign: 'Bearbeiter zuweisen',
  delegate: 'Aufgabe delegieren',
};

const SUBMIT_LABELS: Record<DialogAction, string> = {
  release: 'Zurückgeben',
  assign: 'Zuweisen',
  delegate: 'Delegieren',
};

/** Dialog für begründete Besitzwechsel; Eingaben bleiben bei Serverkonflikten erhalten. */
export function TaskLifecycleDialog({
  open,
  action,
  taskId,
  expectedRevision,
  busy,
  error,
  onOpenChange,
  onSubmit,
  onUseCurrentRevision,
}: TaskLifecycleDialogProps) {
  const reasonId = useId();
  const [reason, setReason] = useState('');
  const [selected, setSelected] = useState<DirectorySubjectSelection[]>([]);

  useEffect(() => {
    if (!open) return;
    setReason('');
    setSelected([]);
  }, [open, action, taskId]);

  const transfer = action !== 'release';
  const valid = reason.trim().length > 0 && (!transfer || selected.length === 1);

  function submit() {
    const trimmedReason = reason.trim();
    if (!valid) return;
    if (action === 'release') {
      onSubmit({ action, userTaskId: taskId, expectedRevision, reason: trimmedReason });
      return;
    }
    const assignee = selected[0]?.subject;
    if (!assignee || assignee.kind !== 'user') return;
    onSubmit({ action, userTaskId: taskId, expectedRevision, assignee, reason: trimmedReason });
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={TITLES[action]}
      icon={action === 'release' ? 'undo' : 'manage_accounts'}
      description={description(action)}
      footer={
        <>
          <Button size="sm" disabled={busy} onClick={() => onOpenChange(false)}>
            Abbrechen
          </Button>
          <Button size="sm" variant="primary" icon={action === 'release' ? 'undo' : 'person_add'}
            loading={busy} disabled={!valid} onClick={submit}>
            {SUBMIT_LABELS[action]}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4 pb-4">
        {Boolean(error) && (
          <div className="border-wait bg-wait/10 flex flex-wrap items-center justify-between gap-2 rounded-[var(--r-sm)] border px-3 py-2.5 text-[12.5px]" role="alert">
            <span>{errorMessage(error)}</span>
            {error instanceof ApiError && error.status === 409 && onUseCurrentRevision && (
              <Button size="sm" variant="secondary" icon="refresh" onClick={onUseCurrentRevision}>
                Aktuellen Stand verwenden
              </Button>
            )}
          </div>
        )}

        {transfer && (
          <DirectorySubjectPicker
            definitionId=""
            taskAssignee={{ taskId, action }}
            kind="user"
            selected={selected}
            multiple={false}
            disabled={busy}
            label="Neuer Bearbeiter"
            onChange={setSelected}
          />
        )}

        <div>
          <FieldLabel htmlFor={reasonId}>Begründung</FieldLabel>
          <TextInput id={reasonId} value={reason} disabled={busy} maxLength={500}
            placeholder={action === 'release' ? 'Warum geht die Aufgabe zurück ins Team?' : 'Warum wird die Aufgabe übergeben?'}
            onChange={(event) => setReason(event.target.value)} />
        </div>
      </div>
    </Modal>
  );
}

function description(action: DialogAction): string {
  if (action === 'release') {
    return 'Die Aufgabe wird wieder für berechtigte Kandidaten geöffnet. Dein privater Entwurf wird nicht übertragen.';
  }
  if (action === 'delegate') {
    return 'Nur ein aktiver, modellierter Kandidat kann übernehmen. Dein privater Entwurf wird nicht übertragen.';
  }
  return 'Der Betrieb weist die Aufgabe unmittelbar einem aktiven Benutzer zu. Der Grund wird protokolliert.';
}

function errorMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 409) {
    return 'Die Aufgabe wurde zwischenzeitlich geändert. Prüfe den aktualisierten Zustand und versuche es erneut.';
  }
  if (error instanceof ApiError && error.status === 503) {
    return 'Das Benutzerverzeichnis ist derzeit nicht verfügbar. Die Aufgabe wurde nicht verändert.';
  }
  return error instanceof Error ? error.message : 'Die Aufgabe konnte nicht verändert werden.';
}
