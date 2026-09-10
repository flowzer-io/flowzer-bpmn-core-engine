import { EmptyState } from '@/components/ui/Card';
import { Icon } from '@/components/ui/Icon';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import { toneSurface } from '@/components/ui/Chip';
import { cn } from '@/lib/cn';
import type { TaskView } from '@/lib/taskView';

interface TaskListPaneProps {
  views: TaskView[];
  activeId?: string;
  deferredIds: readonly string[];
  pending: boolean;
  error: Error | null;
  width: string;
  onRetry: () => void;
  onSelect: (taskId: string) => void;
}

/** Responsive Aufgabenliste ohne Wissen über Transport, Entwürfe oder Formulare. */
export function TaskListPane({
  views,
  activeId,
  deferredIds,
  pending,
  error,
  width,
  onRetry,
  onSelect,
}: TaskListPaneProps) {
  return (
    <div className={cn(
      'border-border bg-surface flex min-h-0 flex-none flex-col border-r',
      width,
      'max-md:w-full',
      activeId && 'max-md:hidden',
    )}>
      <div className="flex-none px-[18px] pt-[18px] pb-2.5">
        <div className="font-display text-[17px] font-semibold">Zu erledigen</div>
        <div className="text-muted mt-0.5 text-[12.5px]">
          {pending ? 'wird geladen …' : `${views.length} offene Aufgabe${views.length === 1 ? '' : 'n'}`}
        </div>
      </div>

      <div className="flex min-h-0 flex-1 flex-col gap-[7px] overflow-auto px-3 pt-1 pb-4">
        {pending && <LoadingRows rows={4} className="p-0" />}
        {error && <ErrorState error={error} onRetry={onRetry} />}
        {!pending && !error && views.length === 0 && (
          <EmptyState icon="task_alt" title="Alles erledigt" description="Neue Aufgaben erscheinen hier automatisch." />
        )}

        {views.map((view) => {
          const isActive = view.id === activeId;
          const isDeferred = deferredIds.includes(view.id);
          return (
            <button
              key={view.id}
              type="button"
              onClick={() => onSelect(view.id)}
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
                style={{ background: priorityColor(view) }}
              />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-[13.5px] font-semibold">{view.title}</span>
                <span className="text-muted mt-0.5 block truncate text-xs">
                  {isDeferred ? 'zurückgestellt · ' : ''}
                  {taskWorkLabel(view)} · {view.workflowName} · {view.dueLabel}
                </span>
              </span>
              <Icon name="chevron_right" size={19} className="text-faint flex-none" />
            </button>
          );
        })}
      </div>
    </div>
  );
}

function priorityColor(view: TaskView): string {
  if (!view.priority) return 'var(--muted)';
  if (view.priority === 'Hoch') return 'var(--fail)';
  if (view.priority === 'Mittel') return 'var(--wait)';
  return 'var(--muted)';
}

function taskWorkLabel(view: TaskView): string {
  const state = view.task.workState;
  if (!state?.claimed) return 'verfügbar';
  if (state.isAssignedToCurrentUser) return 'bei dir';
  return state.actualAssigneeDisplayName ?? state.actualAssignee?.id ?? 'zugewiesen';
}
