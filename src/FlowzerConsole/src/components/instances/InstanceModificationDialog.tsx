import { useState } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/Icon';
import { Modal } from '@/components/ui/Modal';
import { InlineSpinner } from '@/components/ui/States';
import { ApiError } from '@/lib/api/client';
import { useInstanceModificationPreview, useModifyInstance } from '@/lib/api/queries';
import type {
  InstanceModificationFindingDto,
  InstanceModificationRequestDto,
  ProcessInstanceInfoDto,
} from '@/lib/api/types';
import { formatVersion, shortId } from '@/lib/format';
import {
  changedVariables,
  modificationErrorProblems,
  modificationFindingText,
  modificationMoves,
  parseVariableDraft,
} from '@/lib/instanceModification';
import { processScopeVariables } from '@/lib/instanceView';

import { MigrationRetryNotice } from './MigrationRetryNotice';
import { ModificationStepMapping } from './ModificationStepMapping';
import { ModificationVariablesEditor } from './ModificationVariablesEditor';

interface InstanceModificationDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  instance: ProcessInstanceInfoDto;
}

/** Die leere Anfrage beantwortet nur, welche Schritte warten und wohin sie dürfen. */
const EMPTY_REQUEST: InstanceModificationRequestDto = {};

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unbekannter Fehler.';
}

/**
 * Eingriff in eine laufende Instanz: wartende Schritte verschieben und Variablen korrigieren.
 *
 * Der Dialog fragt in zwei Schritten. Zuerst wird ausgewählt; erst „Weiter“ prüft genau
 * diese Anfrage folgenlos und nennt, was sie kostet — verfallende Aufgaben, abgebrochene
 * Aufträge, neu rechnende Timer. Ausgeführt wird erst danach und erst nach ausdrücklicher
 * Bestätigung, denn zurücknehmen lässt sich das nicht. Jede Änderung an der Auswahl nimmt
 * die Bestätigung zurück: Sie galt der geprüften Anfrage und nicht der nächsten.
 */
export function InstanceModificationDialog({
  open,
  onOpenChange,
  instance,
}: InstanceModificationDialogProps) {
  const variables = processScopeVariables(instance);
  const [selection, setSelection] = useState<Record<string, string>>({});
  // Der Anfangswert wird einmal gebildet, nicht bei jedem Rendern: Sonst verwürfe jede
  // Aktualisierung der Instanz, was gerade getippt wurde.
  const [draft, setDraft] = useState(() => JSON.stringify(processScopeVariables(instance), null, 2));
  const [removals, setRemovals] = useState<string[]>([]);
  const [confirming, setConfirming] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);

  const draftResult = parseVariableDraft(draft);
  const set = changedVariables(variables, draftResult.variables);
  const moves = modificationMoves(selection);
  const request: InstanceModificationRequestDto = { moves, variables: { set, remove: removals } };
  const hasChange = moves.length > 0 || set !== undefined || removals.length > 0;

  const stepsQuery = useInstanceModificationPreview(instance.instanceId, EMPTY_REQUEST);
  const checkQuery = useInstanceModificationPreview(
    confirming ? instance.instanceId : undefined,
    request,
  );
  const modify = useModifyInstance();

  // Scheitert eine Prüfung, gilt auch der zuvor gezeigte Stand nicht mehr.
  const steps = stepsQuery.error ? undefined : stepsQuery.data;
  const check = checkQuery.error ? undefined : checkQuery.data;

  // Endet die Instanz zwischen Prüfung und Eingriff, antwortet die API mit 409. Das erklärt
  // der Dialog selbst; ein Toast dazu wäre doppelt und nach dem Schließen nicht mehr lesbar.
  const conflict = modify.error instanceof ApiError && modify.error.status === 409;
  // Eine abgelehnte Ausführung (422) trägt dieselben Befunde wie der Trockenlauf. Sie
  // gehören an dieselbe Stelle wie dort und nicht in einen Toast, der wieder verschwindet.
  const rejected = modificationErrorProblems(modify.error);

  function handleOpenChange(next: boolean) {
    if (!next) modify.reset();
    onOpenChange(next);
  }

  /** Jede Änderung der Anfrage nimmt die Zustimmung zurück — sie galt der geprüften Anfrage. */
  function rearm() {
    setAcknowledged(false);
    if (modify.error) modify.reset();
  }

  function chooseTarget(tokenId: string, targetFlowNodeId: string) {
    if ((selection[tokenId] ?? '') === targetFlowNodeId) return;
    rearm();
    setSelection((previous) => ({ ...previous, [tokenId]: targetFlowNodeId }));
  }

  function toggleRemoval(name: string, remove: boolean) {
    rearm();
    setRemovals((previous) =>
      remove ? [...previous.filter((entry) => entry !== name), name] : previous.filter((entry) => entry !== name),
    );
  }

  function changeDraft(next: string) {
    rearm();
    setDraft(next);
  }

  function execute() {
    modify.mutate(
      { instanceId: instance.instanceId, request },
      {
        onSuccess: () => {
          toast.success('Instanz angepasst');
          handleOpenChange(false);
        },
        onError: (error) => {
          // Konflikt und abgelehnter Plan stehen im Dialog; alles andere bliebe dort
          // ungesehen, weil der Dialog danach weiter auf die Bestätigung wartet.
          if (error instanceof ApiError && error.status === 409) return;
          if (modificationErrorProblems(error).length > 0) return;
          toast.error('Instanz konnte nicht angepasst werden', { description: errorMessage(error) });
        },
      },
    );
  }

  // Nach einer Ablehnung bleibt die Schaltfläche gesperrt, bis sich an der Anfrage etwas
  // ändert: Dieselbe Anfrage noch einmal zu senden brächte dieselbe Ablehnung.
  const canExecute =
    Boolean(check?.applicable)
    && hasChange
    && acknowledged
    && !checkQuery.isFetching
    && !conflict
    && rejected.length === 0;

  return (
    <Modal
      open={open}
      onOpenChange={handleOpenChange}
      icon="edit"
      title="Instanz anpassen"
      className="w-[min(680px,calc(100vw-32px))]"
      description={
        <>
          „{instance.relatedDefinitionName}“ {formatVersion(instance.definitionVersion)} · #
          {shortId(instance.instanceId)} — wartende Schritte verschieben und Variablen
          korrigieren, ohne die Version zu wechseln.
        </>
      }
      footer={
        confirming ? (
          <>
            <Button
              size="sm"
              icon="arrow_back"
              disabled={modify.isPending}
              onClick={() => {
                setConfirming(false);
                setAcknowledged(false);
              }}
            >
              Zurück
            </Button>
            <Button
              size="sm"
              variant="primary"
              icon="edit"
              loading={modify.isPending}
              disabled={!canExecute}
              onClick={execute}
            >
              Instanz anpassen
            </Button>
          </>
        ) : (
          <>
            <Button size="sm" onClick={() => handleOpenChange(false)}>
              Abbrechen
            </Button>
            <Button
              size="sm"
              variant="primary"
              icon="arrow_forward"
              disabled={!hasChange || Boolean(draftResult.error) || !steps}
              title={hasChange ? undefined : 'Wähle ein Ziel oder ändere eine Variable.'}
              onClick={() => setConfirming(true)}
            >
              Weiter
            </Button>
          </>
        )
      }
    >
      <div className="pb-3">
        {stepsQuery.isPending && <InlineSpinner label="Eingriff wird vorbereitet …" />}

        {stepsQuery.error && (
          <MigrationRetryNotice
            icon="error"
            title="Eingriff konnte nicht vorbereitet werden"
            description={errorMessage(stepsQuery.error)}
            onRetry={() => void stepsQuery.refetch()}
          />
        )}

        {steps && !confirming && (
          <>
            <ModificationStepMapping
              steps={steps.steps}
              targets={steps.targets}
              selection={selection}
              onChoose={chooseTarget}
            />
            <ModificationVariablesEditor
              draft={draft}
              onDraftChange={changeDraft}
              error={draftResult.error}
              names={Object.keys(variables)}
              removals={removals}
              onToggleRemoval={toggleRemoval}
            />
          </>
        )}

        {confirming && (
          <>
            {checkQuery.isPending && <InlineSpinner label="Eingriff wird geprüft …" />}

            {checkQuery.error && (
              <MigrationRetryNotice
                icon="error"
                title="Eingriff konnte nicht geprüft werden"
                description={errorMessage(checkQuery.error)}
                onRetry={() => void checkQuery.refetch()}
              />
            )}

            {conflict && (
              <MigrationRetryNotice
                icon="warning"
                title="Die Instanz läuft nicht mehr."
                description="Inzwischen wurde sie beendet oder abgebrochen; der Eingriff ist unterblieben. Schließe den Dialog und sieh dir den aktuellen Stand an."
                onRetry={() => void checkQuery.refetch()}
              />
            )}

            {rejected.length > 0 && (
              <div className="border-border mb-4 rounded-[var(--r)] border p-3">
                <h3 className="text-fail m-0 mb-2 text-[13.5px] font-semibold">
                  Der Eingriff wurde abgelehnt. Die Instanz ist unverändert.
                </h3>
                <FindingList problems={rejected} notices={[]} />
              </div>
            )}

            {check && (
              <>
                <FindingList problems={check.problems} notices={check.notices} />

                <div className="border-border mt-4 border-t pt-3.5">
                  <p className="m-0 text-[13.5px]">
                    {check.applicable
                      ? `${moveSummary(moves.length)}${variableSummary(set, removals)}`
                      : 'So lässt sich der Eingriff nicht ausführen. Geh zurück und ändere die Auswahl.'}
                  </p>
                  {check.applicable && (
                    <label className="mt-3 flex cursor-pointer items-start gap-2.5 text-[13.5px]">
                      <input
                        type="checkbox"
                        checked={acknowledged}
                        onChange={(event) => setAcknowledged(event.target.checked)}
                        className="accent-accent mt-0.5 h-4 w-4 cursor-pointer"
                      />
                      Mir ist bewusst, dass sich der Eingriff nicht rückgängig machen lässt und
                      dass verschobene Aufgaben mit neuer Kennung neu beginnen.
                    </label>
                  )}
                </div>
              </>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}

/** „1 Schritt wird verschoben“ — die Einzahl gehört zum deutschen Satz dazu. */
function moveSummary(count: number): string {
  if (count === 0) return 'Es wird kein Schritt verschoben';
  return count === 1 ? '1 Schritt wird verschoben' : `${count} Schritte werden verschoben`;
}

/** Was an den Variablen geschieht — als Fortsetzung des Satzes über die Schritte. */
function variableSummary(
  set: Record<string, unknown> | undefined,
  removals: string[],
): string {
  const parts: string[] = [];
  if (set) parts.push(`${Object.keys(set).length} gesetzt`);
  if (removals.length > 0) parts.push(`${removals.length} entfernt`);
  if (parts.length === 0) return '; Variablen bleiben unverändert.';

  return `; Variablen: ${parts.join(', ')}.`;
}

/**
 * Hindernisse und Hinweise des Trockenlaufs.
 *
 * Beide tragen verschiedene Symbole und Farben, denn nur die Farbe unterscheidet sie für
 * einen Teil der Belegschaft nicht — und der Unterschied ist wesentlich: Ein Hindernis
 * verhindert den Eingriff, ein Hinweis nennt nur seinen Preis.
 */
function FindingList({
  problems,
  notices,
}: {
  problems: InstanceModificationFindingDto[];
  notices: InstanceModificationFindingDto[];
}) {
  if (problems.length === 0 && notices.length === 0) {
    return (
      <p className="text-muted m-0 text-[13px]">
        Der Eingriff nimmt nichts mit, was die Prüfung nennen müsste.
      </p>
    );
  }

  return (
    <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
      {problems.map((finding, index) => (
        <li key={`problem-${index}`} className="text-muted flex gap-2 text-[13px]">
          <Icon name="error" size={17} className="text-fail mt-px flex-none" />
          <span>{modificationFindingText(finding)}</span>
        </li>
      ))}
      {notices.map((finding, index) => (
        <li key={`notice-${index}`} className="text-muted flex gap-2 text-[13px]">
          <Icon name="info" size={17} className="text-wait mt-px flex-none" />
          <span>{modificationFindingText(finding)}</span>
        </li>
      ))}
    </ul>
  );
}
