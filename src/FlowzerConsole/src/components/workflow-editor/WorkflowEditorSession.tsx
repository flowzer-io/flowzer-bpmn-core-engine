import { useBlocker } from '@tanstack/react-router';
import { useCallback, useEffect, useReducer, useRef, useState, type ReactNode } from 'react';
import { toast } from 'sonner';
import { useDefinitionXml, useLatestDefinition } from '@/lib/api/queries';
import { ErrorState, InlineSpinner } from '@/components/ui/States';
import { ConfirmModal } from '@/components/ui/Modal';
import { WorkflowDraft, isEditorPath } from './WorkflowDraft';
import { WorkflowEditorContext, type CaptureEditor } from './useWorkflowEditor';

/** Die gemeinsame Elternroute bleibt beim Wechsel Diagramm/Gliederung erhalten. */
export function WorkflowEditorSession({ definitionId, children }: { definitionId: string; children: ReactNode }) {
  const latest = useLatestDefinition(definitionId);
  const xml = useDefinitionXml(latest.data?.id);
  const [initial, setInitial] = useState<{ xml: string; baseId: string } | null>(null);
  useEffect(() => {
    // Hintergrund-Refetches dürfen einen geöffneten Arbeitsstand nie ersetzen.
    if (!initial && xml.data !== undefined && latest.data) setInitial({ xml: xml.data, baseId: latest.data.id });
  }, [initial, xml.data, latest.data]);
  if (!initial) {
    const error = latest.error ?? xml.error;
    return error
      ? <div className="p-8"><ErrorState error={error} onRetry={() => { void latest.refetch(); void xml.refetch(); }} /></div>
      : <div className="grid flex-1 place-items-center"><InlineSpinner label="Workflow wird geladen …" /></div>;
  }
  return <EditingSession definitionId={definitionId} initial={initial}>{children}</EditingSession>;
}

function EditingSession({ definitionId, initial, children }: {
  definitionId: string; initial: { xml: string; baseId: string }; children: ReactNode;
}) {
  const [draft] = useState(() => new WorkflowDraft(initial.xml, initial.baseId));
  const [, render] = useReducer((revision: number) => revision + 1, 0);
  const captureRef = useRef<CaptureEditor | null>(null);
  const pendingExit = useRef<((block: boolean) => void) | null>(null);
  const [confirmExit, setConfirmExit] = useState(false);
  const registerCapture = useCallback((capture: CaptureEditor) => {
    captureRef.current = capture;
    return () => { if (captureRef.current === capture) captureRef.current = null; };
  }, []);

  useEffect(() => () => { pendingExit.current?.(true); }, []);
  const beforeUnload = useCallback(() => draft.dirty || hasPendingInput(), [draft]);
  useBlocker({
    enableBeforeUnload: beforeUnload,
    shouldBlockFn: async ({ current, next }) => {
      // Eigenschaftenfelder bündeln Tastendrücke bis zum Blur. Browser-Zurück
      // muss diese Eingaben ebenso übernehmen wie der Klick auf den Ansichtsbutton.
      const pendingInput = hasPendingInput();
      if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
      if (pendingInput && !draft.dirty) draft.change();
      if (!draft.dirty || current.pathname === next.pathname) return false;
      if (isEditorPath(next.pathname, definitionId)) {
        try {
          if (!captureRef.current) throw new Error('Der Editor ist noch nicht bereit.');
          draft.capture(await captureRef.current());
          render();
          return false;
        } catch (error) {
          toast.error('Ansichtswechsel nicht möglich', {
            description: error instanceof Error ? error.message : 'Der Arbeitsstand konnte nicht übernommen werden.',
          });
          return true;
        }
      }
      // Kein automatisches Speichern beim Verlassen. Auch Browser-Zurück/Sidebar
      // durchlaufen denselben Router-Blocker; Abbrechen erhält die laufende Ansicht.
      pendingExit.current?.(true);
      return new Promise<boolean>((resolve) => { pendingExit.current = resolve; setConfirmExit(true); });
    },
  });

  function resolveExit(discard: boolean) {
    if (discard) draft.dirty = false;
    const resolve = pendingExit.current;
    pendingExit.current = null;
    setConfirmExit(false);
    resolve?.(!discard);
  }
  return (
    <WorkflowEditorContext.Provider value={{
      draft, registerCapture,
      runSave: async (action) => {
        // Ein laufender Schreibvorgang gehört zur Sitzung, nicht zur Ansicht.
        if (draft.saving) return;
        draft.saving = true; render();
        try { await action(); }
        finally { draft.saving = false; render(); }
      },
      changed: () => { draft.change(); render(); },
      saved: (xml, id, revision) => { draft.saved(xml, id, revision); render(); },
      allowDiscard: () => { draft.dirty = false; render(); },
    }}>
      {children}
      <ConfirmModal open={confirmExit} onOpenChange={(open) => { if (!open) resolveExit(false); }}
        title="Ungespeicherte Änderungen" description="Wenn du den Editor verlässt, gehen deine ungespeicherten Änderungen verloren. Möchtest du sie verwerfen?"
        destructive confirmLabel="Änderungen verwerfen" onConfirm={() => resolveExit(true)} />
    </WorkflowEditorContext.Provider>
  );
}

/** Auch ein noch fokussierter Feldentwurf muss beim Tab-Schließen geschützt sein. */
function hasPendingInput(): boolean {
  return Boolean(document.querySelector('[data-editor-pending="true"]'))
    || Array.from(document.querySelectorAll<HTMLElement>('.djs-direct-editing-content'))
      .some(element => element.offsetParent !== null);
}
