import { useWorkflowEditor } from '@/components/workflow-editor/useWorkflowEditor';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import { toast } from 'sonner';

import { BpmnModeler, type BpmnModelerHandle } from '@/components/bpmn/BpmnModeler';
import { BpmnDiagnosticsPanel } from '@/components/bpmn/BpmnDiagnosticsPanel';
import { Button } from '@/components/ui/Button';
import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { ConfirmModal } from '@/components/ui/Modal';
import { StartWorkflowDialog } from '@/components/workflows/StartWorkflowDialog';
import { useStartWorkflow } from '@/components/workflows/useStartWorkflow';
import {
  useDefinitions,
  useDeleteDefinition,
  useDeployDefinition,
  useLatestDefinition,
  useSaveDefinition,
  useUpdateDefinitionMeta,
  useValidateDefinition,
  useBpmnCapabilities,
} from '@/lib/api/queries';
import { formatRelative } from '@/lib/format';
import { normalizeBpmnDiagnostics, type BpmnDiagnostic } from '@/lib/modeling/diagnostics';
import { useBreadcrumbs } from '@/stores/breadcrumbs';
import { useCan } from '@/stores/session';

interface ModelerPageProps {
  definitionId: string;
  focusElementId?: string;
}

/**
 * Modellierungsseite eines Workflows: bpmn-js mit dem Eigenschaften-Panel der Konsole,
 * plus Speichern (neue Version) und Veröffentlichen (Version aktivieren).
 *
 * Ohne Modelliererrolle wird daraus eine Ansicht: Das Diagramm ist gesperrt, das Panel
 * zeigt seine Werte, nimmt aber keine an. Sonst entstünden Änderungen, die sich nicht
 * speichern lassen — und beim Verlassen der Seite eine Warnung davor.
 */
export function ModelerPage({ definitionId, focusElementId }: ModelerPageProps) {
  const navigate = useNavigate();
  const modelerRef = useRef<BpmnModelerHandle>(null);

  const definitionsQuery = useDefinitions();
  const latestQuery = useLatestDefinition(definitionId);
  const editing = useWorkflowEditor();
  const { registerCapture } = editing;
  const [inputXml] = useState(editing.draft.xml);

  const saveDefinition = useSaveDefinition();
  const deployDefinition = useDeployDefinition();
  const validateDefinition = useValidateDefinition();
  const capabilitiesQuery = useBpmnCapabilities();
  // Lesen darf jeder Zugelassene; Veroeffentlichen verlangt die Modelliererrolle.
  // Was die API ablehnen wuerde, bietet die Oberflaeche gar nicht erst an.
  const mayPublish = useCan()('modeler');
  const updateMeta = useUpdateDefinitionMeta();
  const startWorkflow = useStartWorkflow();
  const deleteDefinition = useDeleteDefinition();

  const dirty = editing.draft.dirty;
  const [zoom, setZoom] = useState(100);
  const [renaming, setRenaming] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [diagnostics, setDiagnostics] = useState<BpmnDiagnostic[]>([]);

  const definition = definitionsQuery.data?.find((entry) => entry.definitionId === definitionId);
  const name = definition?.name ?? definitionId;

  useBreadcrumbs([{ label: 'Workflows', to: '/workflows' }, { label: name }]);

  useEffect(() => registerCapture(() => {
    if (!modelerRef.current) throw new Error('Der Editor ist noch nicht bereit.');
    return modelerRef.current.getXml();
  }), [registerCapture]);

  const latestVersion = definition?.latestVersion;
  const deployedVersion = definition?.deployedVersion;
  const isDeployedLatest =
    Boolean(deployedVersion) &&
    deployedVersion?.major === latestVersion?.major &&
    deployedVersion?.minor === latestVersion?.minor;

  async function currentXml(): Promise<string | null> {
    try {
      return await modelerRef.current!.getXml();
    } catch (error) {
      toast.error('Das Diagramm konnte nicht gelesen werden', {
        description: error instanceof Error ? error.message : undefined,
      });
      return null;
    }
  }

  async function store(kind: 'save' | 'deploy') {
    await editing.runSave(async () => {
      // getXml beendet zunächst synchron eine laufende Diagramm-Beschriftung.
      const pendingXml = currentXml();
      const submittedRevision = editing.draft.revision;
      const xml = await pendingXml;
      if (!xml) return;
      try {
        if (kind === 'deploy') await validateDefinition.mutateAsync({ xml, deployment: true });
        const mutation = kind === 'deploy' ? deployDefinition : saveDefinition;
        // mutateAsync bleibt auch bei einem Ansichtswechsel zuverlässig auswertbar.
        const result = await mutation.mutateAsync({ xml, previousGuid: editing.draft.baseId });
        editing.saved(xml, result.id, submittedRevision);
        setDiagnostics([]);
        toast.success(kind === 'deploy'
          ? `v${result.version.major}.${result.version.minor} ist aktiv`
          : `Entwurf v${result.version.major}.${result.version.minor} gespeichert`);
        void latestQuery.refetch();
      } catch (error) {
        handleMutationError(kind === 'deploy' ? 'Veröffentlichen fehlgeschlagen' : 'Speichern fehlgeschlagen', error);
      }
    });
  }

  function handleMutationError(title: string, error: unknown) {
    const nextDiagnostics = normalizeBpmnDiagnostics(error);
    if (nextDiagnostics.length > 0) {
      setDiagnostics(nextDiagnostics);
      return;
    }

    // Unbekannte und alte Fehler bleiben bewusst als Toast sichtbar; nur der
    // versionierte BPMN-Vertrag darf ein scheinbar sicheres Sprungziel erzeugen.
    toast.error(title, {
      description: error instanceof Error ? error.message : undefined,
    });
  }

  function handleRename(nextName: string) {
    const trimmed = nextName.trim();
    setRenaming(false);
    if (!trimmed || trimmed === name) return;

    updateMeta.mutate(
      { definitionId, name: trimmed, description: definition?.description ?? null },
      {
        onSuccess: () => toast.success('Name gespeichert'),
        onError: (error) =>
          toast.error('Name konnte nicht gespeichert werden', {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
      <div className="border-border bg-surface flex flex-none flex-wrap items-center gap-3 gap-y-2.5 border-b px-[22px] py-2.5">
        <Button
          variant="ghost"
          size="sm"
          icon="arrow_back"
          title="Zurück zu den Workflows"
          className="border-border h-[34px] w-[34px] border px-0"
          onClick={() => void navigate({ to: '/workflows' })}
        >
          <span className="sr-only">Zurück</span>
        </Button>

        <div className="flex min-w-0 items-center gap-2.5">
          {!mayPublish ? (
            // Umbenennen geht über `PUT /definition/meta` — auch das verlangt die
            // Modelliererrolle. Als Schaltfläche führte der Name nur in eine Ablehnung.
            <span className="font-display truncate text-[16.5px] font-semibold">{name}</span>
          ) : renaming ? (
            <input
              autoFocus
              defaultValue={name}
              onBlur={(event) => handleRename(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') handleRename(event.currentTarget.value);
                if (event.key === 'Escape') setRenaming(false);
              }}
              className="bg-surface-2 border-border font-display text-text rounded-[var(--r-sm)] border px-2 py-1 text-[16.5px] font-semibold outline-none"
            />
          ) : (
            <button
              type="button"
              onClick={() => setRenaming(true)}
              title="Namen bearbeiten"
              className="font-display hover:text-accent cursor-pointer truncate border-none bg-transparent p-0 text-[16.5px] font-semibold"
            >
              {name}
            </button>
          )}

          {latestVersion && (
            <Chip className="w-[190px] shrink-0 justify-center" tone={dirty ? 'wait' : isDeployedLatest ? 'done' : 'muted'}>
              {dirty
                ? 'Ungespeicherte Änderungen'
                : isDeployedLatest
                  ? `Aktiv · v${latestVersion.major}.${latestVersion.minor}`
                  : `Entwurf · v${latestVersion.major}.${latestVersion.minor}`}
            </Chip>
          )}
        </div>

        <span className="flex-1" />

        <Button
          size="sm"
          icon="format_list_bulleted"
          title="Denselben Arbeitsstand als Gliederung bearbeiten"
          onClick={() => void navigate({ to: `/workflows/${encodeURIComponent(definitionId)}/gliederung` })}
        >
          Gliederung
        </Button>

        {latestQuery.data && (
          <span className="text-faint hidden font-mono text-[11.5px] xl:inline">
            gespeichert {formatRelative(latestQuery.data.savedOn)}
          </span>
        )}

        <div className="bg-surface-2 mr-1.5 flex items-center gap-0.5 rounded-[var(--r-sm)] p-[3px]">
          <button
            type="button"
            title="Verkleinern"
            onClick={() => modelerRef.current?.zoomOut()}
            className="text-muted hover:text-text grid h-7 w-7 cursor-pointer place-items-center rounded-md border-none bg-transparent"
          >
            <Icon name="remove" size={18} />
          </button>
          <button
            type="button"
            title="Auf Fenstergröße einpassen"
            onClick={() => modelerRef.current?.zoomReset()}
            className="text-muted hover:text-text min-w-[38px] cursor-pointer border-none bg-transparent text-center text-xs font-semibold"
          >
            {zoom}%
          </button>
          <button
            type="button"
            title="Vergrößern"
            onClick={() => modelerRef.current?.zoomIn()}
            className="text-muted hover:text-text grid h-7 w-7 cursor-pointer place-items-center rounded-md border-none bg-transparent"
          >
            <Icon name="add" size={18} />
          </button>
        </div>

        {/* Ohne Modelliererrolle gibt es nichts zurückzunehmen: Das Diagramm ist gesperrt. */}
        {mayPublish && (
          <>
            <Button
              size="sm"
              icon="undo"
              title="Rückgängig"
              className="w-[34px] px-0"
              onClick={() => modelerRef.current?.undo()}
            >
              <span className="sr-only">Rückgängig</span>
            </Button>
            <Button
              size="sm"
              icon="redo"
              title="Wiederherstellen"
              className="w-[34px] px-0"
              onClick={() => modelerRef.current?.redo()}
            >
              <span className="sr-only">Wiederherstellen</span>
            </Button>
          </>
        )}

        {definition?.deployedId && (
          <Button
            size="sm"
            icon="rocket_launch"
            loading={startWorkflow.isBusy(definitionId)}
            onClick={() => void startWorkflow.start({ definitionId, name })}
          >
            Starten
          </Button>
        )}

        {mayPublish ? (
          <>
            <Button
              size="sm"
              variant="danger"
              icon="delete"
              title="Workflow löschen"
              className="w-[34px] px-0"
              onClick={() => setConfirmDelete(true)}
            >
              <span className="sr-only">Löschen</span>
            </Button>

            <Button size="sm" icon="save" loading={editing.draft.saving} onClick={() => void store('save')}>
              Speichern
            </Button>

            <Button
              size="sm"
              variant="primary"
              icon="rocket_launch"
              loading={editing.draft.saving}
              onClick={() => void store('deploy')}
            >
              Veröffentlichen
            </Button>
          </>
        ) : (
          <Chip tone="muted">Nur Ansicht</Chip>
        )}
      </div>

      <BpmnDiagnosticsPanel
        diagnostics={diagnostics}
        contractVersion={capabilitiesQuery.data?.contractVersion}
        onSelectElement={(elementId) => modelerRef.current?.selectElement(elementId)}
      />
      <BpmnModeler
        ref={modelerRef}
        definitionId={definitionId}
        xml={inputXml}
        focusElementId={focusElementId}
        diagnostics={diagnostics}
        readOnly={!mayPublish}
        onChange={() => {
          editing.changed();
          // Ein neuer Modellierungsschritt macht den letzten Serverbefund potenziell
          // veraltet; die nächste Prüfung liefert den aktuellen Elementbezug.
          setDiagnostics([]);
        }}
        onZoomChange={setZoom}
      />

      <StartWorkflowDialog {...startWorkflow.dialog} />

      <ConfirmModal
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        destructive
        busy={deleteDefinition.isPending}
        title={`„${name}" löschen?`}
        description="Alle Versionen dieses Workflows, ihre Diagramme und die bereits beendeten Instanzen werden entfernt. Das lässt sich nicht rückgängig machen. Laufende Instanzen verhindern das Löschen."
        confirmLabel="Endgültig löschen"
        confirmIcon="delete"
        onConfirm={() =>
          deleteDefinition.mutate(definitionId, {
            onSuccess: () => {
              setConfirmDelete(false);
              editing.allowDiscard();
              toast.success(`„${name}" gelöscht`);
              void navigate({ to: '/workflows' });
            },
            onError: (error) =>
              toast.error('Workflow konnte nicht gelöscht werden', {
                description: error instanceof Error ? error.message : undefined,
              }),
          })
        }
      />
    </div>
  );
}
