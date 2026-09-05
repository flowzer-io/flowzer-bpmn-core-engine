import { useNavigate } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { Card, EmptyState, SectionLabel } from '@/components/ui/Card';
import { Dot, toneColor, toneSurface, type Tone } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { Segmented } from '@/components/ui/Segmented';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import { TaskGroupHeading, TaskRow } from '@/components/tasks/TaskRow';
import { useActivityFeed } from '@/lib/activity';
import { useDefinitions, useDiagnostics, useStartInstance, useUserTasks } from '@/lib/api/queries';
import { formatNumber, formatTodayLabel, greetingForNow } from '@/lib/format';
import { groupByDue, groupByWorkflow, sortTasks, toTaskView, type DueBucket } from '@/lib/taskView';
import { useSession } from '@/stores/session';

type DashboardView = 'due' | 'workflow';

const VIEW_OPTIONS = [
  { value: 'due' as const, label: 'Liste' },
  { value: 'workflow' as const, label: 'Nach Prozess' },
];

export function DashboardPage() {
  const navigate = useNavigate();
  const user = useSession((state) => state.user);
  const [view, setView] = useState<DashboardView>('due');

  const tasksQuery = useUserTasks();
  const diagnosticsQuery = useDiagnostics();
  const definitionsQuery = useDefinitions();
  const activity = useActivityFeed();
  const startInstance = useStartInstance();

  const taskViews = useMemo(
    () => sortTasks((tasksQuery.data ?? []).map((task) => toTaskView(task))),
    [tasksQuery.data],
  );

  const counts = useMemo(() => {
    const byBucket = (bucket: DueBucket) => taskViews.filter((item) => item.dueBucket === bucket).length;
    return {
      assigned: taskViews.length,
      today: byBucket('today'),
      overdue: byBucket('overdue'),
    };
  }, [taskViews]);

  const storage = diagnosticsQuery.data?.storage;

  const groups = useMemo(
    () => (view === 'due' ? groupByDue(taskViews) : groupByWorkflow(taskViews)),
    [view, taskViews],
  );

  // Für den Schnellstart zählen nur Definitionen, die tatsächlich deployt sind.
  const quickStart = useMemo(
    () => (definitionsQuery.data ?? []).filter((definition) => definition.deployedId).slice(0, 4),
    [definitionsQuery.data],
  );

  const openTask = (taskId: string) => {
    void navigate({ to: '/tasks', search: { task: taskId } });
  };

  return (
    <PageContainer className="pt-[30px]">
      <PageHeader
        className="animate-fade-up"
        eyebrow={formatTodayLabel()}
        title={`${greetingForNow()}, ${user?.name.split(' ')[0] ?? ''}`}
        description={describeWorkload(counts.assigned, counts.today, counts.overdue)}
        actions={
          <Button variant="primary" icon="rocket_launch" onClick={() => void navigate({ to: '/workflows' })}>
            Prozess starten
          </Button>
        }
      />

      <div className="mb-[26px] grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(190px,1fr))]">
        <StatCard
          icon="inbox"
          tone="accent"
          label="Mir zugewiesen"
          value={counts.assigned}
          hint={tasksQuery.isPending ? 'wird geladen …' : `${counts.today} heute fällig`}
          delay={0.04}
        />
        <StatCard
          icon="today"
          tone="wait"
          label="Heute fällig"
          value={counts.today}
          hint={nextDueHint(taskViews)}
          delay={0.08}
        />
        <StatCard
          icon="error"
          tone="fail"
          label="Überfällig"
          value={counts.overdue}
          hint={counts.overdue === 0 ? 'alles im Zeitplan' : firstOverdueTitle(taskViews)}
          emphasize={counts.overdue > 0}
          delay={0.12}
        />
        <StatCard
          icon="play_circle"
          tone="run"
          label="Laufende Instanzen"
          value={storage?.activeInstances ?? 0}
          hint={
            storage
              ? `${formatNumber(storage.completedInstances)} abgeschlossen · ${formatNumber(storage.failedInstances)} fehlerhaft`
              : 'wird geladen …'
          }
          delay={0.16}
        />
      </div>

      <div className="grid items-start gap-[22px] lg:grid-cols-[minmax(0,1fr)_328px]">
        <Card className="animate-fade-up" style={{ animationDelay: '0.2s' }}>
          <div className="flex items-center justify-between px-5 pt-4 pb-3.5">
            <div className="font-display text-[17px] font-semibold">Meine Aufgaben</div>
            <Segmented
              options={VIEW_OPTIONS}
              value={view}
              onChange={setView}
              aria-label="Gruppierung der Aufgaben"
            />
          </div>

          <div className="px-5 pt-1 pb-1.5">
            {tasksQuery.isPending && <LoadingRows rows={4} className="p-0" />}

            {tasksQuery.error && (
              <ErrorState error={tasksQuery.error} onRetry={() => void tasksQuery.refetch()} />
            )}

            {!tasksQuery.isPending && !tasksQuery.error && taskViews.length === 0 && (
              <EmptyState
                icon="task_alt"
                title="Keine offenen Aufgaben"
                description="Sobald dir ein Prozess eine Aufgabe zuweist, erscheint sie hier automatisch."
              />
            )}

            {groups.map((group) => (
              <div key={group.key}>
                <TaskGroupHeading
                  label={view === 'due' ? group.label : group.label.split(' · ')[0]!}
                  count={group.items.length}
                  bucket={view === 'due' ? (group.key as DueBucket) : undefined}
                />
                {group.items.map((item) => (
                  <TaskRow key={item.id} view={item} onOpen={() => openTask(item.id)} />
                ))}
              </div>
            ))}
          </div>
        </Card>

        <aside className="animate-fade-up flex flex-col gap-[18px]" style={{ animationDelay: '0.24s' }}>
          <Card className="px-[19px] py-[18px]">
            <SectionLabel className="mb-3">Aktivität</SectionLabel>

            {activity.length === 0 ? (
              <div className="text-muted text-[13px]">Noch keine Ereignisse aufgezeichnet.</div>
            ) : (
              <div className="flex flex-col">
                {activity.slice(0, 5).map((entry, index, all) => (
                  <button
                    key={entry.id}
                    type="button"
                    onClick={() => entry.href && void navigate({ to: entry.href })}
                    className="flex cursor-pointer gap-3 border-none bg-transparent px-0 py-0.5 text-left"
                  >
                    <span className="flex flex-none flex-col items-center">
                      <Dot tone={entry.tone} size={9} halo className="mt-1.5" />
                      {index < all.length - 1 && <span className="bg-border my-1 w-0.5 flex-1" />}
                    </span>
                    <span className="pb-3.5">
                      <span className="block text-[13.5px] leading-snug">{entry.text}</span>
                      <span className="text-faint mt-0.5 block font-mono text-xs">{entry.time}</span>
                    </span>
                  </button>
                ))}
              </div>
            )}
          </Card>

          <Card
            className="px-[19px] py-[18px]"
            style={{
              background: `linear-gradient(140deg, ${toneSurface('accent', 14)}, var(--surface))`,
            }}
          >
            <div className="flex items-center gap-2 font-semibold">
              <Icon name="bolt" size={20} className="text-accent" />
              Schnellstart
            </div>
            <div className="text-muted mt-1.5 mb-3.5 text-[13px]">
              Häufig genutzte Prozesse direkt anstoßen.
            </div>

            {quickStart.length === 0 ? (
              <div className="text-muted text-[13px]">
                Es ist noch kein Workflow deployt.{' '}
                <button
                  type="button"
                  onClick={() => void navigate({ to: '/workflows' })}
                  className="text-accent cursor-pointer border-none bg-transparent p-0 font-semibold underline-offset-2 hover:underline"
                >
                  Jetzt einen anlegen
                </button>
              </div>
            ) : (
              <div className="flex flex-col gap-[7px]">
                {quickStart.map((definition) => (
                  <button
                    key={definition.definitionId}
                    type="button"
                    disabled={startInstance.isPending}
                    onClick={() => {
                      startInstance.mutate(definition.definitionId, {
                        onSuccess: (instance) => {
                          toast.success(`„${definition.name}" gestartet`, {
                            action: {
                              label: 'Öffnen',
                              onClick: () => void navigate({ to: `/instances/${instance.instanceId}` }),
                            },
                          });
                        },
                        onError: (error) =>
                          toast.error(`„${definition.name}" konnte nicht gestartet werden`, {
                            description: error instanceof Error ? error.message : undefined,
                          }),
                      });
                    }}
                    className="bg-surface border-border hover:border-accent text-text flex cursor-pointer items-center gap-2.5 rounded-[var(--r-sm)] border px-3 py-2.5 text-left text-[13.5px] font-medium disabled:opacity-60"
                  >
                    <Icon name="rocket_launch" size={18} className="text-accent" />
                    <span className="min-w-0 flex-1 truncate">{definition.name}</span>
                  </button>
                ))}
              </div>
            )}
          </Card>
        </aside>
      </div>
    </PageContainer>
  );
}

interface StatCardProps {
  icon: string;
  tone: Tone;
  label: string;
  value: number;
  hint: string;
  emphasize?: boolean;
  delay: number;
}

function StatCard({ icon, tone, label, value, hint, emphasize = false, delay }: StatCardProps) {
  return (
    <Card className="animate-fade-up px-[18px] py-[17px]" style={{ animationDelay: `${delay}s` }}>
      <div className="text-muted flex items-center gap-2 text-[13px] font-medium">
        <Icon name={icon} size={18} style={{ color: toneColor(tone) }} />
        {label}
      </div>
      <div
        className="font-display mt-2.5 text-[34px] leading-none font-semibold tracking-[-0.02em]"
        style={emphasize ? { color: 'var(--fail)' } : undefined}
      >
        {formatNumber(value)}
      </div>
      <div className="text-muted mt-1.5 truncate text-[12.5px]">{hint}</div>
    </Card>
  );
}

function describeWorkload(assigned: number, today: number, overdue: number): string {
  if (assigned === 0) return 'Aktuell ist dir keine Aufgabe zugewiesen.';
  if (overdue > 0) {
    return `Du hast ${today} Aufgabe${today === 1 ? '' : 'n'}, die heute fällig ${today === 1 ? 'ist' : 'sind'} — und ${overdue} überfällige.`;
  }
  return `Du hast ${assigned} offene Aufgabe${assigned === 1 ? '' : 'n'}, davon ${today} heute fällig.`;
}

function nextDueHint(views: { dueBucket: DueBucket; dueLabel: string }[]): string {
  const next = views.find((view) => view.dueBucket === 'today');
  return next ? `nächste ${next.dueLabel.replace('Heute, ', 'um ')}` : 'nichts mehr für heute';
}

function firstOverdueTitle(views: { dueBucket: DueBucket; title: string }[]): string {
  return views.find((view) => view.dueBucket === 'overdue')?.title ?? '';
}
