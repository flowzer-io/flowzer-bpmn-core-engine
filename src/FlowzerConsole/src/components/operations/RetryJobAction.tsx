import { useState } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import { useRetryJob } from '@/lib/api/queries';
import type { OperationsIncidentDto, ProcessVariables } from '@/lib/api/types';
import {
  changedCorrections,
  formatCorrectionDraft,
  MAX_RETRIES,
  MIN_RETRIES,
  parseCorrections,
  parseRetries,
} from '@/lib/jobRetry';

interface RetryJobActionProps {
  incident: OperationsIncidentDto;
  /** Kompakte Darstellung in einer Listenzeile. */
  size?: 'sm' | 'md';
}

/**
 * Gibt einen liegen gebliebenen Auftrag wieder frei — die Betriebsantwort auf eine Störung.
 *
 * Der Dialog bietet beides zusammen an, weil es zusammengehört: Wer einen Auftrag erneut laufen
 * lässt, dessen letzter Versuch an einer falschen Eingabe gescheitert ist, will genau diese
 * Eingabe vorher ändern. Ohne die Korrektur wäre die Freigabe nur ein zweiter Fehlschlag.
 */
export function RetryJobAction({ incident, size = 'sm' }: RetryJobActionProps) {
  const [open, setOpen] = useState(false);
  const retryJob = useRetryJob();

  if (!incident.jobId) return null;
  const jobId = incident.jobId;

  return (
    <>
      <Button size={size} icon="refresh" onClick={() => setOpen(true)}>
        Erneut freigeben
      </Button>

      {open && (
        <RetryJobDialog
          incident={incident}
          busy={retryJob.isPending}
          onDismiss={() => setOpen(false)}
          onConfirm={(retries, variables) =>
            retryJob.mutate(
              { jobId, retries, variables },
              {
                onSuccess: () => {
                  setOpen(false);
                  toast.success('Auftrag erneut freigegeben');
                },
                onError: (error) =>
                  toast.error('Auftrag konnte nicht freigegeben werden', {
                    description: error instanceof Error ? error.message : undefined,
                  }),
              },
            )
          }
        />
      )}
    </>
  );
}

interface RetryJobDialogProps {
  incident: OperationsIncidentDto;
  busy: boolean;
  onDismiss: () => void;
  onConfirm: (retries: number, variables?: ProcessVariables) => void;
}

function RetryJobDialog({ incident, busy, onDismiss, onConfirm }: RetryJobDialogProps) {
  // Der Anfangswert wird einmal gebildet, nicht bei jedem Rendern: Sonst verwürfe jede
  // Aktualisierung der Störungsliste, was gerade getippt wurde.
  const [retries, setRetries] = useState('1');
  const [corrections, setCorrections] = useState(() => formatCorrectionDraft(incident.variables));
  const [submitted, setSubmitted] = useState(false);

  const retriesResult = parseRetries(retries);
  const correctionResult = parseCorrections(corrections);
  const blocked = Boolean(retriesResult.error || correctionResult.error);

  return (
    <Modal
      open
      onOpenChange={(next) => !next && onDismiss()}
      icon="refresh"
      title="Auftrag erneut freigeben?"
      description={
        <>
          „{incident.flowNodeName ?? incident.flowNodeId}" in „{incident.definitionName}" bekommt
          wieder Versuche und wartet danach auf einen Worker. Der Schritt läuft dann ein weiteres
          Mal — mit allem, was er nach außen auslöst.
        </>
      }
      footer={
        <>
          <Button size="sm" onClick={onDismiss} disabled={busy}>
            Abbrechen
          </Button>
          <Button
            size="sm"
            variant="primary"
            icon="refresh"
            loading={busy}
            onClick={() => {
              setSubmitted(true);
              if (blocked || retriesResult.retries === undefined) return;
              onConfirm(retriesResult.retries, changedCorrections(incident.variables, correctionResult.variables));
            }}
          >
            Erneut freigeben
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4 pb-2">
        {incident.message && (
          <div className="border-border bg-surface-2 text-muted rounded-[var(--r-sm)] border px-3 py-2.5 text-[12.5px]">
            <span className="text-faint font-mono text-[11px] tracking-[0.06em] uppercase">
              Letzte Meldung
            </span>
            <div className="text-text mt-1 break-words">{incident.message}</div>
          </div>
        )}

        <div>
          <FieldLabel htmlFor="retry-attempts">Anzahl Versuche</FieldLabel>
          <TextInput
            id="retry-attempts"
            type="number"
            min={MIN_RETRIES}
            max={MAX_RETRIES}
            value={retries}
            onChange={(event) => setRetries(event.target.value)}
          />
          {submitted && retriesResult.error && (
            <div className="text-fail mt-1.5 text-[12.5px]">{retriesResult.error}</div>
          )}
        </div>

        <div>
          <FieldLabel htmlFor="retry-corrections">Eingaben korrigieren (JSON)</FieldLabel>
          <textarea
            id="retry-corrections"
            rows={8}
            spellCheck={false}
            value={corrections}
            onChange={(event) => setCorrections(event.target.value)}
            aria-invalid={correctionResult.error ? true : undefined}
            className={[
              'bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5',
              'font-mono text-[12.5px] outline-none focus:border-accent',
              correctionResult.error ? 'border-fail' : '',
            ].join(' ')}
            placeholder='{ "iban": "DE02120300000000202051" }'
          />
          <div className="text-faint mt-1.5 text-[12.5px]">
            Genannte Felder werden überschrieben, ungenannte bleiben stehen. Leer lassen, um nichts
            zu ändern.
          </div>
          {correctionResult.error && (
            <div className="text-fail mt-1.5 text-[12.5px]">{correctionResult.error}</div>
          )}
        </div>
      </div>
    </Modal>
  );
}
