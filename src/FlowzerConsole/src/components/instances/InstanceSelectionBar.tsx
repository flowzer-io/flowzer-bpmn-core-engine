import { useId, useState } from 'react';

import { Button } from '@/components/ui/Button';
import type { ProcessInstanceInfoDto } from '@/lib/api/types';
import { migrationSelectionProblem } from '@/lib/instanceMigration';

import { InstanceMigrationAssistant } from './InstanceMigrationAssistant';

interface InstanceSelectionBarProps {
  selected: ProcessInstanceInfoDto[];
  onClear: () => void;
}

/**
 * Leiste ueber der Instanzliste, solange etwas ausgewaehlt ist.
 *
 * Der Grund einer gesperrten Migration steht sichtbar daneben und nicht nur im `title`:
 * Ein gesperrter Knopf nimmt keinen Fokus an, sein Titel bliebe fuer Tastatur und
 * Vorlesewerkzeuge unerreichbar.
 */
export function InstanceSelectionBar({ selected, onClear }: InstanceSelectionBarProps) {
  const hintId = useId();
  const [assistantOpen, setAssistantOpen] = useState(false);
  // Nach einer Migration ist die Auswahl verbraucht — aufgehoben wird sie aber erst beim
  // Schliessen, damit das Ergebnis je Instanz noch zu lesen ist.
  const [migrated, setMigrated] = useState(false);

  const problem = migrationSelectionProblem(selected);

  return (
    <div
      role="region"
      aria-label="Ausgewählte Instanzen"
      className="border-border bg-surface-2 mb-4 flex flex-wrap items-center gap-3 rounded-[var(--r)] border px-3.5 py-2.5"
    >
      <span className="text-[13.5px] font-semibold">{selected.length} ausgewählt</span>

      {problem && (
        <span id={hintId} className="text-muted text-[13px]">
          {problem}
        </span>
      )}

      <span className="flex-1" />

      <Button
        size="sm"
        variant="primary"
        icon="upgrade"
        disabled={problem !== null}
        title={problem ?? undefined}
        aria-describedby={problem ? hintId : undefined}
        onClick={() => setAssistantOpen(true)}
      >
        Migrieren …
      </Button>

      <Button size="sm" variant="ghost" onClick={onClear}>
        Auswahl aufheben
      </Button>

      {assistantOpen && (
        <InstanceMigrationAssistant
          open
          onOpenChange={(open) => {
            setAssistantOpen(open);
            if (!open && migrated) {
              setMigrated(false);
              onClear();
            }
          }}
          instanceIds={selected.map((instance) => instance.instanceId)}
          onMigrated={() => setMigrated(true)}
        />
      )}
    </div>
  );
}
