import { useEffect, useMemo, useRef, useState } from 'react';
import { toast } from 'sonner';

import { DecisionDryRunDialog } from '@/components/decisions/DecisionDryRunDialog';
import { DmnEditor, type DmnEditorHandle } from '@/components/decisions/DmnEditor';
import { Button } from '@/components/ui/Button';
import { Card, EmptyState } from '@/components/ui/Card';
import { Chip } from '@/components/ui/Chip';
import { SearchInput, TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { ConfirmModal } from '@/components/ui/Modal';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { ErrorState, InlineSpinner, Skeleton } from '@/components/ui/States';
import { ApiError } from '@/lib/api/client';
import {
  useCreateDecision,
  useDecision,
  useDecisions,
  useDeleteDecision,
  useUpdateDecision,
} from '@/lib/api/queries';
import type { DecisionDefinition } from '@/lib/api/types';
import { cn } from '@/lib/cn';
import { newDecisionXml } from '@/lib/decisions/dmnTemplate';
import { formatTimestamp } from '@/lib/format';
import { useCan } from '@/stores/session';

/**
 * Der Entscheidungskatalog.
 *
 * Eine Entscheidungsdefinition ist eine ganze DMN-Datei; die einzelnen Entscheidungen darin
 * ruft ein Business-Rule-Task über ihre `decisionId` auf. Deshalb steht in der Liste nicht
 * nur der Dateiname, sondern auch, was die Datei enthält — sonst müsste jemand jede Datei
 * öffnen, um den Namen zu finden, den er im Modeler eintragen soll.
 *
 * Speichern legt immer eine neue Version an. Schon deployte Workflows bleiben an ihrer
 * Version; deshalb gibt es hier keinen Entwurfsstand wie bei den Formularen.
 */
export function DecisionsPage() {
  const can = useCan();
  const mayEdit = can('modeler');
  // Der Trockenlauf steht auch dem Betrieb offen: Er wertet nur aus und ändert nichts.
  const mayDryRun = mayEdit || can('operator');

  const decisionsQuery = useDecisions();
  const createDecision = useCreateDecision();
  const updateDecision = useUpdateDecision();
  const deleteDecision = useDeleteDecision();

  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [newName, setNewName] = useState('');
  const [dirty, setDirty] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<DecisionDefinition | null>(null);
  const [deleteBlocked, setDeleteBlocked] = useState<string | null>(null);
  const [dryRunOpen, setDryRunOpen] = useState(false);
  const editorRef = useRef<DmnEditorHandle>(null);
  const uploadRef = useRef<HTMLInputElement>(null);

  const decisions = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('de');
    return (decisionsQuery.data ?? [])
      .filter((entry) =>
        term.length === 0
        || entry.name.toLocaleLowerCase('de').includes(term)
        || entry.decisions.some((decision) =>
          decision.decisionId.toLocaleLowerCase('de').includes(term)
          || decision.name.toLocaleLowerCase('de').includes(term)))
      .sort((left, right) => left.name.localeCompare(right.name, 'de'));
  }, [decisionsQuery.data, search]);

  // Beim ersten Laden den ersten Eintrag auswählen, damit rechts nicht nur eine Aufforderung steht.
  useEffect(() => {
    if (!selectedId && decisions.length > 0) setSelectedId(decisions[0]!.decisionDefinitionId);
  }, [decisions, selectedId]);

  // Ein Filter darf rechts keinen Eintrag stehen lassen, den die Liste nicht mehr zeigt.
  useEffect(() => {
    if (selectedId && !decisions.some((entry) => entry.decisionDefinitionId === selectedId)) {
      setSelectedId(decisions[0]?.decisionDefinitionId ?? null);
    }
  }, [decisions, selectedId]);

  useEffect(() => {
    setDirty(false);
  }, [selectedId]);

  const selected = decisions.find((entry) => entry.decisionDefinitionId === selectedId);
  const detailQuery = useDecision(selectedId ?? undefined);

  function adopt(created: DecisionDefinition, message: string) {
    setSelectedId(created.decisionDefinitionId);
    setCreating(false);
    setNewName('');
    setDirty(false);
    toast.success(message);
  }

  function createFromTemplate() {
    const name = newName.trim();
    if (!name) return;

    createDecision.mutate(
      { name, xml: newDecisionXml(name) },
      {
        onSuccess: (created) => adopt(created, `Entscheidung „${created.name}" angelegt`),
        onError: (error) =>
          toast.error('Entscheidung konnte nicht angelegt werden', { description: describe(error) }),
      },
    );
  }

  async function uploadFile(file: File) {
    let xml: string;
    try {
      xml = await readTextFile(file);
    } catch (error) {
      toast.error('Die Datei konnte nicht gelesen werden', { description: describe(error) });
      return;
    }

    createDecision.mutate(
      { name: file.name.replace(/\.(dmn|xml)$/i, ''), xml },
      {
        onSuccess: (created) => adopt(created, `„${created.name}" hochgeladen`),
        onError: (error) =>
          toast.error('DMN-Datei konnte nicht übernommen werden', { description: describe(error) }),
      },
    );
  }

  async function saveNewVersion() {
    if (!selected || !editorRef.current) return;

    let xml: string;
    try {
      xml = await editorRef.current.getXml();
    } catch (error) {
      toast.error('Der Editor konnte das DMN nicht ausgeben', { description: describe(error) });
      return;
    }

    updateDecision.mutate(
      { decisionDefinitionId: selected.decisionDefinitionId, input: { name: selected.name, xml } },
      {
        onSuccess: (saved) => {
          setDirty(false);
          toast.success(`Version ${saved.version} gespeichert`);
        },
        onError: (error) => toast.error('Speichern fehlgeschlagen', { description: describe(error) }),
      },
    );
  }

  return (
    <PageContainer>
      <PageHeader
        title="Entscheidungen"
        description="DMN-Entscheidungstabellen, die ein Business-Rule-Task im Workflow aufruft."
        actions={mayEdit && (
          <>
            <Button icon="upload" onClick={() => uploadRef.current?.click()}>
              DMN hochladen
            </Button>
            <Button variant="primary" icon="add" onClick={() => setCreating((open) => !open)}>
              Neue Entscheidung
            </Button>
            <input
              ref={uploadRef}
              type="file"
              accept=".dmn,.xml,application/xml,text/xml"
              className="hidden"
              aria-label="DMN-Datei hochladen"
              onChange={(event) => {
                const file = event.target.files?.[0];
                // Das Feld wird geleert, damit dieselbe Datei erneut gewählt werden kann.
                event.target.value = '';
                if (file) void uploadFile(file);
              }}
            />
          </>
        )}
      />

      {creating && (
        <Card className="mb-4 flex items-center gap-2.5 p-3.5">
          <TextInput
            autoFocus
            value={newName}
            onChange={(event) => setNewName(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') createFromTemplate();
              if (event.key === 'Escape') setCreating(false);
            }}
            placeholder="Name der Entscheidung — daraus entsteht die Kennung für den Business-Rule-Task"
            aria-label="Name der Entscheidung"
            className="flex-1"
          />
          <Button variant="primary" loading={createDecision.isPending} onClick={createFromTemplate}>
            Anlegen
          </Button>
          <Button variant="ghost" onClick={() => setCreating(false)}>
            Abbrechen
          </Button>
        </Card>
      )}

      <div className="grid items-start gap-[22px] lg:grid-cols-[320px_minmax(0,1fr)]">
        <div className="flex flex-col gap-2">
          <SearchInput
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Entscheidung filtern …"
            aria-label="Entscheidung filtern"
            wrapperClassName="py-2 mb-1"
          />

          {decisionsQuery.isPending &&
            Array.from({ length: 3 }, (_, index) => <Skeleton key={index} className="h-[72px]" />)}

          {decisionsQuery.error && (
            <ErrorState error={decisionsQuery.error} onRetry={() => void decisionsQuery.refetch()} />
          )}

          {!decisionsQuery.isPending && !decisionsQuery.error && decisions.length === 0 && (
            <Card>
              <EmptyState
                icon="rule"
                title={search ? 'Kein Treffer' : 'Noch keine Entscheidungen'}
                description={
                  search
                    ? 'Passe den Suchbegriff an.'
                    : 'Lege eine Entscheidungstabelle aus der Vorlage an oder lade eine vorhandene DMN-Datei hoch.'
                }
              />
            </Card>
          )}

          {decisions.map((entry) => {
            const active = entry.decisionDefinitionId === selectedId;
            return (
              <button
                key={entry.decisionDefinitionId}
                type="button"
                onClick={() => setSelectedId(entry.decisionDefinitionId)}
                className={cn(
                  'flex w-full cursor-pointer flex-col gap-1.5 rounded-[var(--r)] border px-3 py-3 text-left',
                  'transition-[background-color,border-color] duration-150',
                  active ? 'border-accent bg-surface' : 'bg-surface border-border hover:border-accent',
                )}
              >
                <span className="flex items-center gap-2">
                  <Icon name="rule" size={18} className={active ? 'text-accent' : 'text-muted'} />
                  <span className="min-w-0 flex-1 truncate text-sm font-semibold">{entry.name}</span>
                  <Chip tone="muted">v{entry.version}</Chip>
                </span>
                <span className="text-muted flex flex-wrap gap-1 font-mono text-[11.5px]">
                  {entry.decisions.length === 0
                    ? 'Keine Entscheidung in dieser Datei'
                    : entry.decisions.map((decision) => (
                      <span key={decision.decisionId} className="bg-surface-2 rounded px-1.5 py-0.5">
                        {decision.name ? `${decision.name} · ${decision.decisionId}` : decision.decisionId}
                      </span>
                    ))}
                </span>
                {/* Die API liefert nur die Benutzerkennung; eine GUID sagt niemandem etwas. */}
                <span className="text-faint text-[11.5px]">
                  Zuletzt deployt {formatTimestamp(entry.deployedAt)}
                </span>
              </button>
            );
          })}
        </div>

        <Card className="flex min-h-[calc(100vh-220px)] flex-col overflow-hidden">
          <div className="border-border bg-surface-2 flex items-center justify-between gap-3 border-b px-[22px] py-3.5">
            <div className="flex min-w-0 items-center gap-2.5">
              <Icon name="rule" size={19} className="text-accent" />
              <div className="min-w-0">
                <div className="truncate text-[15px] font-semibold">
                  {selected?.name ?? 'Keine Entscheidung ausgewählt'}
                </div>
                <div className="text-muted text-xs">
                  {selected
                    ? `Version ${selected.version} · deployt ${formatTimestamp(selected.deployedAt)}${dirty ? ' · ungespeicherte Änderungen' : ''}`
                    : 'Wähle links eine Entscheidung.'}
                </div>
              </div>
            </div>

            <div className="flex flex-wrap items-center justify-end gap-2.5">
              {selected && mayDryRun && (
                <Button size="sm" icon="play_arrow" onClick={() => setDryRunOpen(true)}>
                  Trockenlauf
                </Button>
              )}
              {selected && mayEdit && (
                <Button
                  size="sm"
                  variant="primary"
                  icon="save"
                  loading={updateDecision.isPending}
                  disabled={!dirty}
                  onClick={() => void saveNewVersion()}
                >
                  Als neue Version speichern
                </Button>
              )}
              {selected && mayEdit && (
                <Button
                  size="sm"
                  variant="danger"
                  icon="delete"
                  className="w-[38px] px-0"
                  onClick={() => {
                    setDeleteBlocked(null);
                    setPendingDelete(selected);
                  }}
                >
                  <span className="sr-only">{selected.name} löschen</span>
                </Button>
              )}
            </div>
          </div>

          <div className="flex min-h-0 flex-1 flex-col">
            {!selectedId && (
              <EmptyState
                icon="ads_click"
                title="Entscheidung wählen"
                description="Wähle links eine Entscheidung, um sie anzusehen oder zu bearbeiten."
              />
            )}

            {selectedId && detailQuery.isPending && (
              <div className="p-4">
                <InlineSpinner />
              </div>
            )}

            {selectedId && detailQuery.error && (
              <ErrorState
                error={detailQuery.error}
                title="Entscheidung konnte nicht geladen werden"
                onRetry={() => void detailQuery.refetch()}
              />
            )}

            {selectedId && detailQuery.data && (
              <DmnEditor
                key={`${selectedId}-${detailQuery.data.version}`}
                ref={editorRef}
                xml={detailQuery.data.xml}
                readOnly={!mayEdit}
                onChange={() => setDirty(true)}
              />
            )}
          </div>
        </Card>
      </div>

      {selected && dryRunOpen && (
        <DecisionDryRunDialog
          open
          onOpenChange={setDryRunOpen}
          decisionDefinitionId={selected.decisionDefinitionId}
          decisions={selected.decisions}
        />
      )}

      <ConfirmModal
        open={pendingDelete !== null}
        onOpenChange={(open) => {
          if (!open) {
            setPendingDelete(null);
            setDeleteBlocked(null);
          }
        }}
        destructive
        busy={deleteDecision.isPending}
        title={`„${pendingDelete?.name ?? ''}" löschen?`}
        description="Alle Versionen dieser Entscheidung werden entfernt. Das lässt sich nicht rückgängig machen."
        confirmLabel="Endgültig löschen"
        confirmIcon="delete"
        onConfirm={() => {
          const target = pendingDelete;
          if (!target) return;
          deleteDecision.mutate(target.decisionDefinitionId, {
            onSuccess: () => {
              setPendingDelete(null);
              setDeleteBlocked(null);
              if (selectedId === target.decisionDefinitionId) setSelectedId(null);
              toast.success(`„${target.name}" gelöscht`);
            },
            onError: (error) => {
              // Die 409-Antwort nennt die Workflows, die die Entscheidung benutzen. Diese
              // Auskunft gehört in den Dialog und nicht in eine Meldung, die wegwischt.
              if (error instanceof ApiError && error.status === 409) {
                setDeleteBlocked(error.message);
                return;
              }
              setPendingDelete(null);
              toast.error('Entscheidung konnte nicht gelöscht werden', { description: describe(error) });
            },
          });
        }}
      >
        {deleteBlocked && (
          <div role="alert" className="border-fail text-fail mb-2 rounded-[var(--r)] border px-4 py-3 text-sm">
            <strong className="block">Die Entscheidung wird noch benutzt.</strong>
            {deleteBlocked}
          </div>
        )}
      </ConfirmModal>
    </PageContainer>
  );
}

function describe(error: unknown): string | undefined {
  return error instanceof Error ? error.message : undefined;
}

/**
 * Liest eine hochgeladene Datei als Text. Bewusst über `FileReader` statt `File.text()`:
 * Letzteres gibt es erst in neueren Umgebungen, und der Weg über den Reader meldet einen
 * Lesefehler als abgelehntes Versprechen statt als stillen leeren Inhalt.
 */
function readTextFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error('Die Datei konnte nicht gelesen werden.'));
    reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : '');
    reader.readAsText(file);
  });
}
