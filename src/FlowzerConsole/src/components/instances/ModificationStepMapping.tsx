import { useId } from 'react';

import { EmptyState } from '@/components/ui/Card';
import type { InstanceModificationStepDto, ModificationFlowNodeDto } from '@/lib/api/types';
import { flowNodeLabel, modificationTargetGroups, stepLabel } from '@/lib/instanceModification';

interface ModificationStepMappingProps {
  /** Die wartenden Schritte aus dem Trockenlauf. */
  steps: InstanceModificationStepDto[];
  /** Die erlaubten Zielknoten des Modells. */
  targets: ModificationFlowNodeDto[];
  /** Token-Kennung auf gewähltes Ziel; ein leerer Wert heißt „belassen“. */
  selection: Record<string, string>;
  onChoose: (tokenId: string, targetFlowNodeId: string) => void;
  disabled?: boolean;
}

const SELECT_STYLE =
  'bg-surface-2 border-border text-text w-[220px] max-w-full cursor-pointer rounded-[var(--r-sm)] ' +
  'border px-2.5 py-1.5 text-[13px] outline-none focus:border-[var(--accent)] disabled:cursor-default';

/**
 * Die wartenden Schritte der Instanz und das Ziel, auf das sie gehen sollen.
 *
 * Voreingestellt ist „belassen“: Der Dialog verschiebt nichts, was niemand ausgewählt hat.
 * Die Ziele stehen nach Elementart gruppiert, weil ein Modell schnell dreißig Knoten hat
 * und die bloße Liste dann nicht mehr zu überblicken ist. Angeboten werden nur Knoten, die
 * sich betreten lassen — Start- und angeheftete Ereignisse nennt die API gar nicht erst.
 */
export function ModificationStepMapping({
  steps,
  targets,
  selection,
  onChoose,
  disabled,
}: ModificationStepMappingProps) {
  const groupId = useId();
  const groups = modificationTargetGroups(targets);

  return (
    <section className="border-border mb-4 rounded-[var(--r)] border p-3">
      <h3 className="m-0 text-[13.5px] font-semibold">Schritte</h3>
      <p className="text-muted mt-1 mb-3 text-[13px]">
        Ein verschobener Schritt beginnt am Ziel neu. Die Aufgabe oder der Auftrag, der hier
        wartet, verfällt dabei samt Kennung; Variablen bleiben davon unberührt.
      </p>

      {steps.length === 0 ? (
        <EmptyState
          icon="notifications_off"
          title="Kein wartender Schritt"
          description="Die Instanz wartet gerade an keinem Schritt der obersten Ebene. Verschieben lässt sich deshalb nichts; Variablen lassen sich trotzdem korrigieren."
        />
      ) : (
        <ul className="m-0 flex list-none flex-col gap-2 p-0">
          {steps.map((step) => {
            const selectId = `${groupId}-${step.tokenId}`;
            return (
              <li key={step.tokenId} className="flex flex-wrap items-center gap-2">
                <label htmlFor={selectId} className="min-w-0 flex-1 truncate text-[13px]">
                  {stepLabel(step)}
                  <span className="sr-only"> — Ziel des Eingriffs</span>
                </label>
                <span aria-hidden="true" className="text-faint">
                  →
                </span>
                <select
                  id={selectId}
                  className={SELECT_STYLE}
                  value={selection[step.tokenId] ?? ''}
                  disabled={disabled}
                  onChange={(event) => onChoose(step.tokenId, event.target.value)}
                >
                  <option value="">belassen</option>
                  {groups.map((group) => (
                    <optgroup key={group.label} label={group.label}>
                      {group.targets.map((target) => (
                        <option key={target.id} value={target.id}>
                          {flowNodeLabel(target)}
                        </option>
                      ))}
                    </optgroup>
                  ))}
                </select>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
