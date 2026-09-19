import { useNavigate } from '@tanstack/react-router';
import { useMemo } from 'react';

import { Button } from '@/components/ui/Button';
import { Card, CardHeader, EmptyState, SectionLabel } from '@/components/ui/Card';
import { Dot, toneColor } from '@/components/ui/Chip';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import { useAnalyticsDetail } from '@/lib/api/queries';
import type { AnalyticsDayPointDto, FlowNodeAnalyticsDto } from '@/lib/api/types';
import {
  barWidthPercent,
  dayLabel,
  flowNodeLabel,
  outcomeSlices,
  rangeLabel,
  rangeQuery,
  resolveRange,
  timelineScaleMax,
  waitTimeScaleMax,
  type AnalyticsSearch,
} from '@/lib/analytics';
import { formatDurationSeconds, formatNumber } from '@/lib/format';
import { useCan } from '@/stores/session';

const NODE_GRID = 'grid-cols-[minmax(130px,2fr)_78px_repeat(3,minmax(74px,1fr))_86px]';

interface AnalyticsDetailPageProps {
  metaDefinitionId: string;
  search: AnalyticsSearch;
}

/**
 * Die Auswertung eines einzelnen Workflows: Kennzahlen, Wartezeit je Schritt und die
 * Zeitreihe der Starts und Abschlüsse.
 *
 * Die Reihenfolge der Schritte kommt unverändert vom Server (Engpass zuerst). Die Seite
 * sortiert sie nicht nach — sonst stünde oben ein anderer Engpass als der berechnete.
 */
export function AnalyticsDetailPage({ metaDefinitionId, search }: AnalyticsDetailPageProps) {
  const navigate = useNavigate();
  const mayOperate = useCan()('operator');

  const range = useMemo(() => resolveRange(search, new Date()), [search]);
  const detailQuery = useAnalyticsDetail(metaDefinitionId, rangeQuery(range), null, {
    enabled: mayOperate,
  });

  const detail = detailQuery.data;
  const nodes = detail?.nodes ?? [];
  const timeline = detail?.timeline ?? [];
  const waitScale = waitTimeScaleMax(nodes);

  if (!mayOperate) {
    return (
      <div className="p-6">
        <EmptyState
          icon="lock"
          title="Auswertungen"
          description="Dieser Bereich wertet die Laufzeithistorie aus: Durchlaufzeiten, Ausgänge und Engpässe je Workflow. Er ist der Betriebsrolle vorbehalten; bitten Sie die IT um die Freigabe, wenn Sie ihn brauchen."
        />
      </div>
    );
  }

  return (
    <PageContainer>
      <PageHeader
        eyebrow={metaDefinitionId}
        title={detail?.summary.name ?? 'Auswertung'}
        description={`Durchlaufzeit, Engpässe und Verlauf · ${rangeLabel(range)}`}
        actions={
          <Button
            icon="arrow_back"
            onClick={() => void navigate({ to: '/analytics', search })}
          >
            Zur Übersicht
          </Button>
        }
      />

      {detailQuery.error && (
        <Card className="mb-5">
          <ErrorState error={detailQuery.error} onRetry={() => void detailQuery.refetch()} />
        </Card>
      )}

      {detailQuery.isPending && !detailQuery.error && (
        <Card>
          <LoadingRows rows={5} />
        </Card>
      )}

      {detail && detail.summary.totalCount === 0 && (
        <Card>
          <EmptyState
            icon="search_off"
            title="Keine Instanzen im Zeitraum"
            description="Dieser Workflow wurde im gewählten Zeitraum weder gestartet noch beendet."
          />
        </Card>
      )}

      {detail && detail.summary.totalCount > 0 && (
        <div className="flex flex-col gap-[18px]">
          <div className="grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(168px,1fr))]">
            <Card className="px-[17px] py-4">
              <SectionLabel>Instanzen</SectionLabel>
              <div className="font-display mt-1.5 text-[28px] font-semibold tracking-[-0.02em]">
                {formatNumber(detail.summary.totalCount)}
              </div>
              <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1">
                {outcomeSlices(detail.summary).map((slice) => (
                  <span
                    key={slice.key}
                    className="text-muted inline-flex items-center gap-1 text-[12px]"
                  >
                    <Dot tone={slice.tone} size={7} />
                    {slice.label}
                    <strong className="text-text font-mono">{formatNumber(slice.value)}</strong>
                  </span>
                ))}
              </div>
            </Card>

            <Card className="px-[17px] py-4 sm:col-span-2">
              <SectionLabel>Durchlaufzeit</SectionLabel>
              {detail.summary.cycleTime ? (
                <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-2 text-[13px] sm:grid-cols-4">
                  <Metric label="Median" value={formatDurationSeconds(detail.summary.cycleTime.medianSeconds)} />
                  <Metric label="p90" value={formatDurationSeconds(detail.summary.cycleTime.p90Seconds)} />
                  <Metric label="Mittel" value={formatDurationSeconds(detail.summary.cycleTime.meanSeconds)} />
                  <Metric label="Maximum" value={formatDurationSeconds(detail.summary.cycleTime.maxSeconds)} />
                </dl>
              ) : (
                <p className="text-muted mt-2 text-[13px]">
                  Noch keine abgeschlossene Instanz in diesem Zeitraum.
                </p>
              )}
            </Card>
          </div>

          <Card>
            <CardHeader
              icon="timeline"
              title="Wartezeit je Schritt"
              actions={
                <span className="text-muted text-[11.5px]">
                  Balken: Median · Marke: p90
                </span>
              }
            />

            {nodes.length === 0 ? (
              <EmptyState
                icon="rule"
                title="Keine Schritte gemessen"
                description="Im Zeitraum wurde kein Schritt dieses Workflows vollständig durchlaufen."
              />
            ) : (
              <ul className="m-0 flex list-none flex-col gap-3.5 px-[18px] py-4">
                {nodes.map((node) => (
                  <WaitTimeBar key={node.flowNodeId} node={node} scaleMax={waitScale} />
                ))}
              </ul>
            )}
          </Card>

          {nodes.length > 0 && (
            <Card>
              <CardHeader icon="format_list_bulleted" title="Schritte im Zeitraum" />
              <div
                className={`bg-surface-2 text-muted grid ${NODE_GRID} gap-3 px-[18px] py-2.5 font-mono text-[10px] font-semibold tracking-[0.09em] uppercase`}
              >
                <div>Schritt</div>
                <div>Durchläufe</div>
                <div>Median</div>
                <div>p90</div>
                <div>Mittel</div>
                <div>Wartend</div>
              </div>
              {nodes.map((node) => (
                <div
                  key={node.flowNodeId}
                  className={`border-border grid ${NODE_GRID} items-center gap-3 border-t px-[18px] py-3`}
                >
                  <div className="min-w-0">
                    <div className="truncate text-[13.5px] font-semibold">{flowNodeLabel(node)}</div>
                    <div className="text-faint mt-0.5 truncate font-mono text-[11px]">
                      {node.flowNodeId}
                    </div>
                  </div>
                  <div className="font-mono text-[12.5px]">{formatNumber(node.executionCount)}</div>
                  <div className="text-[12.5px] whitespace-nowrap">
                    {formatDurationSeconds(node.waitTime?.medianSeconds)}
                  </div>
                  <div className="text-[12.5px] whitespace-nowrap">
                    {formatDurationSeconds(node.waitTime?.p90Seconds)}
                  </div>
                  <div className="text-[12.5px] whitespace-nowrap">
                    {formatDurationSeconds(node.waitTime?.meanSeconds)}
                  </div>
                  <div className="text-wait font-mono text-[12.5px] font-semibold">
                    {formatNumber(node.waitingTokenCount)}
                  </div>
                </div>
              ))}
            </Card>
          )}

          <Card>
            <CardHeader
              icon="today"
              title="Gestartet und beendet je Tag"
              actions={
                <span className="text-muted inline-flex items-center gap-3 text-[11.5px]">
                  <span className="inline-flex items-center gap-1">
                    <Dot tone="run" size={7} /> gestartet
                  </span>
                  <span className="inline-flex items-center gap-1">
                    <Dot tone="done" size={7} /> beendet
                  </span>
                </span>
              }
            />
            {timeline.length === 0 ? (
              <EmptyState
                icon="today"
                title="Kein Verlauf im Zeitraum"
                description="Für diesen Zeitraum liegen keine Tageswerte vor."
              />
            ) : (
              <Timeline points={timeline} />
            )}
          </Card>
        </div>
      )}
    </PageContainer>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-muted text-[11.5px]">{label}</dt>
      <dd className="m-0 text-[14px] font-semibold">{value}</dd>
    </div>
  );
}

/**
 * Ein Schritt im Wartezeitdiagramm.
 *
 * Der gefüllte Balken ist der Median, die schmale Marke der p90 auf derselben Skala —
 * beide Werte stehen zusätzlich als Text daneben, damit die Zeile auch ohne die Grafik
 * vollständig ist.
 */
function WaitTimeBar({ node, scaleMax }: { node: FlowNodeAnalyticsDto; scaleMax: number }) {
  const median = node.waitTime?.medianSeconds ?? 0;
  const p90 = node.waitTime?.p90Seconds ?? 0;

  return (
    <li>
      <div className="flex items-baseline justify-between gap-3">
        <span className="min-w-0 truncate text-[13.5px] font-semibold" title={node.flowNodeId}>
          {flowNodeLabel(node)}
        </span>
        <span className="text-muted text-[12px] whitespace-nowrap">
          Median {formatDurationSeconds(node.waitTime?.medianSeconds)} · p90{' '}
          {formatDurationSeconds(node.waitTime?.p90Seconds)}
        </span>
      </div>
      <div className="bg-surface-2 relative mt-1.5 h-2.5 w-full overflow-hidden rounded-full" aria-hidden="true">
        <div
          className="h-full rounded-full"
          style={{ width: `${barWidthPercent(median, scaleMax)}%`, background: toneColor('accent') }}
        />
        {p90 > 0 && (
          <span
            className="absolute top-0 h-full w-[2px]"
            style={{
              left: `calc(${barWidthPercent(p90, scaleMax)}% - 1px)`,
              background: toneColor('wait'),
            }}
          />
        )}
      </div>
    </li>
  );
}

/** Zwei schmale Balken je Tag auf gemeinsamer Skala; die Zahlen stehen im Klartext an der Säule. */
function Timeline({ points }: { points: readonly AnalyticsDayPointDto[] }) {
  const scaleMax = timelineScaleMax(points);

  return (
    <div className="flex items-end gap-2 overflow-x-auto px-[18px] py-4">
      {points.map((point) => {
        const description = `${point.day}: ${point.startedCount} gestartet, ${point.finishedCount} beendet`;
        return (
          <div key={point.day} className="flex w-[38px] flex-none flex-col items-center gap-1">
            <div
              role="img"
              aria-label={description}
              title={description}
              className="flex h-[76px] w-full items-end justify-center gap-[3px]"
            >
              <span
                className="w-[8px] rounded-t-[2px]"
                style={{
                  height: `${barWidthPercent(point.startedCount, scaleMax)}%`,
                  background: toneColor('run'),
                }}
              />
              <span
                className="w-[8px] rounded-t-[2px]"
                style={{
                  height: `${barWidthPercent(point.finishedCount, scaleMax)}%`,
                  background: toneColor('done'),
                }}
              />
            </div>
            <span className="text-faint font-mono text-[10px] whitespace-nowrap">
              {dayLabel(point.day)}
            </span>
          </div>
        );
      })}
    </div>
  );
}
