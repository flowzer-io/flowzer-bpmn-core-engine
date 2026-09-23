import { useNavigate } from '@tanstack/react-router';

import { RetryJobAction } from '@/components/operations/RetryJobAction';
import { Card, CardHeader, EmptyState } from '@/components/ui/Card';
import { Chip, toneColor, type Tone } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { Button } from '@/components/ui/Button';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import type { OperationsIncidentDto, OperationsIncidentKind } from '@/lib/api/types';
import { formatRelative, shortId } from '@/lib/format';

const KIND_LABEL: Record<OperationsIncidentKind, string> = {
  jobExhausted: 'Auftrag liegt',
  instanceFailed: 'Instanz gescheitert',
};

// Ein liegen gebliebener Auftrag wartet auf einen Eingriff und ist damit noch zu retten; eine
// gescheiterte Instanz ist bereits zu Ende. Die Farben sagen genau diesen Unterschied.
const KIND_TONE: Record<OperationsIncidentKind, Tone> = {
  jobExhausted: 'wait',
  instanceFailed: 'fail',
};

const KIND_ICON: Record<OperationsIncidentKind, string> = {
  jobExhausted: 'back_hand',
  instanceFailed: 'error',
};

interface IncidentsCardProps {
  incidents: OperationsIncidentDto[] | undefined;
  counters?: { jobExhausted: number; instanceFailed: number };
  pending: boolean;
  error: unknown;
  onRetry: () => void;
}

/**
 * Alles, was ohne Eingriff liegen bleibt — an einer Stelle.
 *
 * Bis hierher musste der Betrieb zwei Dinge getrennt suchen: gescheiterte Instanzen in der
 * Instanzliste und liegen gebliebene Aufträge gar nicht, weil sie nirgends auftauchten. Beides
 * ist dieselbe Frage („Wo hängt etwas?") und steht deshalb in einer Liste.
 */
export function IncidentsCard({ incidents, counters, pending, error, onRetry }: IncidentsCardProps) {
  const navigate = useNavigate();
  const rows = incidents ?? [];

  return (
    <Card>
      <CardHeader
        icon="warning"
        iconClassName="text-fail"
        title="Störungen"
        actions={
          counters && (
            <span className="flex items-center gap-1.5">
              <Chip tone={KIND_TONE.jobExhausted}>{counters.jobExhausted} liegen</Chip>
              <Chip tone={KIND_TONE.instanceFailed}>{counters.instanceFailed} gescheitert</Chip>
            </span>
          )
        }
      />

      {pending && <LoadingRows rows={3} />}
      {error != null && <ErrorState error={error} onRetry={onRetry} />}

      {!pending && error == null && rows.length === 0 && (
        <EmptyState
          className="border-border border-t"
          icon="check_circle"
          title="Keine Störungen"
          description="Aktuell bleibt kein Auftrag und keine Instanz ohne Eingriff liegen."
        />
      )}

      {rows.map((incident) => (
        <div
          key={incident.jobId ?? `${incident.kind}-${incident.instanceId}`}
          className="border-border flex flex-wrap items-center gap-3 gap-y-2.5 border-t px-[18px] py-3.5"
        >
          <span
            className="grid h-8 w-8 flex-none place-items-center rounded-lg"
            style={{
              background: `color-mix(in oklab, ${toneColor(KIND_TONE[incident.kind])} 12%, transparent)`,
              color: toneColor(KIND_TONE[incident.kind]),
            }}
          >
            <Icon name={KIND_ICON[incident.kind]} size={18} />
          </span>

          <div className="min-w-[180px] flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="truncate text-[13.5px] font-semibold">{incident.definitionName}</span>
              <Chip tone={KIND_TONE[incident.kind]}>{KIND_LABEL[incident.kind]}</Chip>
            </div>
            <div className="text-muted mt-0.5 text-[12.5px]">
              {incident.flowNodeName ?? incident.flowNodeId ?? 'Schritt unbekannt'}
              {incident.jobType && <span className="text-faint"> · Typ {incident.jobType}</span>}
            </div>
            {incident.message && (
              <div className="text-faint mt-1 break-words text-[12.5px]">{incident.message}</div>
            )}
            <div className="text-faint mt-1 font-mono text-[11.5px]">
              #{shortId(incident.instanceId)} · seit {formatRelative(incident.since)}
            </div>
          </div>

          <div className="flex flex-none items-center gap-2">
            {incident.kind === 'jobExhausted' && <RetryJobAction incident={incident} />}
            <Button
              size="sm"
              variant="ghost"
              onClick={() => void navigate({ to: `/instances/${incident.instanceId}` })}
            >
              Zur Instanz
            </Button>
          </div>
        </div>
      ))}
    </Card>
  );
}
