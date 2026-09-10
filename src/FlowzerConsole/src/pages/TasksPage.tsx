import type { UserTaskWorkState } from '@flowzer/sdk';
import { useUserTaskActions, useUserTasks, useUserTaskWorkspace } from '@flowzer/react';
import { useMemo, useState } from 'react';
import { toast } from 'sonner';

import { TaskFormCard } from '@/components/tasks/TaskFormCard';
import { TaskLifecycleDialog } from '@/components/tasks/TaskLifecycleDialog';
import { TaskLifecyclePanel } from '@/components/tasks/TaskLifecyclePanel';
import { TaskListPane } from '@/components/tasks/TaskListPane';
import { Chip, toneSurface } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';
import { useCompactLayout } from '@/lib/useCompactLayout';
import { formatTimestamp } from '@/lib/format';
import {
  createTaskAssigneeSearch,
  createTaskFormDirectoryAdapter,
} from '@/lib/flowzer/taskDirectoryAdapters';
import { useTaskDraftEditor } from '@/lib/taskDraft';
import { PRIORITY_TONE, sortTasks, taskIcon, toTaskView } from '@/lib/taskView';
import type { TaskLifecycleCommand } from '@/components/tasks/TaskLifecycleDialog';

interface TasksPageProps {
  /** Vorausgewählte Aufgabe (z. B. aus dem Dashboard oder der Befehlspalette). */
  selectedTaskId?: string;
  /** `null` schliesst die Aufgabe wieder — auf dem Telefon der Weg zurueck zur Liste. */
  onSelectTask?: (taskId: string | null) => void;
  /** Im Sachbearbeiter-Modus füllt die Seite den kompletten Bildschirm. */
  variant?: 'console' | 'worker';
}

/**
 * Aufgaben-Arbeitsplatz: links die eigene Liste, rechts das Formular der
 * ausgewählten Aufgabe. Diese Ansicht ist die einzige, die Sachbearbeitende
 * sehen — der Prozess-Manager erreicht sie zusätzlich über `/tasks`.
 */
export function TasksPage({ selectedTaskId, onSelectTask, variant = 'console' }: TasksPageProps) {
  const tasksQuery = useUserTasks({ refetchInterval: 10_000 });

  // Lokal zurückgestellte Aufgaben rutschen ans Listenende — ein reiner
  // Anzeigezustand, die Engine kennt kein "später".
  const [deferred, setDeferred] = useState<string[]>([]);
  const [fallbackSelection, setFallbackSelection] = useState<string | null>(null);
  const [lifecycleDialog, setLifecycleDialog] = useState<{
    action: 'release' | 'assign' | 'delegate';
    taskId: string;
    expectedRevision: number;
  } | null>(null);

  const views = useMemo(() => {
    const sorted = sortTasks((tasksQuery.data ?? []).map((task) => toTaskView(task)));
    return [...sorted].sort(
      (a, b) => Number(deferred.includes(a.id)) - Number(deferred.includes(b.id)),
    );
  }, [tasksQuery.data, deferred]);

  // Am grossen Schirm ist die erste Aufgabe gleich geoeffnet — die Liste steht ja
  // daneben. Auf dem Telefon verdeckte dieselbe Vorauswahl die Liste; dort faengt man
  // bei der Liste an und oeffnet selbst.
  const compact = useCompactLayout();
  const activeId = selectedTaskId ?? fallbackSelection ?? (compact ? undefined : views[0]?.id);
  const active = views.find((view) => view.id === activeId) ?? (compact ? undefined : views[0]);
  const workspace = useUserTaskWorkspace(active?.id ?? '', {
    enabled: Boolean(active),
    refetchInterval: 10_000,
  });
  const actions = useUserTaskActions(active?.id ?? '');

  const select = (taskId: string | null) => {
    if (onSelectTask) onSelectTask(taskId);
    else setFallbackSelection(taskId);
  };

  const currentTask = workspace.task ?? active?.task;
  const workState = normalizeWorkState(currentTask?.workState);
  const canWork = workspace.canWork;
  const taskRevision = workState.revision;
  const lifecyclePending = actions.claim.isPending || actions.release.isPending
    || actions.assign.isPending || actions.delegate.isPending;
  const lifecycleError = actions.claim.error ?? actions.release.error
    ?? actions.assign.error ?? actions.delegate.error;
  const formQuery = {
    data: workspace.form,
    isPending: workspace.isPending,
    error: workspace.error,
  };
  const formDirectoryAdapter = useMemo(
    () => activeId
      ? createTaskFormDirectoryAdapter(activeId, workspace.searchSubjects, workspace.resolveSubjects)
      : undefined,
    [activeId, workspace.resolveSubjects, workspace.searchSubjects],
  );
  const lifecycleAssigneeSearch = useMemo(
    () => lifecycleDialog && lifecycleDialog.action !== 'release'
      ? createTaskAssigneeSearch(lifecycleDialog.action, actions.searchAssignees)
      : undefined,
    [actions.searchAssignees, lifecycleDialog],
  );
  const draft = useTaskDraftEditor(
    active?.id,
    currentTask?.token.variables ?? {},
    taskRevision,
    canWork,
    {
      draft: workspace.draft,
      isPending: workspace.isPending,
      isRefreshing: workspace.isRefreshing,
      error: workspace.error,
      reloadDraft: workspace.reloadDraft,
      saveDraft: actions.saveDraft,
      deleteDraft: actions.deleteDraft,
    },
  );

  function claim(expectedRevision: number) {
    if (!active) return;
    resetLifecycle();
    actions.claim.mutate(
      { expectedRevision },
      {
        onSuccess: () => toast.success('Aufgabe übernommen'),
        onError: (error) => toast.error('Aufgabe konnte nicht übernommen werden', {
          description: error instanceof Error ? error.message : undefined,
        }),
      },
    );
  }

  function openLifecycleDialog(
    action: 'release' | 'assign' | 'delegate',
    expectedRevision: number,
  ) {
    if (!active) return;
    resetLifecycle();
    setLifecycleDialog({ action, taskId: active.id, expectedRevision });
  }

  function submitLifecycle(command: TaskLifecycleCommand) {
    const callbacks = {
      onSuccess: () => {
        setLifecycleDialog(null);
        toast.success(command.action === 'release'
          ? 'Aufgabe zurückgegeben'
          : command.action === 'assign'
            ? 'Bearbeiter zugewiesen'
            : 'Aufgabe delegiert');
      },
    };

    if (command.action === 'release') {
      actions.release.mutate({
        expectedRevision: command.expectedRevision,
        reason: command.reason,
      }, callbacks);
      return;
    }

    const payload = {
      expectedRevision: command.expectedRevision,
      reason: command.reason,
      assignee: command.assignee,
    };
    if (command.action === 'assign') actions.assign.mutate(payload, callbacks);
    else actions.delegate.mutate(payload, callbacks);
  }

  function resetLifecycle() {
    actions.claim.reset();
    actions.release.reset();
    actions.assign.reset();
    actions.delegate.reset();
  }

  const listWidth = variant === 'worker' ? 'w-[340px]' : 'w-[320px]';
  return (
    <div className={cn('flex min-h-0 flex-1', variant === 'console' && 'h-full')}>
      <TaskListPane
        views={views}
        activeId={active?.id}
        deferredIds={deferred}
        pending={tasksQuery.isPending}
        error={tasksQuery.error}
        width={listWidth}
        onRetry={() => void tasksQuery.refetch()}
        onSelect={(taskId) => select(taskId)}
      />

      <div className={cn('bg-bg min-w-0 flex-1 overflow-auto', !active && 'max-md:hidden')}>
        {!active ? (
          <div className="grid h-full place-items-center p-10 text-center">
            <div>
              <div
                className="text-done mx-auto grid h-[74px] w-[74px] place-items-center rounded-full"
                style={{ background: toneSurface('done', 14) }}
              >
                <Icon name="task_alt" size={40} />
              </div>
              <h1 className="font-display mt-5 text-[25px] font-semibold tracking-[-0.02em]">
                Alles erledigt
              </h1>
              <div className="text-muted mx-auto mt-2 max-w-[360px] leading-normal">
                Du hast alle dir zugewiesenen Aufgaben abgearbeitet. Neue Aufgaben erscheinen hier
                automatisch.
              </div>
            </div>
          </div>
        ) : (
          <div className="mx-auto max-w-[768px] px-5 pt-5 pb-16 md:px-10 md:pt-8">
            <button
              type="button"
              onClick={() => select(null)}
              className="text-muted hover:text-accent -ml-2 mb-3 flex items-center gap-1.5 rounded-[var(--r-sm)] px-2 py-1.5 text-[13.5px] font-semibold md:hidden"
            >
              <Icon name="arrow_back" size={19} />
              Alle Aufgaben
            </button>
            <div className="flex items-start gap-4">
              <span
                className="text-accent grid h-[46px] w-[46px] flex-none place-items-center rounded-xl"
                style={{ background: toneSurface('accent', 12) }}
              >
                <Icon name={taskIcon(active)} size={24} />
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2.5">
                  <span className="bg-surface-2 text-muted rounded-md px-2 py-0.5 font-mono text-[11.5px]">
                    {active.workflowName}
                  </span>
                  {active.priority && <Chip tone={PRIORITY_TONE[active.priority]}>{active.priority}</Chip>}
                  {active.dueBucket === 'overdue' && <Chip tone="fail">Überfällig</Chip>}
                </div>
                <h1 className="font-display mt-2.5 text-2xl font-semibold tracking-[-0.02em]">
                  {active.title}
                </h1>
                <div className="text-muted mt-1.5 text-[13.5px]">
                  Gestartet {formatTimestamp(active.startedAt)} · fällig {active.dueLabel}
                </div>
              </div>
            </div>

            <TaskLifecyclePanel
              state={workState}
              busy={lifecyclePending}
              draftDirty={canWork && draft.dirty}
              onClaim={claim}
              onRelease={(revision) => openLifecycleDialog('release', revision)}
              onAssign={(revision) => openLifecycleDialog('assign', revision)}
              onDelegate={(revision) => openLifecycleDialog('delegate', revision)}
            />

            {!lifecycleDialog && Boolean(lifecycleError) && (
              <div className="border-wait bg-wait/10 mt-3 rounded-[var(--r)] border px-3.5 py-3 text-[12.5px]" role="alert">
                {lifecycleError instanceof Error
                  ? lifecycleError.message
                  : 'Der Aufgabenstand konnte nicht geändert werden.'}
              </div>
            )}

            {canWork && currentTask ? (
              <TaskFormCard
                view={active}
                workspace={{
                  task: currentTask,
                  form: formQuery.data,
                  pending: formQuery.isPending,
                  error: formQuery.error,
                  directoryAdapter: formDirectoryAdapter,
                }}
                controls={{ draft, actions, lifecyclePending, taskRevision }}
                onCompleted={() => {
                  const next = views.find((view) => view.id !== active.id);
                  if (next) select(next.id);
                }}
                onDefer={() => {
                  setDeferred((current) => current.includes(active.id) ? current : [...current, active.id]);
                  const next = views.find((view) => view.id !== active.id);
                  if (next) select(next.id);
                  toast('Zurückgestellt — bleibt in deiner Liste', { icon: '🕓' });
                }}
              />
            ) : (
              <div className="border-border bg-surface mt-[22px] rounded-[var(--r-lg)] border border-dashed px-6 py-10 text-center">
                <Icon name="lock" size={28} className="text-faint mx-auto" />
                <div className="mt-3 text-sm font-semibold">Noch nicht zur Bearbeitung geöffnet</div>
                <p className="text-muted mx-auto mt-1.5 max-w-[420px] text-[13px] leading-normal">
                  Übernimm die Aufgabe zuerst. Formular und privater Entwurf werden erst danach geladen.
                </p>
              </div>
            )}
          </div>
        )}
      </div>

      {lifecycleDialog && (
        <TaskLifecycleDialog
          open
          {...lifecycleDialog}
          busy={lifecyclePending}
          error={lifecycleError}
          searchAssignees={lifecycleAssigneeSearch}
          onOpenChange={(open) => {
            if (!open && !lifecyclePending) {
              setLifecycleDialog(null);
              resetLifecycle();
            }
          }}
          onUseCurrentRevision={() => {
            if (active?.id !== lifecycleDialog.taskId) return;
            setLifecycleDialog({
              ...lifecycleDialog,
              expectedRevision: workState.revision,
            });
            resetLifecycle();
          }}
          onSubmit={submitLifecycle}
        />
      )}
    </div>
  );
}

function normalizeWorkState(state: UserTaskWorkState | undefined): UserTaskWorkState & { revision: number } {
  return {
    revision: state?.revision ?? 0,
    claimed: state?.claimed ?? false,
    isAssignedToCurrentUser: state?.isAssignedToCurrentUser ?? false,
    actualAssignee: state?.actualAssignee,
    actualAssigneeDisplayName: state?.actualAssigneeDisplayName ?? null,
    canWork: state?.canWork ?? false,
    canClaim: state?.canClaim ?? false,
    canRelease: state?.canRelease ?? false,
    canAssign: state?.canAssign ?? false,
    canDelegate: state?.canDelegate ?? false,
  };
}
