import { Button } from '@/components/ui/Button';
import { Chip, Dot } from '@/components/ui/Chip';
import { SectionLabel } from '@/components/ui/Card';
import type { CalledInstanceDto } from '@/lib/api/types';
import { formatVersion, shortId } from '@/lib/format';
import { instanceTone, STATE_LABEL } from '@/lib/instanceView';

/**
 * Der Aufrufer dieser Instanz.
 *
 * Bewusst im Kopf der Instanz und nicht in einem Reiter: Eine Kindinstanz ist ohne ihren
 * Aufrufer nicht zu verstehen — sie ist nirgends gestartet worden, sondern ein Schritt eines
 * anderen Vorgangs. Das gehört neben Name, Version und Kennung und nicht in die Diagnose.
 */
export function ParentInstanceLink({ parentInstanceId, onOpen }: {
  parentInstanceId: string;
  onOpen: (instanceId: string) => void;
}) {
  return (
    <Button
      variant="ghost"
      size="sm"
      icon="call_merge"
      title="Zur aufrufenden Instanz"
      className="border-border h-[34px] border"
      onClick={() => onOpen(parentInstanceId)}
    >
      Aufgerufen von #{shortId(parentInstanceId)}
    </Button>
  );
}

/**
 * Die von Call Activities dieser Instanz gestarteten Vorgänge.
 *
 * Kein eigener Reiter: Die allermeisten Instanzen rufen nichts auf, und ein dauerhaft
 * sichtbarer Reiter, der fast immer leer ist, kostet in jeder Instanz Platz und Aufmerksamkeit.
 * Der Abschnitt steht deshalb bei den Warteobjekten — dort steht ohnehin, worauf ein Vorgang
 * außerhalb seines eigenen Diagramms wartet, und die Call Activity ist genau so ein Wartepunkt.
 * Ohne Kindinstanzen entfällt er ganz; ein leerer Kasten wäre reines Rauschen.
 */
export function CalledInstancesSection({ instances, onOpen }: {
  instances: CalledInstanceDto[];
  onOpen: (instanceId: string) => void;
}) {
  if (instances.length === 0) return null;

  return (
    <section className="mt-6">
      <SectionLabel className="mb-3.5">Aufgerufene Vorgänge</SectionLabel>

      <div className="flex flex-col gap-2.5">
        {instances.map((instance) => (
          <button
            key={instance.instanceId}
            type="button"
            onClick={() => onOpen(instance.instanceId)}
            className="border-border hover:bg-inset text-text flex w-full cursor-pointer items-center gap-3 rounded-[var(--r)] border bg-transparent p-3 text-left"
          >
            <Dot tone={instanceTone(instance.state)} size={10} halo className="flex-none" />
            <div className="min-w-0 flex-1">
              <div className="truncate text-[13px] font-semibold">{instance.relatedDefinitionName}</div>
              <div className="text-faint mt-0.5 truncate font-mono text-[11.5px]">
                #{shortId(instance.instanceId)} · {formatVersion(instance.definitionVersion)}
              </div>
            </div>
            <Chip tone={instanceTone(instance.state)}>{STATE_LABEL[instance.state]}</Chip>
          </button>
        ))}
      </div>
    </section>
  );
}
