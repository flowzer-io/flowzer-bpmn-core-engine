import { useNavigate } from '@tanstack/react-router';
import { useMemo, useState } from 'react';

import { Card, EmptyState } from '@/components/ui/Card';
import { Chip, toneColor } from '@/components/ui/Chip';
import { SearchInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { Segmented } from '@/components/ui/Segmented';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import { instanceBucket, type InstanceBucket } from '@/lib/api/normalize';
import { useInstances } from '@/lib/api/queries';
import { formatTimestamp, parseApiDate, shortId } from '@/lib/format';
import { nodeLabel } from '@/lib/bpmnModel';
import {
  BUCKET_LABEL,
  BUCKET_TONE,
  currentToken,
  instanceProgress,
  STATE_LABEL,
  useDefinitionModels,
  waitingBadges,
} from '@/lib/instanceView';

type Filter = InstanceBucket | 'all';

const GRID = 'grid-cols-[minmax(0,1.5fr)_minmax(0,1.5fr)_150px_104px_116px]';

export function InstancesPage() {
  const navigate = useNavigate();
  const [filter, setFilter] = useState<Filter>('active');
  const [search, setSearch] = useState('');

  const instancesQuery = useInstances();
  const instances = useMemo(() => instancesQuery.data ?? [], [instancesQuery.data]);

  // Die BPMN-Modelle liefern Schrittnamen und Gesamtzahl für den Fortschritt.
  const models = useDefinitionModels(useMemo(() => instances.map((i) => i.definitionId), [instances]));

  const counts = useMemo(() => {
    const result = { all: instances.length, active: 0, done: 0, error: 0 };
    for (const instance of instances) result[instanceBucket(instance.state)] += 1;
    return result;
  }, [instances]);

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase();

    return instances
      .filter((instance) => filter === 'all' || instanceBucket(instance.state) === filter)
      .filter(
        (instance) =>
          term.length === 0 ||
          instance.relatedDefinitionName.toLowerCase().includes(term) ||
          instance.instanceId.toLowerCase().includes(term) ||
          shortId(instance.instanceId).toLowerCase().includes(term),
      )
      .sort((a, b) => {
        const aTime = parseApiDate(a.startedAt)?.getTime() ?? 0;
        const bTime = parseApiDate(b.startedAt)?.getTime() ?? 0;
        return bTime - aTime;
      });
  }, [instances, filter, search]);

  const filterOptions = [
    { value: 'all' as const, label: 'Alle', count: counts.all },
    { value: 'active' as const, label: 'Aktiv', count: counts.active },
    { value: 'done' as const, label: 'Fertig', count: counts.done },
    { value: 'error' as const, label: 'Fehler', count: counts.error },
  ];

  return (
    <PageContainer>
      <PageHeader
        title="Instanzen"
        description={`Laufende und abgeschlossene Prozesse — Ansicht: ${filter === 'all' ? 'Alle' : BUCKET_LABEL[filter]}`}
      />

      <div className="mb-4 flex items-center gap-3.5">
        <Segmented
          options={filterOptions}
          value={filter}
          onChange={setFilter}
          aria-label="Instanzen filtern"
        />
        <span className="flex-1" />
        <SearchInput
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Instanz-ID oder Workflow …"
          wrapperClassName="py-2 min-w-[260px]"
        />
      </div>

      <Card>
        <div
          className={`bg-surface-2 text-muted grid ${GRID} items-center gap-4 px-[18px] py-2.5 font-mono text-[10.5px] font-semibold tracking-[0.1em] uppercase`}
        >
          <div>Workflow</div>
          <div>Aktueller Schritt</div>
          <div>Warteobjekte</div>
          <div>Status</div>
          <div>Gestartet</div>
        </div>

        {instancesQuery.isPending && <LoadingRows rows={5} />}

        {instancesQuery.error && (
          <ErrorState error={instancesQuery.error} onRetry={() => void instancesQuery.refetch()} />
        )}

        {!instancesQuery.isPending && !instancesQuery.error && visible.length === 0 && (
          <EmptyState
            className="border-border border-t"
            icon="filter_alt_off"
            title={
              instances.length === 0 ? 'Es läuft noch keine Instanz' : 'Keine Instanzen in dieser Ansicht'
            }
            description={
              instances.length === 0
                ? 'Starte einen deployten Workflow, um hier die erste Instanz zu sehen.'
                : 'Wechsle den Filter oder passe die Suche an.'
            }
          />
        )}

        {visible.map((instance) => {
          const bucket = instanceBucket(instance.state);
          const tone = BUCKET_TONE[bucket];
          const model = models.get(instance.definitionId);
          const progress = instanceProgress(instance, model);
          const token = currentToken(instance);
          const badges = waitingBadges(instance);

          const stepName = model
            ? nodeLabel(model, token?.currentFlowNodeId)
            : (token?.currentFlowElement?.Name ?? token?.currentFlowNodeId ?? '—');

          return (
            <button
              key={instance.instanceId}
              type="button"
              onClick={() => void navigate({ to: `/instances/${instance.instanceId}` })}
              className={`border-border hover:bg-inset text-text grid w-full ${GRID} cursor-pointer items-center gap-4 border-t border-x-0 border-b-0 bg-transparent px-[18px] py-3.5 text-left`}
            >
              <div className="min-w-0">
                <div className="truncate text-[14.5px] font-semibold">{instance.relatedDefinitionName}</div>
                <div className="text-faint mt-0.5 font-mono text-xs">#{shortId(instance.instanceId)}</div>
              </div>

              <div className="min-w-0">
                <div className="text-muted mb-1.5 truncate text-[13px]">
                  {bucket === 'done' ? 'Abgeschlossen' : stepName}
                </div>
                <div className="bg-surface-2 h-[5px] overflow-hidden rounded-[5px]">
                  <div
                    className="h-full rounded-[5px] transition-[width] duration-500"
                    style={{
                      width: progress.ratio === null ? '100%' : `${Math.round(progress.ratio * 100)}%`,
                      background: progress.ratio === null ? 'var(--border-strong)' : toneColor(tone),
                    }}
                    title={
                      progress.total > 0
                        ? `${progress.visited} von ${progress.total} Elementen durchlaufen`
                        : 'Fortschritt unbekannt — Modell nicht geladen'
                    }
                  />
                </div>
              </div>

              <div className="flex gap-1.5">
                {badges.map((badge) => (
                  <span
                    key={badge.icon}
                    title={`${badge.count} ${badge.title}`}
                    className="bg-surface-2 text-muted inline-flex items-center gap-1 rounded-[7px] px-2 py-0.5 text-xs font-semibold"
                  >
                    <Icon name={badge.icon} size={15} />
                    {badge.count}
                  </span>
                ))}
              </div>

              <div>
                <Chip tone={tone}>{STATE_LABEL[instance.state]}</Chip>
              </div>

              <div className="text-muted font-mono text-xs">{formatTimestamp(instance.startedAt)}</div>
            </button>
          );
        })}
      </Card>
    </PageContainer>
  );
}
