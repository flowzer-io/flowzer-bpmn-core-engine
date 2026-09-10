import { toast } from 'sonner';

import { Chip, type Tone } from '@/components/ui/Chip';
import { EmptyState, SectionLabel } from '@/components/ui/Card';
import { Icon } from '@/components/ui/Icon';
import type { FlowNodeState, ProcessInstanceInfoDto, ProcessVariables, TokenDto } from '@/lib/api/types';
import { nodeLabel, nodeTypeLabel, type BpmnModelSummary } from '@/lib/bpmnModel';
import { cn } from '@/lib/cn';
import { formatTimestamp, formatVariableValue, shortId } from '@/lib/format';
import { nodeExecutions } from '@/lib/instanceView';

const TOKEN_STATE_LABEL: Record<FlowNodeState, string> = {
  Ready: 'Bereit',
  Active: 'Aktiv',
  Completing: 'Wird abgeschlossen',
  WaitingForLoopEnd: 'Wartet auf parallele Ausführungen',
  Completed: 'Abgeschlossen',
  Failing: 'Störung wird behandelt',
  Terminating: 'Wird abgebrochen',
  Failed: 'Gestört',
  Terminated: 'Abgebrochen',
  Withdrawn: 'Zurückgezogen',
  Compensating: 'Wird kompensiert',
  Compensated: 'Kompensiert',
  Merged: 'Zusammengeführt',
};

function tokenTone(state: FlowNodeState): Tone {
  if (state === 'Failed' || state === 'Failing') return 'fail';
  if (state === 'Terminated' || state === 'Terminating' || state === 'Withdrawn') return 'wait';
  if (state === 'Completed' || state === 'Compensated' || state === 'Merged') return 'done';
  return 'run';
}

/** Zeigt den aktuellen, am Master-Token persistierten Prozessscope. */
export function ProcessVariablesPanel({ variables }: { variables: ProcessVariables }) {
  const variableEntries = Object.entries(variables);

  return (
    <>
      <div className="mb-3 flex items-center justify-between">
        <SectionLabel>Prozessvariablen</SectionLabel>
        {variableEntries.length > 0 && (
          <CopyJsonButton value={variables} successMessage="Variablen kopiert" />
        )}
      </div>

      {variableEntries.length === 0 ? (
        <EmptyState
          icon="data_object"
          title="Keine Variablen"
          description="Dieser Prozess führt derzeit keine Daten mit."
        />
      ) : (
        <VariableTable variables={variables} />
      )}
    </>
  );
}

/** Zeigt die persistierten Ein- und Ausgaben aller Ausführungen eines Knotens. */
export function RuntimeNodeDataPanel({
  instance,
  flowNodeId,
  model,
}: {
  instance: ProcessInstanceInfoDto;
  flowNodeId: string | undefined;
  model: BpmnModelSummary;
}) {
  if (!flowNodeId) {
    return (
      <EmptyState
        icon="ads_click"
        title="Kein Schritt ausgewählt"
        description="Wählen Sie im Diagramm oder in der Knotenliste einen Prozessschritt aus."
      />
    );
  }

  const executions = nodeExecutions(instance, flowNodeId);
  const node = model.nodeById.get(flowNodeId);

  return (
    <>
      <SectionLabel className="mb-2">Ausgewählter Prozessschritt</SectionLabel>
      <div className="mb-4">
        <div className="text-[14px] font-semibold">{nodeLabel(model, flowNodeId)}</div>
        <div className="text-muted mt-0.5 text-xs">{nodeTypeLabel(node?.type)} · {flowNodeId}</div>
      </div>

      {executions.length === 0 ? (
        <EmptyState
          icon="data_object"
          title="Keine Ausführungsdaten"
          description="Für diesen Schritt wurde noch kein Token persistiert."
        />
      ) : (
        <div className="flex flex-col gap-3">
          {executions.map((token, index) => (
            <ExecutionDataCard
              key={token.id}
              token={token}
              ordinal={index + 1}
              total={executions.length}
            />
          ))}
        </div>
      )}
    </>
  );
}

function ExecutionDataCard({ token, ordinal, total }: {
  token: TokenDto;
  ordinal: number;
  total: number;
}) {
  return (
    <article
      aria-label={`Ausführung ${ordinal} von ${total}`}
      className="border-border overflow-hidden rounded-[var(--r)] border"
    >
      <header className="border-border bg-surface-2 flex flex-wrap items-center gap-2 border-b px-3 py-2.5">
        <span className="font-mono text-xs font-semibold">#{shortId(token.id)}</span>
        <Chip tone={tokenTone(token.state)}>{TOKEN_STATE_LABEL[token.state]}</Chip>
        <span className="text-faint ml-auto text-[11.5px]">Start {formatTimestamp(token.startTime)}</span>
      </header>

      <div className="space-y-4 p-3">
        <VariableSnapshot
          title="Gebundener Input"
          value={token.variables}
          missingDescription="Kein lokaler Input-Snapshot. Der Schritt verwendet den Prozessscope oder besitzt keine Eingabezuordnung."
          emptyDescription="Für diese Ausführung wurde ein leerer Input-Snapshot persistiert."
        />
        <VariableSnapshot
          title="Erzeugter Output"
          value={token.outputData}
          missingDescription="Für diese Ausführung ist noch kein Output-Snapshot persistiert."
          emptyDescription="Für diese Ausführung wurde ein leerer Output-Snapshot persistiert."
        />
      </div>
    </article>
  );
}

function VariableSnapshot({
  title,
  value,
  missingDescription,
  emptyDescription,
}: {
  title: string;
  value: ProcessVariables | null | undefined;
  missingDescription: string;
  emptyDescription: string;
}) {
  const entries = value == null ? [] : Object.entries(value);

  return (
    <section>
      <div className="mb-2 flex items-center justify-between gap-2">
        <SectionLabel>{title}</SectionLabel>
        {value != null && entries.length > 0 && (
          <CopyJsonButton value={value} successMessage={`${title} kopiert`} />
        )}
      </div>
      {value == null ? (
        <p className="text-muted m-0 text-xs leading-5">{missingDescription}</p>
      ) : entries.length === 0 ? (
        <p className="text-muted m-0 text-xs leading-5">{emptyDescription}</p>
      ) : (
        <VariableTable variables={value} compact />
      )}
    </section>
  );
}

function VariableTable({ variables, compact = false }: {
  variables: ProcessVariables;
  compact?: boolean;
}) {
  return (
    <dl className="border-border m-0 overflow-hidden rounded-[var(--r)] border">
      {Object.entries(variables).map(([key, value], index) => (
        <div
          key={key}
          className={cn(
            'flex items-start gap-2.5 font-mono text-[12.5px]',
            compact ? 'px-2.5 py-2' : 'px-3 py-2.5',
            index > 0 && 'border-border border-t',
          )}
        >
          <dt className="text-muted min-w-0 flex-1 break-all">{key}</dt>
          <dd className="text-accent m-0 max-w-[58%] break-all text-right font-semibold">
            {formatVariableValue(value)}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function CopyJsonButton({ value, successMessage }: {
  value: ProcessVariables;
  successMessage: string;
}) {
  return (
    <button
      type="button"
      title="Als JSON kopieren"
      onClick={() => {
        void navigator.clipboard.writeText(JSON.stringify(value, null, 2));
        toast.success(successMessage);
      }}
      className="text-faint hover:text-text cursor-pointer border-none bg-transparent p-0"
    >
      <Icon name="content_copy" size={18} />
    </button>
  );
}
