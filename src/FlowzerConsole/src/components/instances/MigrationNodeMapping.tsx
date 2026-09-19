import { useId } from 'react';

import type { InstanceMigrationMappingDto, MigrationFlowNodeDto } from '@/lib/api/types';
import type { MigrationMappingChoice } from '@/lib/instanceMigration';
import { flowNodeLabel, migrationMappingRows, migrationTargetOptions } from '@/lib/instanceMigration';

interface MigrationNodeMappingProps {
  /** Forderungen und Ziele aus dem Trockenlauf. */
  mapping: InstanceMigrationMappingDto;
  /** Die bereits getroffenen Wahlen — auch die wieder geleerten. */
  choices: MigrationMappingChoice[];
  onChoose: (source: MigrationFlowNodeDto, targetId: string) => void;
  disabled?: boolean;
}

const SELECT_STYLE =
  'bg-surface-2 border-border text-text w-[220px] max-w-full cursor-pointer rounded-[var(--r-sm)] ' +
  'border px-2.5 py-1.5 text-[13px] outline-none focus:border-[var(--accent)] disabled:cursor-default';

/**
 * Zuordnung wartender Knoten, die es in der Zielversion nicht mehr gibt.
 *
 * Ohne diesen Block bliebe jede Instanz zurück, deren Schritt umbenannt oder ersetzt wurde —
 * der Assistent fragt lieber, als sie stillschweigend liegen zu lassen. Die Auswahl bietet
 * nur Knoten derselben Elementart an; das Zielmodell kennt eine Aufgabe auf einem Gateway
 * nicht. Wer nichts wählt, migriert die betroffenen Instanzen nicht.
 */
export function MigrationNodeMapping({
  mapping,
  choices,
  onChoose,
  disabled,
}: MigrationNodeMappingProps) {
  const groupId = useId();
  // Eine ältere API kennt den Block noch nicht; dann ist hier schlicht nichts zuzuordnen.
  const rows = migrationMappingRows(mapping?.required ?? [], choices);
  if (rows.length === 0) return null;

  return (
    <section className="border-border mb-4 rounded-[var(--r)] border p-3">
      <h3 className="m-0 text-[13.5px] font-semibold">Knoten von Hand zuordnen</h3>
      <p className="text-muted mt-1 mb-3 text-[13px]">
        Die Zuordnung gilt für alle ausgewählten Instanzen und verschiebt nur den Punkt, an dem
        eine Instanz steht: Variablen, Aufgaben-ID, Übernahme und Fristen bleiben, das Formular
        kommt aus der Zielversion.
      </p>

      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {rows.map((row, index) => {
          const selectId = `${groupId}-${index}`;
          return (
            <li key={row.source.id} className="flex flex-wrap items-center gap-2">
              <label htmlFor={selectId} className="min-w-0 flex-1 truncate text-[13px]">
                {flowNodeLabel(row.source)}
                <span className="sr-only"> — Ziel in der Zielversion</span>
              </label>
              <span aria-hidden="true" className="text-faint">
                →
              </span>
              <select
                id={selectId}
                className={SELECT_STYLE}
                value={row.targetId}
                disabled={disabled}
                onChange={(event) => onChoose(row.source, event.target.value)}
              >
                <option value="">nicht zuordnen</option>
                {migrationTargetOptions(mapping.targets ?? [], row).map((target) => (
                  <option key={target.id} value={target.id}>
                    {flowNodeLabel(target)}
                  </option>
                ))}
              </select>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
