import type { RuntimeDiagram } from '@flowzer/sdk';
import { useMemo } from 'react';

import { Dot, type Tone } from '@/components/ui/Chip';
import { EmptyState } from '@/components/ui/Card';
import { nodeLabel, nodeTypeLabel, type BpmnModelSummary } from '@/lib/bpmnModel';
import { formatTimestamp, parseApiDate } from '@/lib/format';
import { runtimeState } from '@/lib/runtimeView';

const STATE_LABEL: Record<ReturnType<typeof runtimeState>, string> = {
  Ready: 'Bereit', Active: 'Aktiv', Completing: 'Wird abgeschlossen',
  WaitingForLoopEnd: 'Wartet auf parallele Ausführungen', Completed: 'Abgeschlossen',
  Failing: 'Störung wird behandelt', Terminating: 'Wird abgebrochen', Failed: 'Gestört',
  Terminated: 'Abgebrochen', Withdrawn: 'Zurückgezogen', Compensating: 'Wird kompensiert',
  Compensated: 'Kompensiert', Merged: 'Zusammengeführt', Unknown: 'Unbekannter Zustand',
};

function stateTone(state: ReturnType<typeof runtimeState>): Tone {
  if (state === 'Failed' || state === 'Failing') return 'fail';
  if (state === 'Terminated' || state === 'Terminating' || state === 'Withdrawn') return 'wait';
  if (state === 'Completed' || state === 'Compensated' || state === 'Merged') return 'done';
  return state === 'Unknown' ? 'muted' : 'run';
}

export function RuntimeTimeline({ runtime, model }: {
  runtime: RuntimeDiagram;
  model: BpmnModelSummary;
}) {
  const events = useMemo(() => (runtime.events ?? []).map((event) => ({
    event,
    state: runtimeState(event.state),
    at: parseApiDate(event.occurredAtUtc),
  })), [runtime.events]);

  if (events.length === 0) {
    return <EmptyState icon="timeline" title="Noch keine Laufzeitereignisse" description="Für diese Instanz ist noch kein persistierter Knotenzustand vorhanden." />;
  }

  return (
    <ol aria-label="Engine-Ereignisse" className="m-0 list-none p-0">
      {events.map(({ event, state, at }, index) => {
        const flowNodeId = event.flowNodeId ?? '';
        const node = model.nodeById.get(flowNodeId);
        return (
          <li key={event.id} className="flex gap-3.5">
            <div className="flex flex-none flex-col items-center">
              <Dot tone={stateTone(state)} size={12} halo className="mt-1" />
              {index < events.length - 1 && <span className="bg-border my-1 w-0.5 flex-1" />}
            </div>
            <div className="pb-[18px]">
              <div className="text-[13.5px] font-semibold">{nodeLabel(model, flowNodeId)}</div>
              <div className="text-muted mt-0.5 text-[12.5px]">
                {nodeTypeLabel(node?.type)} · {STATE_LABEL[state]}
              </div>
              <div className="text-faint mt-1 font-mono text-[11.5px]">{formatTimestamp(at)}</div>
            </div>
          </li>
        );
      })}
    </ol>
  );
}
