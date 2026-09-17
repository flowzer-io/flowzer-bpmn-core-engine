import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';

interface MigrationInstanceRowProps {
  /** Kurzkennung der Instanz, z. B. „#A1B2-C3D“. */
  title: string;
  /** Migrierbar bzw. migriert — steuert nur den Ton, nie die Aussage allein. */
  ok: boolean;
  label: string;
  problems?: string[];
  notices?: string[];
}

/**
 * Eine Instanz im Migrationsassistenten: Kennung, Einstufung und die Sätze dazu.
 *
 * Die Einstufung steht als Text im Chip und nicht nur als Farbe — rot und grün
 * unterscheiden sich für einen Teil der Belegschaft nicht. Probleme und Hinweise
 * tragen zusätzlich verschiedene Symbole.
 */
export function MigrationInstanceRow({
  title,
  ok,
  label,
  problems = [],
  notices = [],
}: MigrationInstanceRowProps) {
  return (
    <li className="border-border rounded-[var(--r)] border p-3">
      <div className="flex items-center gap-2.5">
        <span className="font-mono text-[12.5px] font-semibold">{title}</span>
        <Chip tone={ok ? 'done' : 'fail'}>{label}</Chip>
      </div>

      {(problems.length > 0 || notices.length > 0) && (
        <ul className="m-0 mt-2 flex list-none flex-col gap-1.5 p-0">
          {problems.map((text) => (
            <li key={text} className="text-muted flex gap-2 text-[13px]">
              <Icon name="error" size={17} className="text-fail mt-px flex-none" />
              <span>{text}</span>
            </li>
          ))}
          {notices.map((text) => (
            <li key={text} className="text-muted flex gap-2 text-[13px]">
              <Icon name="info" size={17} className="text-wait mt-px flex-none" />
              <span>{text}</span>
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}
