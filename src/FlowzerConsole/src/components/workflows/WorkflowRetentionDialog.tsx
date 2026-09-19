import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { FieldLabel } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';

/** Was mit beendeten Instanzen dieses Workflows geschehen soll. */
type RetentionMode = 'global' | 'never' | 'days';

interface WorkflowRetentionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workflowName: string;
  /** Aktueller Wert: `null` heißt „installationsweite Frist", `0` heißt „nie löschen". */
  retentionDays: number | null;
  busy?: boolean;
  onSubmit: (retentionDays: number | null) => void;
}

/**
 * Setzt die Aufbewahrungsfrist beendeter Instanzen eines Workflows.
 *
 * Die drei Fälle sind bewusst drei Optionen und kein Zahlenfeld mit Sonderbedeutungen: In
 * einem reinen Eingabefeld wären „leer", „0" und „30" drei Zustände, deren Unterschied man
 * raten müsste — und ausgerechnet die 0 bedeutet das Gegenteil dessen, was eine kleine Zahl
 * sonst bedeutet. Wer laut auswählt, was gelten soll, verkürzt keine Frist aus Versehen.
 */
export function WorkflowRetentionDialog({
  open,
  onOpenChange,
  workflowName,
  retentionDays,
  busy = false,
  onSubmit,
}: WorkflowRetentionDialogProps) {
  const [mode, setMode] = useState<RetentionMode>('global');
  const [days, setDays] = useState('30');

  useEffect(() => {
    if (!open) return;
    setMode(retentionDays === null ? 'global' : retentionDays === 0 ? 'never' : 'days');
    setDays(retentionDays !== null && retentionDays > 0 ? String(retentionDays) : '30');
  }, [open, retentionDays]);

  const parsedDays = Number(days);
  const daysValid = /^\d+$/.test(days.trim()) && parsedDays >= 1 && parsedDays <= 36_500;
  const valid = mode !== 'days' || daysValid;
  const next = mode === 'global' ? null : mode === 'never' ? 0 : parsedDays;
  const unchanged = next === retentionDays;

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={`Aufbewahrung für „${workflowName}“`}
      icon="schedule"
      description="Gilt nur für beendete Instanzen. Laufende Vorgänge werden nie gelöscht."
      footer={
        <>
          <Button size="sm" onClick={() => onOpenChange(false)} disabled={busy}>
            Abbrechen
          </Button>
          <Button
            size="sm"
            variant="primary"
            icon="schedule"
            loading={busy}
            disabled={!valid || unchanged}
            onClick={() => onSubmit(next)}
          >
            Speichern
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 pb-3">
        <label className="flex cursor-pointer items-start gap-2.5">
          <input
            type="radio"
            name="retention-mode"
            className="mt-1"
            checked={mode === 'global'}
            onChange={() => setMode('global')}
          />
          <span>
            <span className="text-[13.5px] font-semibold">Einstellung der Installation</span>
            <span className="text-muted block text-[12.5px]">
              Es gilt die Frist, die für alle Workflows konfiguriert ist.
            </span>
          </span>
        </label>

        <label className="flex cursor-pointer items-start gap-2.5">
          <input
            type="radio"
            name="retention-mode"
            className="mt-1"
            checked={mode === 'never'}
            onChange={() => setMode('never')}
          />
          <span>
            <span className="text-[13.5px] font-semibold">Nie löschen</span>
            <span className="text-muted block text-[12.5px]">
              Beendete Instanzen bleiben dauerhaft erhalten — für aufbewahrungspflichtige
              Vorgänge.
            </span>
          </span>
        </label>

        <label className="flex cursor-pointer items-start gap-2.5">
          <input
            type="radio"
            name="retention-mode"
            className="mt-1"
            checked={mode === 'days'}
            onChange={() => setMode('days')}
          />
          <span className="min-w-0 flex-1">
            <span className="text-[13.5px] font-semibold">Eigene Frist</span>
            <span className="text-muted block text-[12.5px]">
              Beendete Instanzen werden nach dieser Zeit endgültig gelöscht.
            </span>
            <span className="mt-2 block">
              <FieldLabel>Tage</FieldLabel>
              <input
                type="text"
                inputMode="numeric"
                aria-label="Aufbewahrung in Tagen"
                className="bg-surface-2 border-border text-text w-28 rounded-[var(--r-sm)] border px-3 py-2 text-[13.5px] outline-none focus:border-[var(--accent)]"
                value={days}
                disabled={mode !== 'days'}
                onChange={(event) => setDays(event.target.value)}
              />
              {mode === 'days' && !daysValid && (
                <span className="text-fail mt-1.5 block text-[12.5px]">
                  Eine ganze Zahl zwischen 1 und 36500.
                </span>
              )}
            </span>
          </span>
        </label>
      </div>
    </Modal>
  );
}
