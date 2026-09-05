import { Chip, toneColor, toneSurface } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';
import { DUE_BUCKET_TONE, PRIORITY_TONE, taskIcon, type TaskView } from '@/lib/taskView';

interface TaskRowProps {
  view: TaskView;
  onOpen: () => void;
  className?: string;
}

/** Eine Aufgabenzeile, wie sie das Dashboard und die Aufgabenliste verwenden. */
export function TaskRow({ view, onOpen, className }: TaskRowProps) {
  const tone = view.dueBucket === 'overdue' ? 'fail' : 'accent';

  return (
    <button
      type="button"
      onClick={onOpen}
      className={cn(
        'border-border hover:bg-inset flex w-full cursor-pointer items-center gap-3.5 border-t',
        'border-x-0 border-b-0 bg-transparent px-1.5 py-3.5 text-left',
        className,
      )}
    >
      <span
        className="grid h-[38px] w-[38px] flex-none place-items-center rounded-[10px]"
        style={{ background: toneSurface(tone, 11), color: toneColor(tone) }}
      >
        <Icon name={taskIcon(view)} size={20} />
      </span>

      <span className="min-w-0 flex-1">
        <span className="block truncate font-semibold">{view.title}</span>
        <span className="text-muted mt-0.5 block truncate text-[13px]">
          {view.workflowName}
          {view.task.definitionVersion && (
            <> · v{view.task.definitionVersion.major}.{view.task.definitionVersion.minor}</>
          )}
        </span>
      </span>

      <span className="flex flex-none flex-col items-end gap-1.5">
        {view.priority && <Chip tone={PRIORITY_TONE[view.priority]}>{view.priority}</Chip>}
        <span
          className="text-[12.5px] font-medium"
          style={{ color: view.dueBucket === 'overdue' ? 'var(--fail)' : 'var(--muted)' }}
        >
          {view.dueLabel}
        </span>
      </span>

      <Icon name="chevron_right" size={20} className="text-faint flex-none" />
    </button>
  );
}

/** Gruppenüberschrift über einer Aufgabenliste. */
export function TaskGroupHeading({
  label,
  count,
  bucket,
}: {
  label: string;
  count: number;
  bucket?: keyof typeof DUE_BUCKET_TONE;
}) {
  const tone = bucket ? DUE_BUCKET_TONE[bucket] : 'muted';

  return (
    <div
      className="flex items-center gap-2 px-0 pt-4 pb-1.5 font-mono text-[11px] font-semibold tracking-[0.1em] uppercase"
      style={{ color: tone === 'fail' ? 'var(--fail)' : 'var(--muted)' }}
    >
      <span className="h-1.5 w-1.5 rounded-full" style={{ background: toneColor(tone) }} />
      {label} · {count}
    </div>
  );
}
