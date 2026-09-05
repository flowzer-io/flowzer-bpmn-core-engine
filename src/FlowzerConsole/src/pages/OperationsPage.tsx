import { useNavigate } from '@tanstack/react-router';
import { useMemo } from 'react';

import { Card, CardHeader, EmptyState } from '@/components/ui/Card';
import { Chip, toneColor, type Tone } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { ErrorState, LoadingRows, Skeleton } from '@/components/ui/States';
import { useDiagnostics, useHealth, useInstances, useTimers } from '@/lib/api/queries';
import type { OperationsDiagnosticsDto } from '@/lib/api/types';
import { instanceBucket } from '@/lib/api/normalize';
import { formatDueIn, formatDuration, formatNumber, formatRelative, parseApiDate, shortId } from '@/lib/format';
import { useCan } from '@/stores/session';

type HealthLevel = 'ok' | 'warn' | 'error';

const HEALTH_TONE: Record<HealthLevel, Tone> = { ok: 'done', warn: 'wait', error: 'fail' };
const HEALTH_LABEL: Record<HealthLevel, string> = { ok: 'Gesund', warn: 'Hinweis', error: 'Fehler' };

const TIMER_GRID = 'grid-cols-[minmax(140px,1.6fr)_minmax(90px,1fr)_62px_128px]';

export function OperationsPage() {
  const navigate = useNavigate();
  const mayOperate = useCan()('operator');

  // Ohne Betriebsrolle lehnt die API die Diagnose ab. Die Abfragen gar nicht erst zu
  // stellen ist ehrlicher als eine Seite voller Fehlermeldungen.
  const diagnosticsQuery = useDiagnostics({ enabled: mayOperate });
  const timersQuery = useTimers({ enabled: mayOperate });
  const healthQuery = useHealth();
  const instancesQuery = useInstances();

  const diagnostics = diagnosticsQuery.data;

  const healthCards = useMemo(() => buildHealthCards(diagnostics, healthQuery.data?.status), [
    diagnostics,
    healthQuery.data?.status,
  ]);

  const overall: HealthLevel = healthCards.some((card) => card.level === 'error')
    ? 'error'
    : healthCards.some((card) => card.level === 'warn')
      ? 'warn'
      : 'ok';

  const timers = useMemo(
    () =>
      [...(timersQuery.data ?? [])].sort(
        (a, b) => (parseApiDate(a.dueAt)?.getTime() ?? 0) - (parseApiDate(b.dueAt)?.getTime() ?? 0),
      ),
    [timersQuery.data],
  );

  const failedInstances = useMemo(
    () => (instancesQuery.data ?? []).filter((instance) => instanceBucket(instance.state) === 'error'),
    [instancesQuery.data],
  );

  if (!mayOperate) {
    return (
      <div className="p-6">
        <EmptyState
          icon="lock"
          title="Betrieb und Diagnose"
          description="Dieser Bereich zeigt den Zustand der Engine, laufende Timer und hängende Arbeit. Er ist der Betriebsrolle vorbehalten; bitten Sie die IT um die Freigabe, wenn Sie ihn brauchen."
        />
      </div>
    );
  }

  return (
    <PageContainer>
      <PageHeader
        title="Betrieb & Diagnose"
        description="Laufzeitzustand von Engine, Ablage und Timer-Scheduler."
        actions={
          <span
            className="inline-flex items-center gap-2 rounded-[20px] px-3.5 py-1.5 text-[13px] font-semibold"
            style={{
              background: `color-mix(in oklab, ${toneColor(HEALTH_TONE[overall])} 12%, transparent)`,
              color: toneColor(HEALTH_TONE[overall]),
            }}
          >
            <span className="h-2 w-2 rounded-full" style={{ background: toneColor(HEALTH_TONE[overall]) }} />
            {overall === 'ok'
              ? 'Alle Systeme betriebsbereit'
              : overall === 'warn'
                ? 'Betriebsbereit mit Hinweisen'
                : 'Störung erkannt'}
          </span>
        }
      />

      {diagnosticsQuery.error && (
        <Card className="mb-5">
          <ErrorState error={diagnosticsQuery.error} onRetry={() => void diagnosticsQuery.refetch()} />
        </Card>
      )}

      <div className="mb-[22px] grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(184px,1fr))]">
        {diagnosticsQuery.isPending
          ? Array.from({ length: 4 }, (_, index) => (
              <Skeleton key={index} className="h-[132px] rounded-[var(--r-lg)]" />
            ))
          : healthCards.map((card) => (
              <Card key={card.name} className="px-[17px] py-4">
                <div className="flex items-center justify-between">
                  <span className="bg-surface-2 text-muted grid h-[34px] w-[34px] place-items-center rounded-[9px]">
                    <Icon name={card.icon} size={19} />
                  </span>
                  <span
                    className="h-2.5 w-2.5 rounded-full"
                    style={{
                      background: toneColor(HEALTH_TONE[card.level]),
                      boxShadow: `0 0 0 3px color-mix(in oklab, ${toneColor(HEALTH_TONE[card.level])} 14%, transparent)`,
                    }}
                  />
                </div>
                <div className="mt-3 flex items-center gap-2">
                  <span className="text-sm font-semibold">{card.name}</span>
                  <Chip tone={HEALTH_TONE[card.level]}>{HEALTH_LABEL[card.level]}</Chip>
                </div>
                <div className="text-muted mt-1 text-[12.5px]">{card.detail}</div>
              </Card>
            ))}
      </div>

      <div className="grid items-start gap-[18px] lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <div className="flex min-w-0 flex-col gap-[18px]">
          <Card>
            <CardHeader
              icon="schedule"
              iconClassName="text-wait"
              title="Timer-Scheduler"
              actions={
                <span className="text-muted font-mono text-[11.5px]">
                  {diagnostics ? `Poll ${diagnostics.timerScheduler.pollIntervalSeconds} s` : '—'}
                </span>
              }
            />

            <div
              className={`bg-surface-2 text-muted grid ${TIMER_GRID} gap-3.5 px-[18px] py-2.5 font-mono text-[10px] font-semibold tracking-[0.09em] uppercase`}
            >
              <div>Timer / Instanz</div>
              <div>Art</div>
              <div>Wdh.</div>
              <div>Nächste</div>
            </div>

            {timersQuery.isPending && <LoadingRows rows={3} />}
            {timersQuery.error && (
              <ErrorState error={timersQuery.error} onRetry={() => void timersQuery.refetch()} />
            )}

            {!timersQuery.isPending && timers.length === 0 && (
              <EmptyState
                className="border-border border-t"
                icon="schedule"
                title="Keine offenen Timer"
                description="Sobald ein Prozess auf eine Zeit wartet, erscheint der Timer hier."
              />
            )}

            {timers.map((timer) => (
              <button
                key={timer.id}
                type="button"
                disabled={!timer.processInstanceId}
                onClick={() =>
                  timer.processInstanceId &&
                  void navigate({ to: `/instances/${timer.processInstanceId}` })
                }
                className={`border-border grid w-full ${TIMER_GRID} items-center gap-3.5 border-t border-x-0 border-b-0 bg-transparent px-[18px] py-3.5 text-left ${
                  timer.processInstanceId ? 'hover:bg-inset cursor-pointer' : 'cursor-default'
                }`}
              >
                <div className="min-w-0">
                  <div className="truncate text-[13.5px] font-semibold" title={timer.flowNodeId}>
                    {timer.flowNodeId}
                  </div>
                  <div className="text-faint mt-0.5 truncate font-mono text-[11.5px]">
                    {timer.processInstanceId
                      ? `#${shortId(timer.processInstanceId)}`
                      : `${timer.relatedDefinitionId} (Start-Timer)`}
                  </div>
                </div>
                <div className="text-accent truncate font-mono text-xs">{timer.kind}</div>
                <div className="text-muted font-mono text-[12.5px]">
                  {timer.remainingOccurrences ?? '∞'}
                </div>
                <div className="text-wait inline-flex items-center gap-1.5 text-[12.5px] font-semibold whitespace-nowrap">
                  <span className="bg-wait h-1.5 w-1.5 rounded-full" />
                  {formatDueIn(timer.dueAt)}
                </div>
              </button>
            ))}
          </Card>

          <Card>
            <CardHeader icon="error" iconClassName="text-fail" title="Fehlgeschlagene Instanzen" />

            {failedInstances.length === 0 ? (
              <EmptyState
                className="border-border border-t"
                icon="check_circle"
                title="Keine Fehler"
                description="Aktuell ist keine Instanz in einem Fehlerzustand."
              />
            ) : (
              failedInstances.map((instance) => (
                <button
                  key={instance.instanceId}
                  type="button"
                  onClick={() => void navigate({ to: `/instances/${instance.instanceId}` })}
                  className="border-border hover:bg-inset flex w-full cursor-pointer items-center gap-3 border-t border-x-0 border-b-0 bg-transparent px-[18px] py-3.5 text-left"
                >
                  <span
                    className="text-fail grid h-8 w-8 flex-none place-items-center rounded-lg"
                    style={{ background: 'color-mix(in oklab, var(--fail) 12%, transparent)' }}
                  >
                    <Icon name="warning" size={18} />
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-[13.5px] font-semibold">
                      {instance.relatedDefinitionName}
                    </div>
                    <div className="text-faint mt-0.5 font-mono text-[11.5px]">
                      #{shortId(instance.instanceId)} · {formatRelative(instance.startedAt)}
                    </div>
                  </div>
                  <span className="text-accent flex-none text-[12.5px] font-semibold">Details</span>
                </button>
              ))
            )}
          </Card>
        </div>

        <div className="flex min-w-0 flex-col gap-[18px]">
          <Card className="p-[18px]">
            <div className="mb-1 flex items-baseline justify-between">
              <span className="font-display text-[15.5px] font-semibold">Instanzen</span>
              <span className="text-muted font-mono text-[11.5px]">Gesamtbestand</span>
            </div>

            {diagnostics ? (
              <>
                <div className="mb-4 flex items-baseline gap-2">
                  <span className="font-display text-[30px] font-semibold tracking-[-0.02em]">
                    {formatNumber(diagnostics.storage.totalInstances)}
                  </span>
                  <span className="text-muted text-[12.5px] font-semibold">gespeichert</span>
                </div>
                <DistributionBar
                  segments={[
                    { label: 'aktiv', value: diagnostics.storage.activeInstances, tone: 'run' },
                    { label: 'abgeschlossen', value: diagnostics.storage.completedInstances, tone: 'done' },
                    { label: 'fehlerhaft', value: diagnostics.storage.failedInstances, tone: 'fail' },
                  ]}
                />
              </>
            ) : (
              <Skeleton className="h-[120px]" />
            )}
          </Card>

          <Card>
            <CardHeader icon="inventory_2" title="Ablage" />
            <div className="px-[18px] py-3">
              {diagnostics ? (
                <dl className="grid grid-cols-2 gap-x-4 gap-y-2.5 text-[13px]">
                  <Metric label="Definitionen" value={diagnostics.storage.totalDefinitions} />
                  <Metric label="davon aktiv" value={diagnostics.storage.activeDefinitions} />
                  <Metric label="Formulare" value={diagnostics.storage.formMetadataEntries} />
                  <Metric label="Offene User-Tasks" value={diagnostics.storage.openUserTasks} />
                  <Metric label="Wartende Nachrichten" value={diagnostics.storage.pendingMessages} />
                  <Metric label="Wartende Signale" value={diagnostics.storage.pendingSignals} />
                  <Metric label="Offene Timer" value={diagnostics.storage.pendingTimers} />
                  <Metric label="Offene Services" value={diagnostics.storage.pendingServices} />
                </dl>
              ) : (
                <Skeleton className="h-[120px]" />
              )}
            </div>
          </Card>

          <Card>
            <CardHeader icon="sensors" title="Telemetrie" />
            <div className="text-muted px-[18px] py-3 text-[13px]">
              {diagnostics ? (
                <ul className="space-y-1.5">
                  <li>
                    OpenTelemetry:{' '}
                    <strong className="text-text">
                      {diagnostics.observability.enabled ? 'aktiv' : 'inaktiv'}
                    </strong>
                  </li>
                  <li>
                    OTLP-Export:{' '}
                    <strong className="text-text">
                      {diagnostics.observability.otlpExporterEnabled
                        ? (diagnostics.observability.otlpEndpointHint ?? 'aktiv')
                        : 'inaktiv'}
                    </strong>
                  </li>
                  <li className="font-mono text-[11.5px]">
                    {diagnostics.observability.serviceName} {diagnostics.observability.serviceVersion}
                  </li>
                  <li className="font-mono text-[11.5px]">
                    Umgebung: {diagnostics.environment}
                  </li>
                </ul>
              ) : (
                <Skeleton className="h-20" />
              )}
            </div>
          </Card>
        </div>
      </div>
    </PageContainer>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <>
      <dt className="text-muted">{label}</dt>
      <dd className="text-right font-mono font-semibold">{formatNumber(value)}</dd>
    </>
  );
}

function DistributionBar({ segments }: { segments: { label: string; value: number; tone: Tone }[] }) {
  const total = segments.reduce((sum, segment) => sum + segment.value, 0);

  return (
    <div>
      <div className="bg-surface-2 flex h-3 overflow-hidden rounded-full">
        {total === 0 ? (
          <div className="bg-border h-full w-full" />
        ) : (
          segments.map((segment) => (
            <div
              key={segment.label}
              title={`${segment.label}: ${segment.value}`}
              style={{ width: `${(segment.value / total) * 100}%`, background: toneColor(segment.tone) }}
            />
          ))
        )}
      </div>
      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5">
        {segments.map((segment) => (
          <span key={segment.label} className="text-muted inline-flex items-center gap-1.5 text-[12.5px]">
            <span className="h-2 w-2 rounded-full" style={{ background: toneColor(segment.tone) }} />
            {segment.label}
            <strong className="text-text font-mono">{formatNumber(segment.value)}</strong>
          </span>
        ))}
      </div>
    </div>
  );
}

interface HealthCard {
  name: string;
  icon: string;
  level: HealthLevel;
  detail: string;
}

function buildHealthCards(
  diagnostics: OperationsDiagnosticsDto | undefined,
  healthStatus: string | undefined,
): HealthCard[] {
  if (!diagnostics) return [];

  const scheduler = diagnostics.timerScheduler;
  const schedulerLevel: HealthLevel = !scheduler.enabled
    ? 'warn'
    : scheduler.lastErrorMessage
      ? 'error'
      : 'ok';

  return [
    {
      name: 'Web-API',
      icon: 'api',
      level: healthStatus && healthStatus.toLowerCase() !== 'healthy' ? 'warn' : 'ok',
      detail: healthStatus ? `Status ${healthStatus}` : 'erreichbar',
    },
    {
      name: 'Ablage',
      icon: 'database',
      level: 'ok',
      detail: `${formatNumber(diagnostics.storage.totalInstances)} Instanzen · ${diagnostics.storage.storageRootHint}`,
    },
    {
      name: 'Timer-Scheduler',
      icon: 'schedule',
      level: schedulerLevel,
      detail: !scheduler.enabled
        ? 'deaktiviert'
        : (scheduler.lastErrorMessage ??
          `${scheduler.status} · letzter Lauf ${formatDuration(scheduler.lastTickDurationMs)}`),
    },
    {
      name: 'OpenTelemetry',
      icon: 'sensors',
      level: diagnostics.observability.enabled ? 'ok' : 'warn',
      detail: diagnostics.observability.enabled
        ? (diagnostics.observability.otlpEndpointHint ?? 'aktiv')
        : 'OTLP-Export inaktiv',
    },
  ];
}
