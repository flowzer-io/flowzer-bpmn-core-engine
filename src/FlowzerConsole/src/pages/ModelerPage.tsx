import { useNavigate } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import { toast } from 'sonner';

import { BpmnModeler, type BpmnModelerHandle } from '@/components/bpmn/BpmnModeler';
import { Button } from '@/components/ui/Button';
import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { ErrorState, InlineSpinner } from '@/components/ui/States';
import {
  useDefinitionXml,
  useDefinitions,
  useDeployDefinition,
  useLatestDefinition,
  useSaveDefinition,
  useStartInstance,
  useUpdateDefinitionMeta,
} from '@/lib/api/queries';
import { formatRelative } from '@/lib/format';
import { useBreadcrumbs } from '@/stores/breadcrumbs';
import { useCan } from '@/stores/session';

interface ModelerPageProps {
  definitionId: string;
}

/**
 * Modellierungsseite eines Workflows: bpmn-js mit Camunda-8-Eigenschaften-Panel,
 * plus Speichern (neue Version) und Deployen (Version aktivieren).
 */
export function ModelerPage({ definitionId }: ModelerPageProps) {
  const navigate = useNavigate();
  const modelerRef = useRef<BpmnModelerHandle>(null);

  const definitionsQuery = useDefinitions();
  const latestQuery = useLatestDefinition(definitionId);
  const xmlQuery = useDefinitionXml(latestQuery.data?.id);

  const saveDefinition = useSaveDefinition();
  const deployDefinition = useDeployDefinition();
  // Lesen darf jeder Zugelassene; Veroeffentlichen verlangt die Modelliererrolle.
  // Was die API ablehnen wuerde, bietet die Oberflaeche gar nicht erst an.
  const mayPublish = useCan()('modeler');
  const updateMeta = useUpdateDefinitionMeta();
  const startInstance = useStartInstance();

  const [dirty, setDirty] = useState(false);
  const [zoom, setZoom] = useState(100);
  const [renaming, setRenaming] = useState(false);

  const definition = definitionsQuery.data?.find((entry) => entry.definitionId === definitionId);
  const name = definition?.name ?? definitionId;

  useBreadcrumbs([{ label: 'Workflows', to: '/workflows' }, { label: name }]);

  // Nach dem Laden eines anderen Diagramms gilt der Editor wieder als unverändert.
  useEffect(() => setDirty(false), [xmlQuery.data]);

  // Ungespeicherte Änderungen sollen beim Verlassen des Tabs nicht still verloren gehen.
  useEffect(() => {
    if (!dirty) return;
    const onBeforeUnload = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [dirty]);

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

  async function handleSave() {
    const xml = await currentXml();
    if (!xml) return;

    saveDefinition.mutate(
      { xml, previousGuid: latestQuery.data?.id },
      {
        onSuccess: (saved) => {
          setDirty(false);
          toast.success(`Version v${saved.version.major}.${saved.version.minor} gespeichert`);
          void latestQuery.refetch();
        },
        onError: (error) =>
          toast.error('Speichern fehlgeschlagen', {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
  }

  async function handleDeploy() {
    const xml = await currentXml();
    if (!xml) return;

    deployDefinition.mutate(
      { xml, previousGuid: latestQuery.data?.id },
      {
        onSuccess: (deployed) => {
          setDirty(false);
          toast.success(`v${deployed.version.major}.${deployed.version.minor} ist aktiv`, {
            description: 'Neue Instanzen laufen ab sofort gegen diese Version.',
          });
          void latestQuery.refetch();
        },
        onError: (error) =>
          toast.error('Deploy fehlgeschlagen', {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
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

  if (latestQuery.error) {
    return (
      <div className="p-8">
        <ErrorState error={latestQuery.error} onRetry={() => void latestQuery.refetch()} />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-hidden">
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
          {renaming ? (
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
            <Chip tone={isDeployedLatest ? 'done' : dirty ? 'wait' : 'muted'}>
              {dirty
                ? 'Ungespeicherte Änderungen'
                : isDeployedLatest
                  ? `Aktiv · v${latestVersion.major}.${latestVersion.minor}`
                  : `Entwurf · v${latestVersion.major}.${latestVersion.minor}`}
            </Chip>
          )}
        </div>

        <span className="flex-1" />

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

        {definition?.deployedId && (
          <Button
            size="sm"
            icon="rocket_launch"
            loading={startInstance.isPending}
            onClick={() =>
              startInstance.mutate(definitionId, {
                onSuccess: (instance) =>
                  toast.success('Instanz gestartet', {
                    action: {
                      label: 'Öffnen',
                      onClick: () => void navigate({ to: `/instances/${instance.instanceId}` }),
                    },
                  }),
                onError: (error) =>
                  toast.error('Start fehlgeschlagen', {
                    description: error instanceof Error ? error.message : undefined,
                  }),
              })
            }
          >
            Starten
          </Button>
        )}

        {mayPublish ? (
          <>
            <Button size="sm" icon="save" loading={saveDefinition.isPending} onClick={() => void handleSave()}>
              Speichern
            </Button>

            <Button
              size="sm"
              variant="primary"
              icon="rocket_launch"
              loading={deployDefinition.isPending}
              onClick={() => void handleDeploy()}
            >
              Deployen
            </Button>
          </>
        ) : (
          <Chip tone="muted">Nur Ansicht</Chip>
        )}
      </div>

      {xmlQuery.isPending ? (
        <div className="grid flex-1 place-items-center">
          <InlineSpinner label="Diagramm wird geladen …" />
        </div>
      ) : (
        <BpmnModeler
          ref={modelerRef}
          xml={xmlQuery.data}
          onChange={() => setDirty(true)}
          onZoomChange={setZoom}
        />
      )}
    </div>
  );
}
