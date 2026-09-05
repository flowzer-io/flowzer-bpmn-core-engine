import { useMemo } from 'react';

import type { Tone } from '@/components/ui/Chip';
import { instanceBucket } from '@/lib/api/normalize';
import { useDiagnostics, useInstances, useTimers } from '@/lib/api/queries';
import type { ProcessInstanceInfoDto, TimerSubscriptionDto } from '@/lib/api/types';
import { formatDueIn, formatRelative, parseApiDate, shortId } from '@/lib/format';

export interface ActivityEntry {
  id: string;
  text: string;
  time: string;
  tone: Tone;
  /** Sortierschlüssel: je größer, desto aktueller. */
  at: number;
  href?: string;
}

const MAX_ENTRIES = 12;

function instanceEntries(instances: ProcessInstanceInfoDto[]): ActivityEntry[] {
  return instances.flatMap((instance): ActivityEntry[] => {
    const startedAt = parseApiDate(instance.startedAt);
    const finishedAt = parseApiDate(instance.finishedAt);
    const bucket = instanceBucket(instance.state);
    const href = `/instances/${instance.instanceId}`;
    const label = instance.relatedDefinitionName;

    if (bucket === 'error') {
      const at = finishedAt ?? startedAt;
      return [
        {
          id: `${instance.instanceId}-error`,
          text: `Instanz #${shortId(instance.instanceId)} („${label}") ist fehlgeschlagen`,
          time: formatRelative(at),
          tone: 'fail' as const,
          at: at?.getTime() ?? 0,
          href,
        },
      ];
    }

    if (bucket === 'done' && finishedAt) {
      return [
        {
          id: `${instance.instanceId}-done`,
          text: `„${label}" wurde abgeschlossen`,
          time: formatRelative(finishedAt),
          tone: 'done' as const,
          at: finishedAt.getTime(),
          href,
        },
      ];
    }

    if (!startedAt) return [];

    return [
      {
        id: `${instance.instanceId}-start`,
        text: `Neue Instanz von „${label}" gestartet`,
        time: formatRelative(startedAt),
        tone: 'accent' as const,
        at: startedAt.getTime(),
        href,
      },
    ];
  });
}

function timerEntries(timers: TimerSubscriptionDto[]): ActivityEntry[] {
  // Nur Timer, die in den nächsten 24 Stunden fällig werden, sind eine Meldung wert.
  const horizon = Date.now() + 24 * 60 * 60_000;

  return timers
    .filter((timer) => {
      const dueAt = parseApiDate(timer.dueAt);
      return dueAt !== null && dueAt.getTime() <= horizon;
    })
    .map((timer) => {
      const dueAt = parseApiDate(timer.dueAt);
      const scope = timer.processInstanceId ? `#${shortId(timer.processInstanceId)}` : timer.relatedDefinitionId;
      return {
        id: `timer-${timer.id}`,
        text: `Timer „${timer.flowNodeId}" in ${scope} läuft ${formatDueIn(dueAt)} ab`,
        time: formatDueIn(dueAt),
        tone: 'wait' as const,
        at: dueAt?.getTime() ?? 0,
        href: timer.processInstanceId ? `/instances/${timer.processInstanceId}` : '/operations',
      };
    });
}

/**
 * Baut den Aktivitätsstrom aus echten Laufzeitdaten: gestartete, beendete und
 * fehlgeschlagene Instanzen, bald fällige Timer sowie der letzte Scheduler-Fehler.
 */
export function useActivityFeed(): ActivityEntry[] {
  const instancesQuery = useInstances();
  const timersQuery = useTimers();
  const diagnosticsQuery = useDiagnostics();

  const instances = instancesQuery.data;
  const timers = timersQuery.data;
  const schedulerError = diagnosticsQuery.data?.timerScheduler.lastErrorMessage;
  const schedulerFailedAt = diagnosticsQuery.data?.timerScheduler.lastFailedTickAtUtc;

  return useMemo(() => {
    const entries: ActivityEntry[] = [
      ...instanceEntries(instances ?? []),
      ...timerEntries(timers ?? []),
    ];

    if (schedulerError) {
      const at = parseApiDate(schedulerFailedAt);
      entries.push({
        id: 'scheduler-error',
        text: `Timer-Scheduler meldet: ${schedulerError}`,
        time: formatRelative(at),
        tone: 'fail',
        at: at?.getTime() ?? Date.now(),
        href: '/operations',
      });
    }

    return entries.sort((a, b) => b.at - a.at).slice(0, MAX_ENTRIES);
  }, [instances, timers, schedulerError, schedulerFailedAt]);
}
