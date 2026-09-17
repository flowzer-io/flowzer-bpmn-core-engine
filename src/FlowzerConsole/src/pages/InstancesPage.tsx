import { useNavigate } from '@tanstack/react-router';
import { useMemo, useState } from 'react';

import {
  INSTANCE_GRID,
  INSTANCE_SELECTION_CELL,
  InstanceListRow,
} from '@/components/instances/InstanceListRow';
import { InstanceSelectionBar } from '@/components/instances/InstanceSelectionBar';
import { Card, EmptyState } from '@/components/ui/Card';
import { SearchInput } from '@/components/ui/Field';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { Segmented } from '@/components/ui/Segmented';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import { instanceBucket, type InstanceBucket } from '@/lib/api/normalize';
import { useInstances } from '@/lib/api/queries';
import { parseApiDate, shortId } from '@/lib/format';
import { nodeLabel } from '@/lib/bpmnModel';
import { isMigrationCandidate } from '@/lib/instanceMigration';
import { BUCKET_LABEL, currentToken, useDefinitionModels } from '@/lib/instanceView';

type Filter = InstanceBucket | 'all';

export function InstancesPage() {
  const navigate = useNavigate();
  const [filter, setFilter] = useState<Filter>('active');
  const [search, setSearch] = useState('');
  const [selectedIds, setSelectedIds] = useState<ReadonlySet<string>>(() => new Set());

  const instancesQuery = useInstances();
  const instances = useMemo(() => instancesQuery.data ?? [], [instancesQuery.data]);

  // Die BPMN-Modelle liefern ausschließlich verständliche Schrittnamen. Bei
  // Verzweigungen wäre eine lineare Fortschrittszahl fachlich falsch.
  const models = useDefinitionModels(useMemo(
    () => instances.filter((instance) => instance.canInspect === true).map((instance) => instance.definitionId),
    [instances],
  ));

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

  // Die Auswahlspalte erscheint nur, wenn überhaupt etwas auszuwählen ist — sonst stünde
  // in jeder Zeile eine leere Spalte, die nichts erklärt.
  const selectable = useMemo(
    () => new Set(visible.filter(isMigrationCandidate).map((instance) => instance.instanceId)),
    [visible],
  );
  // Aus dem aktuellen Bestand abgeleitet: Endet eine ausgewählte Instanz inzwischen, fällt sie
  // aus der Auswahl, statt ohne Kästchen — und damit unabwählbar — mitgezählt zu werden.
  const selected = useMemo(
    () => instances.filter((instance) => selectedIds.has(instance.instanceId) && isMigrationCandidate(instance)),
    [instances, selectedIds],
  );

  function toggleSelected(instanceId: string, on: boolean) {
    setSelectedIds((previous) => {
      const next = new Set(previous);
      if (on) next.add(instanceId);
      else next.delete(instanceId);
      return next;
    });
  }

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

      <div className="mb-4 flex flex-col items-stretch gap-3 sm:flex-row sm:items-center sm:gap-3.5">
        <Segmented
          options={filterOptions}
          value={filter}
          onChange={setFilter}
          aria-label="Instanzen filtern"
        />
        <span className="hidden flex-1 sm:block" />
        <SearchInput
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Instanz-ID oder Workflow …"
          wrapperClassName="py-2 min-w-[260px]"
        />
      </div>

      {selected.length > 0 && (
        <InstanceSelectionBar selected={selected} onClear={() => setSelectedIds(new Set())} />
      )}

      <Card>
        <div className="bg-surface-2 flex items-center max-md:hidden">
          {selectable.size > 0 && <div className={INSTANCE_SELECTION_CELL} />}
          <div
            className={`text-muted grid flex-1 ${INSTANCE_GRID} items-center gap-4 px-[18px] py-2.5 font-mono text-[10.5px] font-semibold tracking-[0.1em] uppercase`}
          >
            <div>Workflow</div>
            <div>Aktueller Schritt</div>
            <div>Warteobjekte</div>
            <div>Status</div>
            <div>Gestartet</div>
          </div>
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
              instances.length === 0 ? 'Keine sichtbaren Vorgänge' : 'Keine Instanzen in dieser Ansicht'
            }
            description={
              instances.length === 0
                ? 'Starte einen deployten Workflow, um hier die erste Instanz zu sehen.'
                : 'Wechsle den Filter oder passe die Suche an.'
            }
          />
        )}

        {visible.map((instance) => {
          const model = models.get(instance.definitionId);
          const token = currentToken(instance);

          const stepName = instance.canInspect === false ? 'Vorgangsübersicht' : model
            ? nodeLabel(model, token?.currentFlowNodeId)
            : (token?.currentFlowElement?.Name ?? token?.currentFlowNodeId ?? '—');

          return (
            <InstanceListRow
              key={instance.instanceId}
              instance={instance}
              stepName={stepName}
              onOpen={() => void navigate({ to: `/instances/${instance.instanceId}` })}
              selection={
                selectable.size > 0
                  ? {
                      selectable: selectable.has(instance.instanceId),
                      selected: selectedIds.has(instance.instanceId),
                      onChange: (on) => toggleSelected(instance.instanceId, on),
                    }
                  : undefined
              }
            />
          );
        })}
      </Card>
    </PageContainer>
  );
}
