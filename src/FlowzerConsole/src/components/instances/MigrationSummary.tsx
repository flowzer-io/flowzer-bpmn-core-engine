import type { VersionDto } from '@/lib/api/types';
import { formatVersion } from '@/lib/format';
import { instanceCountLabel } from '@/lib/instanceMigration';

interface MigrationSummaryProps {
  sourceVersion: VersionDto | null;
  targetVersion: VersionDto;
  /** Alle geprüften Instanzen. */
  total: number;
  /** Die davon migrierbaren — nur sie werden gesendet. */
  migratable: number;
  acknowledged: boolean;
  onAcknowledgedChange: (acknowledged: boolean) => void;
}

/**
 * Was der Klick auf „migrieren“ bewirkt — und die Bestätigung, ohne die er nicht geht.
 *
 * Der Satz nennt immer beide Zahlen: Wer drei Instanzen ausgewählt hat und nur zwei
 * migriert, muss das vorher lesen und nicht erst am Ergebnis bemerken. Zurücknehmen lässt
 * sich der Schritt nicht, deshalb hängt er an einer ausdrücklichen Zustimmung.
 */
export function MigrationSummary({
  sourceVersion,
  targetVersion,
  total,
  migratable,
  acknowledged,
  onAcknowledgedChange,
}: MigrationSummaryProps) {
  if (migratable === 0) {
    return (
      <div className="border-border mt-4 border-t pt-3.5">
        <p className="text-muted m-0 text-[13.5px]">
          Keine der ausgewählten Instanzen lässt sich auf {formatVersion(targetVersion)} heben. Es
          wird nichts geändert.
        </p>
      </div>
    );
  }

  const summary =
    migratable === total
      ? `${instanceCountLabel(migratable)} ${migratable === 1 ? 'wird' : 'werden'} auf ${formatVersion(targetVersion)} migriert.`
      : `${migratable} von ${instanceCountLabel(total)} werden migriert. Nicht migrierbare bleiben unverändert auf ${formatVersion(sourceVersion)}.`;

  return (
    <div className="border-border mt-4 border-t pt-3.5">
      <p className="m-0 text-[13.5px]">{summary}</p>
      <label className="mt-3 flex cursor-pointer items-start gap-2.5 text-[13.5px]">
        <input
          type="checkbox"
          checked={acknowledged}
          onChange={(event) => onAcknowledgedChange(event.target.checked)}
          className="accent-accent mt-0.5 h-4 w-4 cursor-pointer"
        />
        Mir ist bewusst, dass sich die Migration nicht rückgängig machen lässt.
      </label>
    </div>
  );
}
