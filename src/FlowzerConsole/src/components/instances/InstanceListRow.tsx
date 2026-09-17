import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { instanceBucket } from '@/lib/api/normalize';
import type { ProcessInstanceInfoDto } from '@/lib/api/types';
import { formatTimestamp, formatVersion, shortId } from '@/lib/format';
import { BUCKET_TONE, STATE_LABEL, waitingBadges } from '@/lib/instanceView';

/**
 * Die Tabellenspalten gelten erst ab `md`. Auf einem Telefon blieben von fuenf festen
 * Spalten Streifen von wenigen Zentimetern uebrig — die Ueberschriften ueberlagerten
 * sich gegenseitig und vom Workflownamen war nichts mehr zu lesen. Dort wird aus der
 * Zeile eine Karte: Name und Schritt je eine eigene Zeile, der Rest laeuft darunter um.
 */
export const INSTANCE_GRID = 'md:grid-cols-[minmax(0,1.5fr)_minmax(0,1.5fr)_150px_104px_116px]';

/** Breite der Auswahlspalte samt linkem Rand — Kopfzeile und Zeilen teilen sie sich. */
export const INSTANCE_SELECTION_CELL = 'w-[42px] flex-none pl-[18px]';

export interface InstanceRowSelection {
  /** Nur laufende Instanzen mit Betriebsrecht lassen sich auswaehlen. */
  selectable: boolean;
  selected: boolean;
  onChange: (selected: boolean) => void;
}

interface InstanceListRowProps {
  instance: ProcessInstanceInfoDto;
  /** Der aktuelle Schritt als Text; die Seite kennt dafuer die BPMN-Modelle. */
  stepName: string;
  onOpen: () => void;
  /** Fehlt, wenn die Liste ueberhaupt keine auswaehlbare Instanz enthaelt. */
  selection?: InstanceRowSelection;
}

/**
 * Eine Zeile der Instanzliste.
 *
 * Kaestchen und Navigation stehen nebeneinander statt ineinander: Ein Kaestchen im
 * navigierenden `<button>` waere ein Bedienelement im Bedienelement — die Tastatur
 * erreichte es nicht, und jeder Klick darauf oeffnete zusaetzlich die Instanz.
 */
export function InstanceListRow({ instance, stepName, onOpen, selection }: InstanceListRowProps) {
  const bucket = instanceBucket(instance.state);
  const badges = waitingBadges(instance);

  return (
    <div className="border-border hover:bg-inset flex items-center border-t">
      {selection && (
        <div className={INSTANCE_SELECTION_CELL}>
          {selection.selectable ? (
            <input
              type="checkbox"
              checked={selection.selected}
              onChange={(event) => selection.onChange(event.target.checked)}
              aria-label={`Instanz #${shortId(instance.instanceId)} auswählen`}
              className="accent-accent h-4 w-4 cursor-pointer"
            />
          ) : (
            <span className="block h-4 w-4" />
          )}
        </div>
      )}

      <button
        type="button"
        onClick={onOpen}
        className={`text-text flex min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-2 md:grid ${INSTANCE_GRID} cursor-pointer md:items-center md:gap-4 border-none bg-transparent px-[18px] py-3.5 text-left`}
      >
        <div className="w-full min-w-0 md:w-auto">
          <div className="truncate text-[14.5px] font-semibold">{instance.relatedDefinitionName}</div>
          <div className="text-faint mt-0.5 font-mono text-xs">
            #{shortId(instance.instanceId)} · {formatVersion(instance.definitionVersion)}
          </div>
        </div>

        <div className="w-full min-w-0 md:w-auto">
          <div className="text-muted truncate text-[13px]">
            {bucket === 'done' ? 'Abgeschlossen' : stepName}
          </div>
        </div>

        <div className="flex gap-1.5">
          {badges.map((badge) => (
            <span
              key={badge.icon}
              title={`${badge.count} ${badge.title}`}
              className="bg-surface-2 text-muted inline-flex items-center gap-1 rounded-[7px] px-2 py-0.5 text-xs font-semibold"
            >
              <Icon name={badge.icon} size={15} />
              {badge.count}
            </span>
          ))}
        </div>

        <div>
          <Chip tone={BUCKET_TONE[bucket]}>{STATE_LABEL[instance.state]}</Chip>
        </div>

        <div className="text-muted font-mono text-xs">{formatTimestamp(instance.startedAt)}</div>
      </button>
    </div>
  );
}
