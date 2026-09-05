import { useQueries } from '@tanstack/react-query';
import { useMemo } from 'react';

import type { Tone } from '@/components/ui/Chip';
import { definitionsApi } from '@/lib/api/endpoints';
import { instanceBucket, isFailedToken, isFinishedToken, isLiveToken, type InstanceBucket } from '@/lib/api/normalize';
import { queryKeys } from '@/lib/api/queries';
import type { ProcessInstanceInfoDto, ProcessInstanceState, TokenDto } from '@/lib/api/types';
import { parseBpmn, type BpmnModelSummary } from '@/lib/bpmnModel';

export const BUCKET_TONE: Record<InstanceBucket, Tone> = {
  active: 'run',
  done: 'done',
  error: 'fail',
};

export const BUCKET_LABEL: Record<InstanceBucket, string> = {
  active: 'Aktiv',
  done: 'Fertig',
  error: 'Fehler',
};

/** Der genaue Engine-Zustand als deutscher Anzeigetext. */
export const STATE_LABEL: Record<ProcessInstanceState, string> = {
  Initialized: 'Initialisiert',
  Running: 'Läuft',
  Waiting: 'Wartet',
  Completing: 'Schließt ab',
  Completed: 'Abgeschlossen',
  Failing: 'Fehler tritt auf',
  Failed: 'Fehlgeschlagen',
  Terminating: 'Wird beendet',
  Terminated: 'Beendet',
  Compensating: 'Kompensiert',
  Compensated: 'Kompensiert',
};

/**
 * Lädt das BPMN-XML für einen Satz von Definitionsversionen.
 *
 * Die Instanzliste referenziert typischerweise nur eine Handvoll verschiedener
 * Versionen. Da BPMN-XML einer Version unveränderlich ist, wird es dauerhaft
 * zwischengespeichert und pro Version genau einmal geholt.
 */
export function useDefinitionModels(versionGuids: string[]): Map<string, BpmnModelSummary> {
  const unique = useMemo(() => [...new Set(versionGuids.filter(Boolean))].sort(), [versionGuids]);

  const results = useQueries({
    queries: unique.map((guid) => ({
      queryKey: queryKeys.definitionXml(guid),
      queryFn: ({ signal }: { signal: AbortSignal }) => definitionsApi.getXml(guid, signal),
      staleTime: Infinity,
      gcTime: 30 * 60_000,
      retry: false,
    })),
  });

  // `results` ist bei jedem Render ein neues Array; als Abhängigkeit dient deshalb
  // die Liste der geladenen XML-Inhalte.
  const xmls = results.map((result) => result.data);
  const xmlKey = xmls.map((xml) => (xml ? xml.length : 0)).join(',');

  return useMemo(() => {
    const map = new Map<string, BpmnModelSummary>();
    unique.forEach((guid, index) => {
      const xml = xmls[index];
      if (xml) map.set(guid, parseBpmn(xml));
    });
    return map;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [unique, xmlKey]);
}

export interface InstanceProgress {
  /** Anteil erledigter Schritte, 0…1. `null`, wenn das Modell (noch) unbekannt ist. */
  ratio: number | null;
  visited: number;
  total: number;
}

/**
 * Fortschritt einer Instanz: Anteil der Prozessschritte, die bereits ein Token
 * gesehen hat. Ohne geladenes Modell wird kein Anteil geraten — abgeschlossene
 * Instanzen gelten als vollständig.
 */
export function instanceProgress(
  instance: ProcessInstanceInfoDto,
  model: BpmnModelSummary | undefined,
): InstanceProgress {
  const bucket = instanceBucket(instance.state);

  const visitedIds = new Set(
    instance.tokens.map((token) => token.currentFlowNodeId).filter((id): id is string => Boolean(id)),
  );

  if (bucket === 'done') {
    const total = model?.nodes.length ?? visitedIds.size;
    return { ratio: 1, visited: total, total };
  }

  if (!model || model.nodes.length === 0) {
    return { ratio: null, visited: visitedIds.size, total: 0 };
  }

  const total = model.nodes.length;
  const visited = [...visitedIds].filter((id) => model.nodeById.has(id)).length;

  // Mindestens ein sichtbarer Anteil, sobald die Instanz überhaupt gestartet ist.
  const ratio = Math.min(1, Math.max(visited / total, visited > 0 ? 0.05 : 0));
  return { ratio, visited, total };
}

/** Das Token, das den aktuellen Schritt der Instanz repräsentiert. */
export function currentToken(instance: ProcessInstanceInfoDto): TokenDto | undefined {
  const failed = instance.tokens.find(isFailedToken);
  if (failed) return failed;

  const live = instance.tokens.filter(isLiveToken);
  if (live.length > 0) {
    // Bei mehreren aktiven Zweigen ist der zuletzt bewegte am aussagekräftigsten.
    return live.reduce((latest, token) =>
      (token.lastStateChangeTime ?? '') > (latest.lastStateChangeTime ?? '') ? token : latest,
    );
  }

  return instance.tokens.at(-1);
}

/** Markierungen für den BPMN-Viewer aus den Tokens einer Instanz. */
export function tokenMarkers(instance: ProcessInstanceInfoDto): {
  markers: Record<string, 'completed' | 'active' | 'failed'>;
  activeNodeIds: string[];
} {
  const markers: Record<string, 'completed' | 'active' | 'failed'> = {};
  const activeNodeIds: string[] = [];

  for (const token of instance.tokens) {
    const nodeId = token.currentFlowNodeId;
    if (!nodeId) continue;

    if (isFailedToken(token)) {
      markers[nodeId] = 'failed';
    } else if (isLiveToken(token)) {
      markers[nodeId] = 'active';
      activeNodeIds.push(nodeId);
    } else if (isFinishedToken(token) && markers[nodeId] !== 'active') {
      markers[nodeId] = 'completed';
    }
  }

  return { markers, activeNodeIds };
}

/** Zählt Wartepunkte einer Instanz für die Spalte „Warteobjekte“. */
export function waitingBadges(instance: ProcessInstanceInfoDto): { icon: string; count: number; title: string }[] {
  return [
    { icon: 'person', count: instance.userTaskSubscriptionCount, title: 'offene User-Tasks' },
    { icon: 'mail', count: instance.messageSubscriptionCount, title: 'erwartete Nachrichten' },
    { icon: 'bolt', count: instance.signalSubscriptionCount, title: 'erwartete Signale' },
    { icon: 'smart_toy', count: instance.serviceSubscriptionCount, title: 'offene Service-Tasks' },
  ].filter((badge) => badge.count > 0);
}
