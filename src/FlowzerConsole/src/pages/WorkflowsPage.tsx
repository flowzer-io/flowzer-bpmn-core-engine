import { useNavigate } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { toast } from 'sonner';

import { BpmnThumbnail } from '@/components/bpmn/BpmnThumbnail';
import { Button } from '@/components/ui/Button';
import { Card, EmptyState } from '@/components/ui/Card';
import { Chip, Dot, toneSurface, type Tone } from '@/components/ui/Chip';
import { SearchInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { Segmented } from '@/components/ui/Segmented';
import { ErrorState, Skeleton } from '@/components/ui/States';
import { useCreateDefinition, useDefinitions, useInstances, useStartInstance } from '@/lib/api/queries';
import type { ExtendedBpmnMetaDefinitionDto, VersionDto } from '@/lib/api/types';
import { instanceBucket } from '@/lib/api/normalize';
import { formatRelative, parseApiDate } from '@/lib/format';
import { iconForLabel } from '@/lib/taskView';

type SortKey = 'updated' | 'name' | 'active';

const SORT_OPTIONS = [
  { value: 'updated' as const, label: 'Zuletzt geändert' },
  { value: 'name' as const, label: 'Name' },
  { value: 'active' as const, label: 'Aktivität' },
];

type DeployState = 'deployed' | 'outdated' | 'draft';

const DEPLOY_LABEL: Record<DeployState, string> = {
  deployed: 'Aktiv',
  outdated: 'Neue Version',
  draft: 'Entwurf',
};

const DEPLOY_TONE: Record<DeployState, Tone> = {
  deployed: 'done',
  outdated: 'wait',
  draft: 'muted',
};

function versionLabel(version: VersionDto | null | undefined): string {
  return version ? `v${version.major}.${version.minor}` : '—';
}

function deployState(definition: ExtendedBpmnMetaDefinitionDto): DeployState {
  if (!definition.deployedId || !definition.deployedVersion) return 'draft';

  const latest = definition.latestVersion;
  if (!latest) return 'deployed';

  const isNewer =
    latest.major > definition.deployedVersion.major ||
    (latest.major === definition.deployedVersion.major && latest.minor > definition.deployedVersion.minor);

  return isNewer ? 'outdated' : 'deployed';
}

export function WorkflowsPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState<SortKey>('updated');

  const definitionsQuery = useDefinitions();
  const instancesQuery = useInstances();
  const createDefinition = useCreateDefinition();
  const startInstance = useStartInstance();

  /** Anzahl laufender Instanzen je Definition — für die Fußzeile der Karten. */
  const activeByDefinition = useMemo(() => {
    const counts = new Map<string, number>();
    for (const instance of instancesQuery.data ?? []) {
      if (instanceBucket(instance.state) !== 'active') continue;
      counts.set(instance.relatedDefinitionId, (counts.get(instance.relatedDefinitionId) ?? 0) + 1);
    }
    return counts;
  }, [instancesQuery.data]);

  const definitions = useMemo(() => {
    const term = search.trim().toLowerCase();
    const filtered = (definitionsQuery.data ?? []).filter(
      (definition) =>
        term.length === 0 ||
        definition.name.toLowerCase().includes(term) ||
        definition.definitionId.toLowerCase().includes(term) ||
        (definition.description ?? '').toLowerCase().includes(term),
    );

    return [...filtered].sort((a, b) => {
      if (sort === 'name') return a.name.localeCompare(b.name, 'de');
      if (sort === 'active') {
        return (activeByDefinition.get(b.definitionId) ?? 0) - (activeByDefinition.get(a.definitionId) ?? 0);
      }
      const aDate = parseApiDate(a.latestVersionDateTime)?.getTime() ?? 0;
      const bDate = parseApiDate(b.latestVersionDateTime)?.getTime() ?? 0;
      return bDate - aDate;
    });
  }, [definitionsQuery.data, search, sort, activeByDefinition]);

  const totalActive = useMemo(
    () => [...activeByDefinition.values()].reduce((sum, value) => sum + value, 0),
    [activeByDefinition],
  );

  return (
    <PageContainer>
      <PageHeader
        title="Workflows"
        description={
          definitionsQuery.isPending
            ? 'Katalog wird geladen …'
            : `${definitions.length} Prozessdefinition${definitions.length === 1 ? '' : 'en'} · ${totalActive} laufende Instanz${totalActive === 1 ? '' : 'en'}`
        }
        actions={
          <Button
            variant="primary"
            icon="add"
            loading={createDefinition.isPending}
            onClick={() => {
              createDefinition.mutate(undefined, {
                onSuccess: (meta) => {
                  toast.success('Neuer Workflow angelegt');
                  void navigate({ to: `/workflows/${encodeURIComponent(meta.definitionId)}` });
                },
                onError: (error) =>
                  toast.error('Workflow konnte nicht angelegt werden', {
                    description: error instanceof Error ? error.message : undefined,
                  }),
              });
            }}
          >
            Neuer Workflow
          </Button>
        }
      />

      <div className="mb-5 flex items-center gap-2.5">
        <SearchInput
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Workflow filtern …"
          wrapperClassName="max-w-[340px] flex-1 py-2"
        />
        <span className="flex-1" />
        <Segmented options={SORT_OPTIONS} value={sort} onChange={setSort} aria-label="Sortierung" />
      </div>

      {definitionsQuery.error && (
        <Card>
          <ErrorState error={definitionsQuery.error} onRetry={() => void definitionsQuery.refetch()} />
        </Card>
      )}

      {definitionsQuery.isPending && (
        <div className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(304px,1fr))]">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-[260px] rounded-[var(--r-lg)]" />
          ))}
        </div>
      )}

      {!definitionsQuery.isPending && !definitionsQuery.error && definitions.length === 0 && (
        <Card>
          <EmptyState
            icon={search ? 'search_off' : 'schema'}
            title={search ? 'Kein Workflow gefunden' : 'Noch keine Workflows'}
            description={
              search
                ? 'Passe den Suchbegriff an oder lege einen neuen Workflow an.'
                : 'Lege einen Workflow an, modelliere ihn im Editor und deploye ihn anschließend.'
            }
          />
        </Card>
      )}

      <div className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(304px,1fr))]">
        {definitions.map((definition) => {
          const state = deployState(definition);
          const activeCount = activeByDefinition.get(definition.definitionId) ?? 0;
          const canStart = Boolean(definition.deployedId);

          return (
            <Card
              key={definition.definitionId}
              className="hover:border-accent hover:shadow-pop group cursor-pointer p-0 transition-[border-color,box-shadow,transform] duration-150 hover:-translate-y-0.5"
              onClick={() => void navigate({ to: `/workflows/${encodeURIComponent(definition.definitionId)}` })}
            >
              <div className="relative h-[120px]">
                <BpmnThumbnail versionGuid={definition.deployedId ?? null} className="h-full w-full" />
                <span className="absolute top-2.5 right-2.5">
                  <Chip tone={DEPLOY_TONE[state]}>{DEPLOY_LABEL[state]}</Chip>
                </span>
              </div>

              <div className="px-[17px] py-[15px]">
                <div className="flex items-center gap-2.5">
                  <span
                    className="text-accent grid h-8 w-8 flex-none place-items-center rounded-[9px]"
                    style={{ background: toneSurface('accent', 11) }}
                  >
                    <Icon name={iconForLabel(`${definition.name} ${definition.description ?? ''}`)} size={19} />
                  </span>
                  <div className="font-display min-w-0 truncate text-[15.5px] font-semibold">
                    {definition.name}
                  </div>
                </div>

                <div className="text-muted mt-2.5 min-h-[39px] text-[13px] leading-normal">
                  {definition.description?.trim() || (
                    <span className="text-faint">Keine Beschreibung hinterlegt.</span>
                  )}
                </div>

                <div className="border-border mt-3.5 flex items-center gap-2.5 border-t pt-3.5">
                  <span className="bg-surface-2 text-muted rounded-md px-1.5 py-0.5 font-mono text-[11.5px] font-semibold">
                    {versionLabel(definition.latestVersion)}
                  </span>
                  <span className="flex-1" />
                  {activeCount > 0 && (
                    <span className="text-muted inline-flex items-center gap-1.5 text-[12.5px]">
                      <Dot tone="run" size={6} />
                      {activeCount} aktiv
                    </span>
                  )}
                  <span className="text-faint text-xs whitespace-nowrap">
                    {formatRelative(definition.latestVersionDateTime)}
                  </span>
                </div>

                <div className="mt-3 flex gap-2 opacity-0 transition-opacity duration-150 group-hover:opacity-100 focus-within:opacity-100">
                  <Button
                    size="sm"
                    icon="rocket_launch"
                    className="flex-1"
                    disabled={!canStart || startInstance.isPending}
                    title={canStart ? undefined : 'Der Workflow muss zuerst deployt werden.'}
                    onClick={(event) => {
                      event.stopPropagation();
                      startInstance.mutate(definition.definitionId, {
                        onSuccess: (instance) =>
                          toast.success(`„${definition.name}" gestartet`, {
                            action: {
                              label: 'Öffnen',
                              onClick: () => void navigate({ to: `/instances/${instance.instanceId}` }),
                            },
                          }),
                        onError: (error) =>
                          toast.error('Start fehlgeschlagen', {
                            description: error instanceof Error ? error.message : undefined,
                          }),
                      });
                    }}
                  >
                    Starten
                  </Button>
                  <Button
                    size="sm"
                    icon="edit"
                    className="flex-1"
                    onClick={(event) => {
                      event.stopPropagation();
                      void navigate({ to: `/workflows/${encodeURIComponent(definition.definitionId)}` });
                    }}
                  >
                    Bearbeiten
                  </Button>
                </div>
              </div>
            </Card>
          );
        })}
      </div>
    </PageContainer>
  );
}
