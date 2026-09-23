import { useId } from 'react';

import { FieldLabel } from '@/components/ui/Field';
import {
  ANY_FILTER,
  versionFilterOptions,
  workflowFilterOptions,
  type FilterableInstance,
  type InstanceFilter,
} from '@/lib/instanceFilter';

const SELECT_CLASS =
  'bg-surface border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] ' +
  'focus:border-accent outline-none disabled:cursor-not-allowed disabled:opacity-55';

interface InstanceFilterBarProps {
  instances: readonly FilterableInstance[];
  filter: InstanceFilter;
  onChange: (filter: InstanceFilter) => void;
  /** Wie viele Zeilen die Liste darunter gerade zeigt. */
  resultCount: number;
}

/**
 * Die Auswahl nach Workflow und — davon abhängig — nach Version.
 *
 * Die Versionswahl bleibt ohne Workflow sichtbar und gesperrt statt ausgeblendet: So bleibt
 * erkennbar, dass es sie gibt und woran sie hängt, und die Reihe springt beim Wählen nicht um.
 */
export function InstanceFilterBar({ instances, filter, onChange, resultCount }: InstanceFilterBarProps) {
  const workflowFieldId = useId();
  const versionFieldId = useId();

  const workflows = workflowFilterOptions(instances);
  const versions = versionFilterOptions(instances, filter.workflowId);
  const withoutWorkflow = filter.workflowId === ANY_FILTER;

  return (
    <div className="mb-4 flex flex-wrap items-end gap-3">
      <div className="min-w-[150px] flex-1 sm:max-w-[240px]">
        <FieldLabel htmlFor={workflowFieldId}>Workflow</FieldLabel>
        <select
          id={workflowFieldId}
          className={SELECT_CLASS}
          value={filter.workflowId}
          // Die Version gehört zum Workflow; sie wird beim Wechsel mitgesetzt, damit keine
          // Einschränkung des vorigen Workflows stehen bleibt.
          onChange={(event) => onChange({ workflowId: event.target.value, versionKey: ANY_FILTER })}
        >
          <option value={ANY_FILTER}>Alle Workflows</option>
          {workflows.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label} ({option.count})
            </option>
          ))}
        </select>
      </div>

      <div className="min-w-[150px] flex-1 sm:max-w-[240px]">
        <FieldLabel htmlFor={versionFieldId}>Version</FieldLabel>
        <select
          id={versionFieldId}
          className={SELECT_CLASS}
          value={filter.versionKey}
          disabled={withoutWorkflow}
          title={withoutWorkflow ? 'Erst einen Workflow wählen' : undefined}
          onChange={(event) => onChange({ ...filter, versionKey: event.target.value })}
        >
          <option value={ANY_FILTER}>Alle Versionen</option>
          {versions.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label} ({option.count})
            </option>
          ))}
        </select>
      </div>

      <p role="status" className="text-muted basis-full text-[12.5px] sm:ml-auto sm:basis-auto sm:pb-2.5">
        {resultCount} von {instances.length} Instanzen
      </p>
    </div>
  );
}
