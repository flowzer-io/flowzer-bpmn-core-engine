import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';
import { mainPath, type BpmnModelSummary } from '@/lib/bpmnModel';

interface TaskStepperProps {
  model: BpmnModelSummary;
  /** Der Knoten, auf dem die Instanz gerade steht. */
  currentNodeId: string | null | undefined;
  className?: string;
}

/**
 * Zeigt, an welcher Stelle des Prozesses die Aufgabe steht.
 * Grundlage ist der Hauptpfad des BPMN-Modells (siehe `mainPath`).
 */
export function TaskStepper({ model, currentNodeId, className }: TaskStepperProps) {
  const path = mainPath(model, currentNodeId);
  if (path.length === 0) return null;

  const currentIndex = path.findIndex((node) => node.id === currentNodeId);
  const nextStep = currentIndex >= 0 ? path[currentIndex + 1] : undefined;

  return (
    <div
      className={cn(
        'bg-surface border-border shadow-card rounded-[var(--r-lg)] border px-[18px] py-3.5',
        className,
      )}
    >
      <div className="flex items-center overflow-hidden">
        {path.map((node, index) => {
          const done = currentIndex >= 0 && index < currentIndex;
          const active = index === currentIndex;

          return (
            <div key={node.id} className="flex min-w-0 items-center">
              {index > 0 && <span className="bg-border mx-2 h-0.5 min-w-2 flex-[1_1_10px]" />}
              <div className={cn('flex items-center gap-2', active ? 'flex-none' : 'min-w-0 flex-[0_1_auto]')}>
                <span
                  className="grid h-6 w-6 flex-none place-items-center rounded-full"
                  style={
                    done
                      ? { background: 'var(--done)', color: '#fff' }
                      : active
                        ? { background: 'var(--accent)', color: 'var(--accent-ink)' }
                        : { background: 'var(--surface-2)', color: 'var(--faint)' }
                  }
                >
                  <Icon name={done ? 'check' : active ? 'edit' : 'more_horiz'} size={15} />
                </span>
                <span
                  className={cn(
                    'truncate text-[12.5px]',
                    active ? 'text-text font-bold' : done ? 'text-muted font-medium' : 'text-faint font-medium',
                  )}
                >
                  {node.name?.trim() || node.id}
                </span>
              </div>
            </div>
          );
        })}
      </div>

      {nextStep && (
        <div className="border-border text-muted mt-3 flex items-center gap-2 border-t pt-2.5 text-[12.5px]">
          <Icon name="arrow_forward" size={16} className="text-accent" />
          Im Anschluss: {nextStep.name?.trim() || nextStep.id}
        </div>
      )}
    </div>
  );
}
