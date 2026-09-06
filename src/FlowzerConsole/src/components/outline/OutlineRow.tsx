import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';
import { canMove, moveBlock, removeBlock } from '@/lib/outline/edit';
import {
  blockLabel,
  type OutlineBlock,
  type OutlineDocument,
  type OutlineStep,
  type TaskKind,
} from '@/lib/outline/model';

const STEP_ICON: Record<TaskKind, string> = {
  user: 'person',
  service: 'settings',
};

/** Die Schiene links neben einer Zeile: Knoten und weiterfuehrende Linie. */
export function Rail({ block }: { block: OutlineBlock }) {
  const gateway = block.kind === 'parallel' || block.kind === 'choice';

  return (
    <div className="flex h-full flex-col items-center">
      <span
        className={cn(
          'border-border-strong bg-surface grid h-[22px] w-[22px] flex-none place-items-center border-2',
          gateway ? 'rotate-45 rounded-[5px]' : 'rounded-full',
        )}
      >
        {block.kind === 'step' && <Icon name={STEP_ICON[block.task]} size={13} className="text-muted" />}
        {block.kind === 'end' && <Icon name="stop_circle" size={13} className="text-muted" />}
      </span>
      {block.kind !== 'end' && <span className="bg-border min-h-5 w-0.5 flex-1" />}
    </div>
  );
}

export function Meta({ block }: { block: OutlineBlock }) {
  if (block.kind !== 'step') {
    const hint =
      block.kind === 'parallel'
        ? `${block.branches.length} Zweige laufen gleichzeitig`
        : block.kind === 'choice'
          ? `${block.branches.length} Möglichkeiten`
          : 'Der Ablauf endet hier.';
    return <div className="text-muted mt-0.5 text-[12px]">{hint}</div>;
  }

  return (
    <div className="text-muted mt-1 flex flex-wrap items-center gap-1.5 text-[12px]">
      {block.task === 'user' ? (
        <>
          <Chip tone="accent">{block.formKey ?? block.formId ?? 'ohne Formular'}</Chip>
          <span>{describeAssignment(block)}</span>
          {block.dueDate && (
            <Chip tone="wait">
              <Icon name="schedule" size={13} />
              {block.dueDate}
            </Chip>
          )}
        </>
      ) : (
        <Chip tone="run">{block.workerType ?? 'ohne Dienst'}</Chip>
      )}
    </div>
  );
}

function describeAssignment(step: OutlineStep): string {
  const parts = [step.assignee, step.candidateUsers, step.candidateGroups].filter(
    (value): value is string => Boolean(value?.trim()),
  );
  return parts.length > 0 ? parts.join(' · ') : 'ohne Zuständigkeit';
}

interface RowActionsProps {
  block: OutlineBlock;
  document: OutlineDocument;
  onChange: (next: OutlineDocument) => void;
}

/** Verschieben und Entfernen direkt an der Zeile. */
export function RowActions({ block, document, onChange }: RowActionsProps) {
  const actions: { icon: string; title: string; enabled: boolean; run: () => OutlineDocument }[] = [
    {
      icon: 'arrow_upward',
      title: 'Nach oben',
      enabled: canMove(document, block.id, 'up'),
      run: () => moveBlock(document, block.id, 'up'),
    },
    {
      icon: 'arrow_downward',
      title: 'Nach unten',
      enabled: canMove(document, block.id, 'down'),
      run: () => moveBlock(document, block.id, 'down'),
    },
    {
      icon: 'delete',
      title: 'Entfernen',
      enabled: true,
      run: () => removeBlock(document, block.id),
    },
  ];

  return (
    <div className="flex flex-none items-center gap-0.5">
      {actions.map((action) => (
        <span
          key={action.icon}
          role="button"
          tabIndex={0}
          aria-label={`${action.title}: ${blockLabel(block)}`}
          aria-disabled={!action.enabled}
          onClick={(event) => {
            event.stopPropagation();
            if (action.enabled) onChange(action.run());
          }}
          onKeyDown={(event) => {
            if (event.key !== 'Enter' && event.key !== ' ') return;
            event.preventDefault();
            event.stopPropagation();
            if (action.enabled) onChange(action.run());
          }}
          className={cn(
            'grid h-7 w-7 place-items-center rounded-md',
            action.enabled ? 'text-muted hover:text-text hover:bg-surface-2 cursor-pointer' : 'text-faint opacity-40',
          )}
        >
          <Icon name={action.icon} size={16} />
        </span>
      ))}
    </div>
  );
}
