import { useWorkflowEditor } from '@/components/workflow-editor/useWorkflowEditor';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useRef, useState } from 'react';
import { toast } from 'sonner';

import { BlockEditor } from '@/components/outline/BlockEditor';
import { OutlineIssues } from '@/components/outline/OutlineIssues';
import { ReadOnlyOverview } from '@/components/outline/ReadOnlyOverview';
import { OutlineView } from '@/components/outline/OutlineView';
import { BpmnDiagnosticsPanel } from '@/components/bpmn/BpmnDiagnosticsPanel';
import { Button } from '@/components/ui/Button';
import { Chip } from '@/components/ui/Chip';
import {
  useDefinitions,
  useDeployDefinition,
  useLatestDefinition,
  useSaveDefinition,
  useValidateDefinition,
  useBpmnCapabilities,
} from '@/lib/api/queries';
import { normalizeBpmnDiagnostics, normalizeBpmnWarnings, type BpmnDiagnostic } from '@/lib/modeling/diagnostics';
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
  const editing = useWorkflowEditor();
  const { registerCapture } = editing;
  const [inputXml] = useState(editing.draft.xml);
  const locallyEdited = useRef(false);
  const saveDefinition = useSaveDefinition();
  const deployDefinition = useDeployDefinition();
  const validateDefinition = useValidateDefinition();
  const capabilitiesQuery = useBpmnCapabilities();
  const mayPublish = useCan()('modeler');

  const [draft, setDraft] = useState<OutlineDocument | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const dirty = editing.draft.dirty;
  const [diagnostics, setDiagnostics] = useState<BpmnDiagnostic[]>([]);

  const definition = definitionsQuery.data?.find((entry) => entry.definitionId === definitionId);
  const name = definition?.name ?? definitionId;
  useBreadcrumbs([{ label: 'Workflows', to: '/workflows' }, { label: name }]);

  const source = useMemo(() => readOutline(inputXml), [inputXml]);

  // Ein neu geladenes Modell setzt die Bearbeitung zurueck.
  useEffect(() => {
    setDraft(source.document ?? null);
    setSelectedId(null);
  }, [source]);

  const written = useMemo(() => (draft ? writeOutlineXml(draft, 'draft') : undefined), [draft]);
  const issues = draft ? (written?.issues ?? []) : source.issues;
  const canSave = mayPublish && Boolean(written?.xml);

  useEffect(() => registerCapture(() => {
    if (!locallyEdited.current) return inputXml;
    if (!written?.xml) throw new Error('Diese Gliederung lässt sich noch nicht verlustfrei ins Diagramm übertragen. Bitte die angezeigten Strukturhinweise korrigieren.');
    return written.xml;
  }), [registerCapture, inputXml, written?.xml]);

  function apply(next: OutlineDocument) {
    setDraft(next);
    locallyEdited.current = true;
    editing.changed();
    setDiagnostics([]);
  }

  async function store(kind: 'save' | 'deploy') {
    const xml = written?.xml;
    if (!xml) return;
    await editing.runSave(async () => {
      const submittedRevision = editing.draft.revision;
      try {
        let warnings: BpmnDiagnostic[] = [];
        if (kind === 'deploy') {
          const validation = await validateDefinition.mutateAsync({ xml, deployment: true });
          warnings = normalizeBpmnWarnings(validation);
        }
        const mutation = kind === 'deploy' ? deployDefinition : saveDefinition;
        const result = await mutation.mutateAsync({ xml, previousGuid: editing.draft.baseId });
        editing.saved(xml, result.id, submittedRevision);
        // Hinweise bleiben nach dem Erfolg stehen; geblockt hat nichts davon.
        setDiagnostics(warnings.length > 0 ? warnings : normalizeBpmnWarnings(result));
        toast.success(kind === 'deploy'
          ? `v${result.version.major}.${result.version.minor} ist aktiv`
          : `Entwurf v${result.version.major}.${result.version.minor} gespeichert`);
        void latestQuery.refetch();
      } catch (error) { handleMutationError(kind, error); }
    });
  }

  function handleMutationError(kind: 'save' | 'deploy', error: unknown) {
    const nextDiagnostics = normalizeBpmnDiagnostics(error);
    if (nextDiagnostics.length > 0) {
      setDiagnostics(nextDiagnostics);
      return;
    }

    toast.error(kind === 'deploy' ? 'Veröffentlichen fehlgeschlagen' : 'Speichern fehlgeschlagen', {
      description: error instanceof Error ? error.message : undefined,
    });
  }

  const openDiagram = (elementId?: string) =>
    void navigate({
      to: `/workflows/${encodeURIComponent(definitionId)}`,
      search: elementId ? { element: elementId } : {},
    });

  function selectIssue(issue: { elementId?: string }) {
    if (!issue.elementId) return;
    if (draft && findBlock(draft.blocks, issue.elementId)) {
      setSelectedId(issue.elementId);
      return;
    }
    openDiagram(issue.elementId);
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
        <Chip className="w-[190px] shrink-0 justify-center" tone={dirty ? 'wait' : 'muted'}>{dirty ? 'Ungespeicherte Änderungen' : 'Gliederung'}</Chip>

        <span className="flex-1" />

        <Button size="sm" icon="account_tree" title="Denselben Arbeitsstand als Diagramm bearbeiten" onClick={() => openDiagram()}>
          Diagramm
        </Button>

        {mayPublish ? (
          <>
            <Button size="sm" icon="save" disabled={!canSave} loading={editing.draft.saving} onClick={() => void store('save')}>
              Speichern
            </Button>
            <Button
              size="sm"
              variant="primary"
              icon="rocket_launch"
              disabled={!canSave || hasBlocker(issues)}
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

      <div className="flex min-h-0 flex-1 overflow-hidden">
        <div className="min-w-0 flex-1 overflow-auto px-[22px] py-5">
          <div className="mx-auto flex max-w-[720px] flex-col gap-4">
            <BpmnDiagnosticsPanel
              diagnostics={diagnostics}
              contractVersion={capabilitiesQuery.data?.contractVersion}
              onSelectElement={(elementId) => selectIssue({ elementId })}
            />
            {!draft && inputXml && (
              <ReadOnlyOverview xml={inputXml} onOpen={(id) => openDiagram(id)} />
            )}
            {!draft ? (
              <details className="text-muted text-sm">
                <summary>Hinweise zur Bearbeitung im Diagramm</summary>
                <OutlineIssues
                  issues={issues}
                  outlineShown={Boolean(draft)}
                  onOpenDiagram={() => openDiagram()}
                  onSelectIssue={selectIssue}
                />
              </details>
            ) : (
              <OutlineIssues
                issues={issues}
                outlineShown={Boolean(draft)}
                onOpenDiagram={() => openDiagram()}
                onSelectIssue={selectIssue}
              />
            )}
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
              definitionId={definitionId}
              document={draft}
              block={selectedId ? findBlock(draft.blocks, selectedId) : undefined}
              editable={mayPublish}
              onChange={apply}
            />
          </aside>
        )}
      </div>
    </div>
  );
}
