import { useNavigate } from '@tanstack/react-router';
import { useId, useMemo, useState } from 'react';

import { Card, EmptyState } from '@/components/ui/Card';
import { Dot, toneColor } from '@/components/ui/Chip';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { Segmented } from '@/components/ui/Segmented';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import { useAnalyticsOverview } from '@/lib/api/queries';
import type { WorkflowAnalyticsSummaryDto } from '@/lib/api/types';
import {
  barWidthPercent,
  dateInputValue,
  outcomeSlices,
  outcomeTotal,
  rangeLabel,
  rangeQuery,
  rangeSelection,
  resolveRange,
  RANGE_PRESETS,
  type AnalyticsSearch,
} from '@/lib/analytics';
import { formatDurationSeconds, formatNumber } from '@/lib/format';
import { useCan } from '@/stores/session';

const OVERVIEW_GRID = 'grid-cols-[minmax(140px,1.7fr)_74px_minmax(200px,2.1fr)_minmax(104px,1fr)]';

const RANGE_OPTIONS = [
  ...RANGE_PRESETS.map((days) => ({ value: String(days), label: `${days} Tage` })),
  { value: 'custom', label: 'Eigener Zeitraum' },
];

interface AnalyticsPageProps {
  search: AnalyticsSearch;
}

/**
 * Die Übersicht aller Workflows im gewählten Zeitraum.
 *
 * Der Zeitraum steht in der Adresse und nicht im Komponentenzustand: Eine Auswertung ist
 * etwas, das man weitergibt — wer den Verweis öffnet, sieht dieselben Zahlen.
 */
export function AnalyticsPage({ search }: AnalyticsPageProps) {
  const navigate = useNavigate();
  const mayOperate = useCan()('operator');
  const fromFieldId = useId();
  const toFieldId = useId();

  // Der Zeitraum hängt an den Suchparametern; er darf sich nicht bei jedem Rendern
  // verschieben, sonst wechselte der Query-Key unter der laufenden Abfrage.
  const range = useMemo(() => resolveRange(search, new Date()), [search]);
  const [customOpen, setCustomOpen] = useState(() => range.days === 'custom');

  // Ohne Betriebsrolle lehnt die API ab. Die Abfrage gar nicht erst zu stellen ist
  // ehrlicher als eine Seite voller Fehlermeldungen.
  const overviewQuery = useAnalyticsOverview(rangeQuery(range), { enabled: mayOperate });
  const workflows = overviewQuery.data?.workflows ?? [];

  const applySearch = (next: AnalyticsSearch) => {
    void navigate({ to: '/analytics', search: next });
  };

  const chooseRange = (value: string) => {
    if (value === 'custom') {
      // Der eigene Zeitraum gilt erst mit beiden Datumsangaben; bis dahin bleibt die
      // bisherige Auswertung stehen, statt dass die Seite kurz leer wird.
      setCustomOpen(true);
      return;
    }

    setCustomOpen(false);
    applySearch({ days: Number(value) });
  };

  const changeBoundary = (key: 'from' | 'to', value: string) => {
    const next: AnalyticsSearch = {
      from: dateInputValue(range.fromIso),
      to: dateInputValue(range.toIso),
      [key]: value,
    };
    if (next.from && next.to) applySearch(next);
  };

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
        title="Auswertungen"
        description={`Durchlaufzeiten und Ausgänge je Workflow · ${rangeLabel(range)}`}
        actions={
          <Segmented
            aria-label="Zeitraum"
            options={RANGE_OPTIONS}
            value={customOpen ? 'custom' : rangeSelection(range)}
            onChange={chooseRange}
          />
        }
      />

      {customOpen && (
        <div className="mb-4 flex flex-wrap items-end gap-3">
          <div className="min-w-[150px]">
            <FieldLabel htmlFor={fromFieldId}>Von</FieldLabel>
            <TextInput
              id={fromFieldId}
              type="date"
              value={dateInputValue(range.fromIso)}
              onChange={(event) => changeBoundary('from', event.target.value)}
            />
          </div>
          <div className="min-w-[150px]">
            <FieldLabel htmlFor={toFieldId}>Bis</FieldLabel>
            <TextInput
              id={toFieldId}
              type="date"
              value={dateInputValue(range.toIso)}
              onChange={(event) => changeBoundary('to', event.target.value)}
            />
          </div>
        </div>
      )}

      <Card>
        <div
          className={`bg-surface-2 text-muted grid ${OVERVIEW_GRID} gap-3.5 px-[18px] py-2.5 font-mono text-[10px] font-semibold tracking-[0.09em] uppercase`}
        >
          <div>Workflow</div>
          <div>Gesamt</div>
          <div>Ausgang</div>
          <div>Median-Durchlaufzeit</div>
        </div>

        {overviewQuery.isPending && <LoadingRows rows={4} />}

        {overviewQuery.error && (
          <ErrorState
            className="border-border border-t"
            error={overviewQuery.error}
            onRetry={() => void overviewQuery.refetch()}
          />
        )}

        {!overviewQuery.isPending && !overviewQuery.error && workflows.length === 0 && (
          <EmptyState
            className="border-border border-t"
            icon="search_off"
            title="Keine Instanzen im Zeitraum"
            description="In diesem Zeitraum wurde keine Instanz gestartet oder beendet. Wählen Sie einen längeren Zeitraum."
          />
        )}

        {workflows.map((workflow) => (
          <button
            key={workflow.metaDefinitionId}
            type="button"
            onClick={() =>
              void navigate({
                to: `/analytics/${encodeURIComponent(workflow.metaDefinitionId)}`,
                search,
              })
            }
            className={`border-border hover:bg-inset grid w-full ${OVERVIEW_GRID} cursor-pointer items-center gap-3.5 border-t border-x-0 border-b-0 bg-transparent px-[18px] py-3.5 text-left`}
          >
            <div className="min-w-0">
              <div className="truncate text-[13.5px] font-semibold">{workflow.name}</div>
              <div className="text-faint mt-0.5 truncate font-mono text-[11.5px]">
                {workflow.metaDefinitionId}
              </div>
            </div>
            <div className="font-mono text-[13.5px] font-semibold">
              {formatNumber(workflow.totalCount)}
            </div>
            <OutcomeDistribution summary={workflow} />
            <div className="text-[13px] font-semibold whitespace-nowrap">
              {formatDurationSeconds(workflow.cycleTime?.medianSeconds)}
              <span className="text-faint ml-1.5 font-mono text-[11px]">
                {workflow.cycleTime ? `n=${formatNumber(workflow.cycleTime.sampleCount)}` : 'keine Messung'}
              </span>
            </div>
          </button>
        ))}
      </Card>
    </PageContainer>
  );
}

/**
 * Die Verteilung einer Zeile: ein Balken für den schnellen Vergleich, darunter jede
 * Zahl im Klartext. Die Farbe allein trägt hier nichts — sie wiederholt nur, was daneben
 * steht, damit die Zeile auch ohne Farbwahrnehmung vollständig lesbar bleibt.
 */
function OutcomeDistribution({ summary }: { summary: WorkflowAnalyticsSummaryDto }) {
  const slices = outcomeSlices(summary);
  const total = outcomeTotal(summary);

  return (
    <div className="min-w-0">
      <div className="bg-surface-2 flex h-2 overflow-hidden rounded-full" aria-hidden="true">
        {slices.map((slice) => (
          <div
            key={slice.key}
            title={`${slice.label}: ${slice.value}`}
            style={{ width: `${barWidthPercent(slice.value, total)}%`, background: toneColor(slice.tone) }}
          />
        ))}
      </div>
      <div className="mt-1.5 flex flex-wrap gap-x-3 gap-y-1">
        {slices.map((slice) => (
          <span key={slice.key} className="text-muted inline-flex items-center gap-1 text-[11.5px]">
            <Dot tone={slice.tone} size={7} />
            {slice.label}
            <strong className="text-text font-mono">{formatNumber(slice.value)}</strong>
          </span>
        ))}
      </div>
    </div>
  );
}
