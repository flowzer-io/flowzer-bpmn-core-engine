import { useMemo, useRef, useState } from 'react';
import { toast } from 'sonner';

import { TaskStepper } from '@/components/tasks/TaskStepper';
import { FormRenderer, type FormRendererHandle } from '@/components/forms/FormRenderer';
import { Button } from '@/components/ui/Button';
import { Chip, toneSurface } from '@/components/ui/Chip';
import { EmptyState } from '@/components/ui/Card';
import { Icon } from '@/components/ui/Icon';
import { ErrorState, InlineSpinner, LoadingRows } from '@/components/ui/States';
import { useCompleteUserTask, useDefinitionXml, useUserTaskForm, useUserTasks } from '@/lib/api/queries';
import type { ProcessVariables } from '@/lib/api/types';
import { parseBpmn } from '@/lib/bpmnModel';
import { cn } from '@/lib/cn';
import { formatTimestamp } from '@/lib/format';
import { PRIORITY_TONE, sortTasks, taskIcon, toTaskView } from '@/lib/taskView';

interface TasksPageProps {
  /** Vorausgewählte Aufgabe (z. B. aus dem Dashboard oder der Befehlspalette). */
  selectedTaskId?: string;
  onSelectTask?: (taskId: string) => void;
  /** Im Sachbearbeiter-Modus füllt die Seite den kompletten Bildschirm. */
  variant?: 'console' | 'worker';
}

/**
 * Aufgaben-Arbeitsplatz: links die eigene Liste, rechts das Formular der
 * ausgewählten Aufgabe. Diese Ansicht ist die einzige, die Sachbearbeitende
 * sehen — der Prozess-Manager erreicht sie zusätzlich über `/tasks`.
 */
export function TasksPage({ selectedTaskId, onSelectTask, variant = 'console' }: TasksPageProps) {
  const tasksQuery = useUserTasks();
  const completeTask = useCompleteUserTask();
  const formRef = useRef<FormRendererHandle>(null);

  // Lokal zurückgestellte Aufgaben rutschen ans Listenende — ein reiner
  // Anzeigezustand, die Engine kennt kein "später".
  const [deferred, setDeferred] = useState<string[]>([]);
  const [fallbackSelection, setFallbackSelection] = useState<string | null>(null);
  const [formData, setFormData] = useState<ProcessVariables>({});

  const views = useMemo(() => {
    const sorted = sortTasks((tasksQuery.data ?? []).map((task) => toTaskView(task)));
    return [...sorted].sort(
      (a, b) => Number(deferred.includes(a.id)) - Number(deferred.includes(b.id)),
    );
  }, [tasksQuery.data, deferred]);

  const activeId = selectedTaskId ?? fallbackSelection ?? views[0]?.id;
  const active = views.find((view) => view.id === activeId) ?? views[0];

  const select = (taskId: string) => {
    setFormData({});
    if (onSelectTask) onSelectTask(taskId);
    else setFallbackSelection(taskId);
  };

  const formQuery = useUserTaskForm(active?.id);
  const xmlQuery = useDefinitionXml(active?.task.definitionId);
  const model = useMemo(() => parseBpmn(xmlQuery.data), [xmlQuery.data]);

  async function handleComplete() {
    if (!active) return;

    const renderer = formRef.current;
    if (renderer) {
      const valid = await renderer.validate();
      if (!valid) {
        toast.error('Bitte fülle alle Pflichtfelder aus.');
        return;
      }
    }

    const data = renderer?.getData() ?? formData;

    completeTask.mutate(
      {
        flowNodeId: active.task.token.currentFlowNodeId ?? '',
        tokenId: active.task.token.id,
        processInstanceId: active.task.processInstanceId ?? null,
        data,
      },
      {
        onSuccess: () => {
          toast.success('Aufgabe abgeschlossen — der Prozess läuft weiter');
          setFormData({});
          const next = views.find((view) => view.id !== active.id);
          if (next) select(next.id);
        },
        onError: (error) =>
          toast.error('Aufgabe konnte nicht abgeschlossen werden', {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
  }

  const openCount = views.length;
  const listWidth = variant === 'worker' ? 'w-[340px]' : 'w-[320px]';

  return (
    <div className={cn('flex min-h-0 flex-1', variant === 'console' && 'h-full')}>
      <div
        className={cn(
          'border-border bg-surface flex min-h-0 flex-none flex-col border-r',
          listWidth,
        )}
      >
        <div className="flex-none px-[18px] pt-[18px] pb-2.5">
          <div className="font-display text-[17px] font-semibold">Zu erledigen</div>
          <div className="text-muted mt-0.5 text-[12.5px]">
            {tasksQuery.isPending
              ? 'wird geladen …'
              : `${openCount} offene Aufgabe${openCount === 1 ? '' : 'n'}`}
          </div>
        </div>

        <div className="flex min-h-0 flex-1 flex-col gap-[7px] overflow-auto px-3 pt-1 pb-4">
          {tasksQuery.isPending && <LoadingRows rows={4} className="p-0" />}

          {tasksQuery.error && (
            <ErrorState error={tasksQuery.error} onRetry={() => void tasksQuery.refetch()} />
          )}

          {!tasksQuery.isPending && !tasksQuery.error && views.length === 0 && (
            <EmptyState
              icon="task_alt"
              title="Alles erledigt"
              description="Neue Aufgaben erscheinen hier automatisch."
            />
          )}

          {views.map((view) => {
            const isActive = view.id === active?.id;
            const isDeferred = deferred.includes(view.id);

            return (
              <button
                key={view.id}
                type="button"
                onClick={() => select(view.id)}
                className={cn(
                  'flex w-full cursor-pointer items-center gap-3 rounded-[var(--r)] border px-3 py-3 text-left',
                  'transition-[background-color,border-color] duration-150',
                  isActive ? 'border-accent' : 'bg-surface-2 border-transparent',
                  isDeferred && !isActive && 'opacity-60',
                )}
                style={isActive ? { background: toneSurface('accent', 9) } : undefined}
              >
                <span
                  className="h-2.5 w-2.5 flex-none rounded-full"
                  style={{
                    background: view.priority
                      ? `var(--${view.priority === 'Hoch' ? 'fail' : view.priority === 'Mittel' ? 'wait' : 'muted'})`
                      : 'var(--muted)',
                  }}
                />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-[13.5px] font-semibold">{view.title}</span>
                  <span className="text-muted mt-0.5 block truncate text-xs">
                    {isDeferred ? 'zurückgestellt · ' : ''}
                    {view.workflowName} · {view.dueLabel}
                  </span>
                </span>
                <Icon name="chevron_right" size={19} className="text-faint flex-none" />
              </button>
            );
          })}
        </div>
      </div>

      <div className="bg-bg min-w-0 flex-1 overflow-auto">
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
          <div className="mx-auto max-w-[768px] px-10 pt-8 pb-16">
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
                  {active.dueRaw && (
                    <>
                      {' '}
                      <span
                        className="text-faint font-mono text-xs"
                        title="Fälligkeit ist ein Ausdruck und wird von der Engine ausgewertet."
                      >
                        ({active.dueRaw})
                      </span>
                    </>
                  )}
                </div>
              </div>
            </div>

            {model.nodes.length > 0 && (
              <TaskStepper
                className="mt-5"
                model={model}
                currentNodeId={active.task.token.currentFlowNodeId}
              />
            )}

            <div className="bg-surface border-border shadow-card mt-[22px] overflow-hidden rounded-[var(--r-lg)] border">
              <div className="border-border bg-surface-2 flex items-center gap-2.5 border-b px-6 py-3.5">
                <Icon name="assignment" size={18} className="text-accent" />
                <span className="text-sm font-semibold">Formular ausfüllen</span>
                {active.formKey && (
                  <span className="text-faint ml-auto font-mono text-[11.5px]">{active.formKey}</span>
                )}
              </div>

              <div className="px-[30px] py-[26px]">
                {formQuery.isPending && <InlineSpinner label="Formular wird geladen …" />}

                {formQuery.error && (
                  <div className="border-border rounded-[var(--r)] border border-dashed px-4 py-6 text-center">
                    <div className="text-fail text-[13.5px] font-semibold">
                      Für diese Aufgabe ist kein Formular verfügbar.
                    </div>
                    <div className="text-muted mt-1.5 text-[13px]">
                      {formQuery.error instanceof Error ? formQuery.error.message : null}
                    </div>
                    <div className="text-faint mt-2 text-xs">
                      Prüfe den Form-Key des User-Tasks im Modeler und ob das Formular gespeichert ist.
                    </div>
                  </div>
                )}

                {formQuery.data && (
                  <FormRenderer
                    key={active.id}
                    ref={formRef}
                    schema={formQuery.data.formData ?? undefined}
                    initialData={active.task.token.variables ?? undefined}
                    onChange={setFormData}
                  />
                )}
              </div>

              <div className="border-border bg-surface-2 flex items-center justify-between gap-2.5 border-t px-6 py-4">
                <Button
                  variant="ghost"
                  size="sm"
                  icon="schedule"
                  onClick={() => {
                    setDeferred((current) =>
                      current.includes(active.id) ? current : [...current, active.id],
                    );
                    const next = views.find((view) => view.id !== active.id);
                    if (next) select(next.id);
                    toast('Zurückgestellt — bleibt in deiner Liste', { icon: '🕓' });
                  }}
                >
                  Später
                </Button>

                <Button
                  variant="primary"
                  icon="check_circle"
                  loading={completeTask.isPending}
                  disabled={!formQuery.data}
                  onClick={() => void handleComplete()}
                >
                  Aufgabe abschließen
                </Button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
