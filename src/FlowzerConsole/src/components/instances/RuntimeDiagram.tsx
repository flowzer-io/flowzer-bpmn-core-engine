import type { RuntimeDiagram as RuntimeDiagramDto } from '@flowzer/sdk';
import { useMemo } from 'react';

import { BpmnViewer } from '@/components/bpmn/BpmnViewer';
import { Icon } from '@/components/ui/Icon';
import { nodeLabel, parseBpmn } from '@/lib/bpmnModel';
import { cn } from '@/lib/cn';
import {
  RUNTIME_STATUS_ICON,
  RUNTIME_STATUS_LABEL,
  runtimeMarkers,
  runtimeNodeStatus,
} from '@/lib/runtimeView';

/**
 * Laufzeitdiagramm samt zugaenglicher Knotenliste. Die Auswahl haelt die Seite, weil auch
 * die Schrittdaten daneben an ihr haengen; hier wird sie nur gezeigt — in der Liste und
 * im Schaubild gleichermassen.
 */
export function RuntimeDiagram({
  runtime,
  selectedNodeId,
  onNodeSelect,
}: {
  runtime: RuntimeDiagramDto;
  selectedNodeId?: string;
  onNodeSelect?: (flowNodeId: string) => void;
}) {
  const model = useMemo(() => parseBpmn(runtime.diagramXml ?? undefined), [runtime.diagramXml]);
  const { markers, activeTokenCounts } = useMemo(() => runtimeMarkers(runtime), [runtime]);
  const nodes = (runtime.nodes ?? []).filter((node) => Boolean(node.flowNodeId));

  return (
    <section aria-labelledby="runtime-diagram-heading" className="flex min-h-0 flex-col lg:h-full">
      <h2 id="runtime-diagram-heading" className="sr-only">Laufzeitdiagramm</h2>
      <div className="h-[42vh] flex-none lg:h-auto lg:min-h-0 lg:flex-1">
        <BpmnViewer
          xml={runtime.diagramXml ?? undefined}
          markers={markers}
          tokenCounts={activeTokenCounts}
          selectedElementId={selectedNodeId}
          onElementClick={onNodeSelect}
          className="h-full w-full"
          ariaLabel="BPMN-Laufzeitdiagramm"
        />
      </div>
      <div className="border-border bg-surface/95 flex-none border-t px-4 py-3">
        <ul aria-label="Legende der Laufzeitzustände" className="m-0 flex list-none flex-wrap gap-3 p-0 text-xs">
          {(['active', 'completed', 'cancelled', 'failed'] as const).map((status) => (
            <li key={status} className="inline-flex items-center gap-1.5">
              <Icon name={RUNTIME_STATUS_ICON[status]} size={15} />
              {RUNTIME_STATUS_LABEL[status]}
            </li>
          ))}
        </ul>
        <ol aria-label="Prozessknoten und Laufzeitzustände" className="mt-3 flex list-none flex-wrap gap-2 p-0">
          {nodes.map((node) => {
            const flowNodeId = node.flowNodeId!;
            const status = runtimeNodeStatus(node.status);
            return (
              <li key={flowNodeId}>
                <button
                  type="button"
                  aria-current={selectedNodeId === flowNodeId ? 'step' : undefined}
                  onClick={() => onNodeSelect?.(flowNodeId)}
                  className={cn(
                    'border-border bg-surface-2 inline-flex min-h-11 items-center gap-2 rounded-[var(--r-sm)] border px-3 py-2 text-left text-xs',
                    selectedNodeId === flowNodeId && 'border-accent text-accent',
                  )}
                >
                  <Icon name={RUNTIME_STATUS_ICON[status]} size={16} />
                  <span>{nodeLabel(model, flowNodeId)}</span>
                  <span className="text-muted">{RUNTIME_STATUS_LABEL[status]}</span>
                  {node.tokenCount > 1 && <span className="font-mono">×{node.tokenCount}</span>}
                </button>
              </li>
            );
          })}
        </ol>
      </div>
    </section>
  );
}
