import { FlowzerApiError, type ExtendedUserTask, type FlowzerForm } from '@flowzer/sdk';
import type { UserTaskActions } from '@flowzer/react';
import { useMemo, useRef, useState } from 'react';
import { toast } from 'sonner';

import type { BoundDirectorySubjectAdapter } from '@/components/bpmn/properties/DirectorySubjectPicker';
import { FormRenderer, type FormRendererHandle } from '@/components/forms/FormRenderer';
import { FormValidationErrors } from '@/components/forms/FormValidationErrors';
import { TaskDraftConflictBanner, TaskDraftStatus } from '@/components/tasks/TaskDraftStatus';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/Icon';
import { InlineSpinner } from '@/components/ui/States';
import { describeFormKey } from '@/lib/formKey';
import { getClientFormActions } from '@/lib/forms/formContractClient';
import type { TaskDraftEditor } from '@/lib/taskDraft';
import type { TaskView } from '@/lib/taskView';

interface TaskFormWorkspace {
  task: ExtendedUserTask;
  form: FlowzerForm | undefined;
  pending: boolean;
  error: Error | null;
  directoryAdapter: BoundDirectorySubjectAdapter | undefined;
}

interface TaskFormControls {
  draft: TaskDraftEditor;
  actions: UserTaskActions;
  lifecyclePending: boolean;
  taskRevision: number;
}

interface TaskFormCardProps {
  view: TaskView;
  workspace: TaskFormWorkspace;
  controls: TaskFormControls;
  onCompleted: () => void;
  onDefer: () => void;
}

/** Formular- und Abschlussbereich einer arbeitsberechtigten Human Task. */
export function TaskFormCard({ view, workspace, controls, onCompleted, onDefer }: TaskFormCardProps) {
  const formRef = useRef<FormRendererHandle>(null);
  const completionKeys = useRef(new Map<string, string>());
  const [submissionError, setSubmissionError] = useState<{
    taskId: string;
    error: unknown;
  } | null>(null);
  const { actions, draft, lifecyclePending, taskRevision } = controls;
  const formActions = useMemo(
    () => workspace.form?.formData ? getClientFormActions(workspace.form.formData) : [],
    [workspace.form?.formData],
  );

  async function complete(actionId?: string) {
    if (lifecyclePending || draft.isSaving || draft.isDiscarding || draft.loadState !== 'ready') return;

    const renderer = formRef.current;
    const action = actionId ? formActions.find((candidate) => candidate.id === actionId) : undefined;
    if (renderer && !await renderer.validate(action?.assignments)) {
      toast.error('Bitte fülle alle Pflichtfelder aus.');
      return;
    }

    const tokenId = workspace.task.token.id;
    const flowNodeId = workspace.task.token.currentFlowNodeId;
    if (!tokenId || !flowNodeId) {
      const error = new Error('Die Aufgabe enthält keine vollständige Laufzeitreferenz.');
      setSubmissionError({ taskId: view.id, error });
      toast.error('Aufgabe konnte nicht abgeschlossen werden', { description: error.message });
      return;
    }

    const idempotencyKey = completionKeys.current.get(view.id) ?? crypto.randomUUID();
    completionKeys.current.set(view.id, idempotencyKey);
    actions.complete.mutate({
      command: {
        flowNodeId,
        tokenId,
        processInstanceId: workspace.task.processInstanceId ?? null,
        expectedTaskRevision: taskRevision,
        actionId,
        data: renderer?.getData() ?? draft.currentData,
      },
      options: { idempotencyKey },
    }, {
      onSuccess: () => {
        completionKeys.current.delete(view.id);
        setSubmissionError(null);
        toast.success('Aufgabe abgeschlossen — der Prozess läuft weiter');
        onCompleted();
      },
      onError: (error) => {
        // Nur ein eindeutiger HTTP-Fehler verwirft den Schlüssel. Netzwerkfehler
        // und unbekannte Fehler besitzen einen unklaren Ausgang und behalten ihn.
        if (error instanceof FlowzerApiError && error.status !== 0) {
          completionKeys.current.delete(view.id);
        }
        setSubmissionError({ taskId: view.id, error });
        toast.error('Aufgabe konnte nicht abgeschlossen werden', {
          description: error instanceof Error ? error.message : undefined,
        });
      },
    });
  }

  const busy = actions.complete.isPending || draft.isSaving || draft.isDiscarding || lifecyclePending;
  return (
    <div className="bg-surface border-border shadow-card mt-[22px] overflow-hidden rounded-[var(--r-lg)] border">
      <div className="border-border bg-surface-2 flex items-center gap-2.5 border-b px-6 py-3.5">
        <Icon name="assignment" size={18} className="text-accent" />
        <span className="text-sm font-semibold">Formular ausfüllen</span>
        {view.formKey && <span className="text-faint ml-auto font-mono text-[11.5px]">{describeFormKey(view.formKey)}</span>}
      </div>

      <div className="px-[30px] py-[26px]">
        {draft.saveState === 'conflict' && (
          <TaskDraftConflictBanner error={draft.error} loading={draft.isRefreshing}
            onLoadServer={() => void draft.adoptServerDraft()} />
        )}
        {draft.loadState === 'ready' && workspace.pending && <InlineSpinner label="Formular wird geladen …" />}
        {workspace.error && <TaskFormError error={workspace.error} />}
        {draft.loadState === 'ready' && workspace.form && (
          <FormValidationErrors
            error={submissionError?.taskId === view.id ? submissionError.error : undefined}
            schema={workspace.form.formData ?? undefined}
          />
        )}
        {draft.loadState === 'ready' && workspace.form && (
          <FormRenderer key={`${view.id}:${draft.formInstanceKey}`} ref={formRef}
            schema={workspace.form.formData ?? undefined} initialData={draft.initialData}
            onChange={draft.setData} directoryAdapter={workspace.directoryAdapter} />
        )}
      </div>

      <div className="border-border bg-surface-2 flex flex-col gap-3 border-t px-6 py-4">
        <TaskDraftStatus {...draft} onSave={draft.save} onDiscard={draft.discard}
          onLoadServer={() => void draft.adoptServerDraft()} />
        <div className="flex items-center justify-between gap-2.5">
          <Button variant="ghost" size="sm" icon="schedule" onClick={onDefer}>Später</Button>
          <div className="flex flex-wrap justify-end gap-2.5">
            {formActions.length === 0 ? (
              <Button variant="primary" icon="check_circle" loading={busy}
                disabled={!workspace.form || draft.loadState !== 'ready' || busy}
                onClick={() => void complete()}>Aufgabe abschließen</Button>
            ) : formActions.map((action) => (
              <Button key={action.id} variant={action.variant}
                icon={action.variant === 'danger' ? 'block' : 'check_circle'} loading={busy}
                disabled={!workspace.form || draft.loadState !== 'ready' || busy}
                onClick={() => void complete(action.id)}>{action.label}</Button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

function TaskFormError({ error }: { error: Error }) {
  return (
    <div className="border-border rounded-[var(--r)] border border-dashed px-4 py-6 text-center">
      <div className="text-fail text-[13.5px] font-semibold">Für diese Aufgabe ist kein Formular verfügbar.</div>
      <div className="text-muted mt-1.5 text-[13px]">{error.message}</div>
      <div className="text-faint mt-2 text-xs">
        Prüfe den Form-Key des User-Tasks im Modeler und ob das Formular veröffentlicht ist.
      </div>
    </div>
  );
}
