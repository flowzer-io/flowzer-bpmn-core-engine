import * as Tabs from '@radix-ui/react-tabs';
import { useNavigate } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { toast } from 'sonner';

import { BpmnViewer } from '@/components/bpmn/BpmnViewer';
import { Button } from '@/components/ui/Button';
import { EmptyState, SectionLabel } from '@/components/ui/Card';
import { Chip, Dot, toneColor, toneSurface, type Tone } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { ErrorState, InlineSpinner } from '@/components/ui/States';
import { instanceBucket, isFailedToken, isLiveToken } from '@/lib/api/normalize';
import { useDefinitionXml, useInstance, useInstanceSubscriptions } from '@/lib/api/queries';
import type { ProcessVariables, TokenDto } from '@/lib/api/types';
import { nodeLabel, nodeTypeIcon, nodeTypeLabel, parseBpmn } from '@/lib/bpmnModel';
import { cn } from '@/lib/cn';
import { formatDueIn, formatTimestamp, formatVariableValue, parseApiDate, shortId } from '@/lib/format';
import { BUCKET_TONE, currentToken, STATE_LABEL, tokenMarkers } from '@/lib/instanceView';
import { useBreadcrumbs } from '@/stores/breadcrumbs';

interface InstanceDetailPageProps {
  instanceId: string;
}

type PanelTab = 'variables' | 'timeline' | 'subscriptions';

const TABS: { value: PanelTab; label: string; icon: string }[] = [
  { value: 'variables', label: 'Variablen', icon: 'data_object' },
  { value: 'timeline', label: 'Verlauf', icon: 'timeline' },
  { value: 'subscriptions', label: 'Warteobjekte', icon: 'notifications_active' },
];

export function InstanceDetailPage({ instanceId }: InstanceDetailPageProps) {
  const navigate = useNavigate();
  const [tab, setTab] = useState<PanelTab>('variables');

  const instanceQuery = useInstance(instanceId);
  const instance = instanceQuery.data;
  const xmlQuery = useDefinitionXml(instance?.definitionId);
  const subscriptionsQuery = useInstanceSubscriptions(instanceId);

  const model = useMemo(() => parseBpmn(xmlQuery.data), [xmlQuery.data]);
  const { markers, activeNodeIds } = useMemo(
    () => (instance ? tokenMarkers(instance) : { markers: {}, activeNodeIds: [] }),
    [instance],
  );

  useBreadcrumbs([
    { label: 'Instanzen', to: '/instances' },
    { label: instance?.relatedDefinitionName ?? 'Instanz' },
  ]);

  if (instanceQuery.isPending) {
    return (
      <div className="grid h-full place-items-center">
        <InlineSpinner label="Instanz wird geladen …" />
      </div>
    );
  }

  if (instanceQuery.error || !instance) {
    return (
      <div className="p-8">
        <ErrorState error={instanceQuery.error} onRetry={() => void instanceQuery.refetch()} />
      </div>
    );
  }

  const bucket = instanceBucket(instance.state);
  const tone = BUCKET_TONE[bucket];
  const active = currentToken(instance);

  // Prozessvariablen liegen am Token; das aktuelle Token ist die relevante Sicht.
  const variables: ProcessVariables = active?.variables ?? {};
  const variableEntries = Object.entries(variables);

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="border-border bg-surface flex flex-none flex-wrap items-center gap-3.5 gap-y-2.5 border-b px-6 py-3">
        <Button
          variant="ghost"
          size="sm"
          icon="arrow_back"
          title="Zurück zur Instanzliste"
          className="border-border h-[34px] w-[34px] border px-0"
          onClick={() => void navigate({ to: '/instances' })}
        >
          <span className="sr-only">Zurück</span>
        </Button>

        <div className="min-w-0">
          <div className="flex items-center gap-2.5">
            <span className="font-display truncate text-[17px] font-semibold">
              {instance.relatedDefinitionName}
            </span>
            <Chip tone={tone}>{STATE_LABEL[instance.state]}</Chip>
          </div>
          <div className="text-faint mt-0.5 font-mono text-xs">
            #{shortId(instance.instanceId)} · gestartet {formatTimestamp(instance.startedAt)}
            {instance.finishedAt && <> · beendet {formatTimestamp(instance.finishedAt)}</>}
          </div>
        </div>

        <span className="flex-1" />

        {instance.userTaskSubscriptionCount > 0 && (
          <Button
            variant="primary"
            size="sm"
            icon="assignment"
            onClick={() => void navigate({ to: '/tasks' })}
          >
            Offene Aufgabe bearbeiten
          </Button>
        )}

        <Button
          size="sm"
          icon="content_copy"
          title="Instanz-ID kopieren"
          onClick={() => {
            void navigator.clipboard.writeText(instance.instanceId);
            toast.success('Instanz-ID kopiert');
          }}
        >
          ID kopieren
        </Button>
      </div>

      <div className="flex min-h-0 flex-1">
        <div className="canvas-grid relative min-w-0 flex-1">
          {xmlQuery.isPending && (
            <div className="grid h-full place-items-center">
              <InlineSpinner label="Diagramm wird geladen …" />
            </div>
          )}

          {xmlQuery.error && (
            <div className="grid h-full place-items-center p-8">
              <ErrorState
                error={xmlQuery.error}
                title="Diagramm nicht verfügbar"
                onRetry={() => void xmlQuery.refetch()}
              />
            </div>
          )}

          {xmlQuery.data && (
            <BpmnViewer
              xml={xmlQuery.data}
              markers={markers}
              tokens={activeNodeIds}
              className="h-full w-full"
            />
          )}

          <div className="bg-surface/90 border-border text-muted absolute bottom-4 left-5 flex gap-3.5 rounded-[20px] border px-3.5 py-1.5 text-xs backdrop-blur-sm">
            <span className="inline-flex items-center gap-1.5">
              <span className="bg-accent h-2.5 w-2.5 rounded-full" />
              durchlaufen
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="border-accent animate-token-pulse h-2.5 w-2.5 rounded-full border-2" />
              aktiv
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="border-border-strong h-2.5 w-2.5 rounded-full border border-dashed" />
              ausstehend
            </span>
          </div>
        </div>

        <Tabs.Root
          value={tab}
          onValueChange={(value) => setTab(value as PanelTab)}
          className="border-border bg-surface flex w-[392px] min-h-0 flex-none flex-col border-l"
        >
          <Tabs.List className="border-border flex flex-none gap-0.5 border-b px-3 pt-2.5">
            {TABS.map((entry) => (
              <Tabs.Trigger
                key={entry.value}
                value={entry.value}
                className={cn(
                  'text-muted -mb-px inline-flex cursor-pointer items-center gap-1.5 border-none border-b-2',
                  'border-b-transparent bg-transparent px-3 py-2.5 text-[13px] font-semibold transition-colors',
                  'data-[state=active]:border-b-accent data-[state=active]:text-accent',
                )}
              >
                <Icon name={entry.icon} size={17} />
                {entry.label}
              </Tabs.Trigger>
            ))}
          </Tabs.List>

          <div className="min-h-0 flex-1 overflow-auto p-[18px]">
            <Tabs.Content value="variables">
              <div className="mb-3 flex items-center justify-between">
                <SectionLabel>Prozessvariablen</SectionLabel>
                {variableEntries.length > 0 && (
                  <button
                    type="button"
                    title="Als JSON kopieren"
                    onClick={() => {
                      void navigator.clipboard.writeText(JSON.stringify(variables, null, 2));
                      toast.success('Variablen kopiert');
                    }}
                    className="text-faint hover:text-text cursor-pointer border-none bg-transparent p-0"
                  >
                    <Icon name="content_copy" size={18} />
                  </button>
                )}
              </div>

              {variableEntries.length === 0 ? (
                <EmptyState
                  icon="data_object"
                  title="Keine Variablen"
                  description="Dieser Prozess führt derzeit keine Daten mit."
                />
              ) : (
                <div className="border-border overflow-hidden rounded-[var(--r)] border">
                  {variableEntries.map(([key, value], index) => (
                    <div
                      key={key}
                      className={cn(
                        'flex items-center gap-2.5 px-3 py-2.5 font-mono text-[12.5px]',
                        index > 0 && 'border-border border-t',
                      )}
                    >
                      <span className="text-muted flex-none">{key}</span>
                      <span className="flex-1" />
                      <span className="text-accent truncate text-right font-semibold">
                        {formatVariableValue(value)}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </Tabs.Content>

            <Tabs.Content value="timeline">
              <SectionLabel className="mb-3.5">Verlauf</SectionLabel>
              <Timeline tokens={instance.tokens} model={model} />
            </Tabs.Content>

            <Tabs.Content value="subscriptions">
              <SectionLabel className="mb-3.5">Aktive Warteobjekte</SectionLabel>

              {subscriptionsQuery.isPending && <InlineSpinner />}
              {subscriptionsQuery.error && (
                <ErrorState
                  error={subscriptionsQuery.error}
                  onRetry={() => void subscriptionsQuery.refetch()}
                />
              )}

              {subscriptionsQuery.data && <Subscriptions data={subscriptionsQuery.data} model={model} />}
            </Tabs.Content>
          </div>
        </Tabs.Root>
      </div>
    </div>
  );
}

function Timeline({ tokens, model }: { tokens: TokenDto[]; model: ReturnType<typeof parseBpmn> }) {
  const events = useMemo(
    () =>
      tokens
        // Die Engine führt zusätzlich ein Token auf Prozessebene ohne Flow-Node.
        // Es ist kein Prozessschritt und gehört nicht in den Verlauf.
        .filter((token) => Boolean(token.currentFlowNodeId))
        .map((token) => ({
          token,
          at: parseApiDate(token.lastStateChangeTime) ?? parseApiDate(token.startTime),
        }))
        .sort((a, b) => (a.at?.getTime() ?? 0) - (b.at?.getTime() ?? 0)),
    [tokens],
  );

  if (events.length === 0) {
    return <EmptyState icon="timeline" title="Kein Verlauf" description="Diese Instanz hat noch keine Schritte." />;
  }

  return (
    <div>
      {events.map(({ token, at }, index) => {
        const tone: Tone = isFailedToken(token) ? 'fail' : isLiveToken(token) ? 'run' : 'done';
        const node = token.currentFlowNodeId ? model.nodeById.get(token.currentFlowNodeId) : undefined;

        return (
          <div key={token.id} className="flex gap-3.5">
            <div className="flex flex-none flex-col items-center">
              <Dot tone={tone} size={12} halo className="mt-1" />
              {index < events.length - 1 && <span className="bg-border my-1 w-0.5 flex-1" />}
            </div>
            <div className="pb-[18px]">
              <div className="text-[13.5px] font-semibold">
                {nodeLabel(model, token.currentFlowNodeId)}
              </div>
              <div className="text-muted mt-0.5 text-[12.5px]">
                {nodeTypeLabel(node?.type)} · {token.state}
              </div>
              <div className="text-faint mt-1 font-mono text-[11.5px]">{formatTimestamp(at)}</div>
            </div>
          </div>
        );
      })}
    </div>
  );
}

interface SubscriptionsData {
  messages: { message: { name: string }; processId: string }[];
  signals: { signal: string; processId: string }[];
  timers: { id: string; flowNodeId: string; dueAt: string; remainingOccurrences?: number | null }[];
  services: TokenDto[];
  userTasks: TokenDto[];
}

function Subscriptions({ data, model }: { data: SubscriptionsData; model: ReturnType<typeof parseBpmn> }) {
  const rows = [
    ...data.userTasks.map((token) => ({
      key: `usertask-${token.id}`,
      icon: 'person',
      tone: 'run' as Tone,
      title: `User-Task · ${nodeLabel(model, token.currentFlowNodeId)}`,
      subtitle: 'wartet auf Bearbeitung',
    })),
    ...data.timers.map((timer) => ({
      key: `timer-${timer.id}`,
      icon: 'schedule',
      tone: 'wait' as Tone,
      title: `Timer · ${nodeLabel(model, timer.flowNodeId)}`,
      subtitle: `läuft ${formatDueIn(timer.dueAt)} ab${
        timer.remainingOccurrences != null ? ` · noch ${timer.remainingOccurrences} Wiederholungen` : ''
      }`,
    })),
    ...data.messages.map((message, index) => ({
      key: `message-${index}`,
      icon: 'mail',
      tone: 'accent' as Tone,
      title: `Nachricht · ${message.message.name}`,
      subtitle: `erwartet in ${message.processId}`,
    })),
    ...data.signals.map((signal, index) => ({
      key: `signal-${index}`,
      icon: 'bolt',
      tone: 'accent' as Tone,
      title: `Signal · ${signal.signal}`,
      subtitle: `erwartet in ${signal.processId}`,
    })),
    ...data.services.map((token) => ({
      key: `service-${token.id}`,
      icon: nodeTypeIcon(model.nodeById.get(token.currentFlowNodeId ?? '')?.type),
      tone: 'run' as Tone,
      title: `Service-Task · ${nodeLabel(model, token.currentFlowNodeId)}`,
      subtitle: 'wartet auf einen Worker',
    })),
  ];

  if (rows.length === 0) {
    return (
      <EmptyState
        icon="notifications_off"
        title="Keine Warteobjekte"
        description="Die Instanz wartet aktuell auf nichts Externes."
      />
    );
  }

  return (
    <div className="flex flex-col gap-2.5">
      {rows.map((row) => (
        <div key={row.key} className="border-border flex items-center gap-3 rounded-[var(--r)] border p-3">
          <span
            className="grid h-9 w-9 flex-none place-items-center rounded-[9px]"
            style={{ background: toneSurface(row.tone, 13), color: toneColor(row.tone) }}
          >
            <Icon name={row.icon} size={19} />
          </span>
          <div className="min-w-0 flex-1">
            <div className="truncate text-[13px] font-semibold">{row.title}</div>
            <div className="text-muted mt-0.5 truncate text-xs">{row.subtitle}</div>
          </div>
        </div>
      ))}
    </div>
  );
}
