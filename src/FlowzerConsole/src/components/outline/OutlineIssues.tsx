import { Icon } from '@/components/ui/Icon';
import { toneSurface } from '@/components/ui/Chip';
import type { OutlineIssue } from '@/lib/outline/model';

interface OutlineIssuesProps {
  issues: readonly OutlineIssue[];
  onOpenDiagram?: () => void;
}

/**
 * Die Meldungen sind der wichtigste Teil der Gliederung: Was sie nicht abbildet,
 * muss sichtbar werden, statt beim Speichern still verloren zu gehen.
 */
export function OutlineIssues({ issues, onOpenDiagram }: OutlineIssuesProps) {
  if (issues.length === 0) return null;

  const blockers = issues.filter((issue) => issue.level === 'blocker');
  const notes = issues.filter((issue) => issue.level === 'hinweis');
  const tone = blockers.length > 0 ? 'fail' : 'wait';

  return (
    <div
      className="rounded-[var(--r)] border px-4 py-3"
      style={{ background: toneSurface(tone, 10), borderColor: `color-mix(in oklab, var(--${tone}) 34%, transparent)` }}
    >
      <div className="flex items-center gap-2 text-[13.5px] font-semibold" style={{ color: `var(--${tone})` }}>
        <Icon name={blockers.length > 0 ? 'block' : 'info'} size={18} />
        {blockers.length > 0
          ? 'Dieser Workflow lässt sich in der Gliederung nicht vollständig abbilden'
          : 'Hinweis zum Speichern'}
      </div>

      <ul className="text-muted mt-2 flex list-disc flex-col gap-1 pl-5 text-[12.5px]">
        {[...blockers, ...notes].map((issue, index) => (
          <li key={`${issue.elementId ?? ''}-${index}`}>
            {issue.message}
            {issue.elementId && <span className="text-faint font-mono"> ({issue.elementId})</span>}
          </li>
        ))}
      </ul>

      {blockers.length > 0 && (
        <p className="text-muted mt-2.5 text-[12.5px]">
          Die Gliederung zeigt ihn deshalb nicht an und speichert ihn nicht.{' '}
          {onOpenDiagram && (
            <button
              type="button"
              onClick={onOpenDiagram}
              className="text-accent cursor-pointer border-none bg-transparent p-0 font-semibold underline"
            >
              Im Diagramm bearbeiten
            </button>
          )}
        </p>
      )}
    </div>
  );
}
