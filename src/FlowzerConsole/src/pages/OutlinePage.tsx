import { useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useState } from 'react';
import { toast } from 'sonner';

import { BlockEditor } from '@/components/outline/BlockEditor';
import { OutlineIssues } from '@/components/outline/OutlineIssues';
import { OutlineView } from '@/components/outline/OutlineView';
import { Button } from '@/components/ui/Button';
import { Chip } from '@/components/ui/Chip';
import { ErrorState, InlineSpinner } from '@/components/ui/States';
import {
  useDefinitionXml,
  useDefinitions,
  useDeployDefinition,
  useLatestDefinition,
  useSaveDefinition,
} from '@/lib/api/queries';
import { findBlock, hasBlocker, type OutlineDocument } from '@/lib/outline/model';
import { readOutline } from '@/lib/outline/read';
import { writeOutlineXml } from '@/lib/outline/write';
import { useBreadcrumbs } from '@/stores/breadcrumbs';
import { useCan } from '@/stores/session';

interface OutlinePageProps {
  definitionId: string;
}

/**
 * Der Workflow als Gliederung — die zweite Oberflaeche neben dem Diagramm.
 *
 * Gespeichert wird nur, was die Gliederung vollstaendig abbildet. Was sie nicht
 * abbildet, steht als Meldung ueber der Liste, und der Weg ins Diagramm bleibt
 * offen. Siehe `docs/GLIEDERUNG-TEILMENGE.md`.
 */
export function OutlinePage({ definitionId }: OutlinePageProps) {
  const navigate = useNavigate();
  const definitionsQuery = useDefinitions();
  const latestQuery = useLatestDefinition(definitionId);
  const xmlQuery = useDefinitionXml(latestQuery.data?.id);
  const saveDefinition = useSaveDefinition();
  const deployDefinition = useDeployDefinition();
  const mayPublish = useCan()('modeler');

  const [draft, setDraft] = useState<OutlineDocument | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);

  const definition = definitionsQuery.data?.find((entry) => entry.definitionId === definitionId);
  const name = definition?.name ?? definitionId;
  useBreadcrumbs([{ label: 'Workflows', to: '/workflows' }, { label: name }]);

  const source = useMemo(() => readOutline(xmlQuery.data), [xmlQuery.data]);

  // Ein neu geladenes Modell setzt die Bearbeitung zurueck.
  useEffect(() => {
    setDraft(source.document ?? null);
    setSelectedId(null);
    setDirty(false);
  }, [source]);

  // Sobald eine Gliederung vorliegt, zaehlen die Meldungen des Schreibwegs: Sie
  // beschreiben genau das, was beim Speichern herauskaeme.
  // Wie im Modeller: ungespeicherte Aenderungen sollen beim Verlassen des Tabs
  // nicht still verloren gehen.
  useEffect(() => {
    if (!dirty) return;
    const onBeforeUnload = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [dirty]);

  const written = useMemo(() => (draft ? writeOutlineXml(draft) : undefined), [draft]);
  const issues = draft ? (written?.issues ?? []) : source.issues;
  const canSave = mayPublish && Boolean(written?.xml) && !hasBlocker(issues);

  function apply(next: OutlineDocument) {
    setDraft(next);
    setDirty(true);
  }

  function store(kind: 'save' | 'deploy') {
    if (!written?.xml) return;
    const mutation = kind === 'deploy' ? deployDefinition : saveDefinition;

    mutation.mutate(
      { xml: written.xml, previousGuid: latestQuery.data?.id },
      {
        onSuccess: (result) => {
          setDirty(false);
          toast.success(
            kind === 'deploy'
              ? `v${result.version.major}.${result.version.minor} ist aktiv`
              : `Version v${result.version.major}.${result.version.minor} gespeichert`,
          );
          void latestQuery.refetch();
        },
        onError: (error) =>
          toast.error(kind === 'deploy' ? 'Deploy fehlgeschlagen' : 'Speichern fehlgeschlagen', {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
  }

  const loading = latestQuery.isPending || (Boolean(latestQuery.data?.id) && xmlQuery.isPending);
  const openDiagram = () => void navigate({ to: `/workflows/${encodeURIComponent(definitionId)}` });

  if (latestQuery.error) {
    return (
      <div className="p-8">
        <ErrorState error={latestQuery.error} onRetry={() => void latestQuery.refetch()} />
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
      <div className="border-border bg-surface flex flex-none flex-wrap items-center gap-3 border-b px-[22px] py-2.5">
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

        <span className="font-display truncate text-[16.5px] font-semibold">{name}</span>
        <Chip tone={dirty ? 'wait' : 'muted'}>{dirty ? 'Ungespeicherte Änderungen' : 'Gliederung'}</Chip>

        <span className="flex-1" />

        <Button size="sm" icon="account_tree" onClick={openDiagram}>
          Diagramm
        </Button>

        {mayPublish ? (
          <>
            <Button size="sm" icon="save" disabled={!canSave} loading={saveDefinition.isPending} onClick={() => store('save')}>
              Speichern
            </Button>
            <Button
              size="sm"
              variant="primary"
              icon="rocket_launch"
              disabled={!canSave}
              loading={deployDefinition.isPending}
              onClick={() => store('deploy')}
            >
              Deployen
            </Button>
          </>
        ) : (
          <Chip tone="muted">Nur Ansicht</Chip>
        )}
      </div>

      {loading && (
        <div className="grid flex-1 place-items-center">
          <InlineSpinner label="Gliederung wird geladen …" />
        </div>
      )}

      {!loading && xmlQuery.error && (
        <div className="grid flex-1 place-items-center p-8">
          <ErrorState error={xmlQuery.error} onRetry={() => void xmlQuery.refetch()} />
        </div>
      )}

      {!loading && !xmlQuery.error && (
        <div className="flex min-h-0 flex-1 overflow-hidden">
          <div className="min-w-0 flex-1 overflow-auto px-[22px] py-5">
            <div className="mx-auto flex max-w-[720px] flex-col gap-4">
              <OutlineIssues issues={issues} onOpenDiagram={openDiagram} />
              {draft && (
                <OutlineView
                  document={draft}
                  selectedId={selectedId}
                  editable={mayPublish}
                  onSelect={setSelectedId}
                  onChange={apply}
                />
              )}
            </div>
          </div>

          {draft && (
            <aside className="border-border bg-surface w-[340px] flex-none overflow-auto border-l p-5 max-lg:hidden">
              <BlockEditor
                document={draft}
                block={selectedId ? findBlock(draft.blocks, selectedId) : undefined}
                editable={mayPublish}
                onChange={apply}
              />
            </aside>
          )}
        </div>
      )}
    </div>
  );
}
