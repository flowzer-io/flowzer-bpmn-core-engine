import { useMemo } from 'react';

import { Icon } from '@/components/ui/Icon';
import type { WorkflowFolderDto } from '@/lib/api/types';
import { cn } from '@/lib/cn';
import { buildFolderTree } from '@/lib/folderTree';

interface FolderTreeProps {
  folders: readonly WorkflowFolderDto[];
  /** Gewählter Ordner; `null` ist die oberste Ebene. */
  selectedId: string | null;
  expandedIds: ReadonlySet<string>;
  /** Anzahl der Workflows ohne Ordner — die Zahl neben „Alle Workflows". */
  rootWorkflowCount: number;
  onSelect: (folderId: string | null) => void;
  onToggle: (folderId: string) => void;
  /** Ein Workflow wurde auf einen Ordner gezogen. */
  onDropWorkflow?: (definitionId: string, folderId: string | null) => void;
}

/**
 * Die Ordnerspalte neben der Kachelwand.
 *
 * Der Baum steht immer offen statt eine Ebene nach der anderen zu zeigen: Wer einen
 * Workflow sucht, soll die Ablage sehen und nicht danach raten. Ein Stift markiert die
 * Ordner, in denen die angemeldete Person bearbeiten darf — die uebrigen bleiben sichtbar
 * und benutzbar, nur eben zum Ansehen und Starten.
 */
export function FolderTree({
  folders,
  selectedId,
  expandedIds,
  rootWorkflowCount,
  onSelect,
  onToggle,
  onDropWorkflow,
}: FolderTreeProps) {
  const nodes = useMemo(() => buildFolderTree(folders, expandedIds), [folders, expandedIds]);

  return (
    <nav aria-label="Ordner" className="flex flex-col gap-px">
      <FolderRow
        icon="home_storage"
        label="Alle Workflows"
        count={rootWorkflowCount}
        depth={0}
        selected={selectedId === null}
        onSelect={() => onSelect(null)}
        onDropWorkflow={onDropWorkflow ? (definitionId) => onDropWorkflow(definitionId, null) : undefined}
      />

      {nodes.map(({ folder, depth, hasChildren }) => (
        <FolderRow
          key={folder.id}
          icon={expandedIds.has(folder.id) && hasChildren ? 'folder_open' : 'folder'}
          label={folder.name}
          count={folder.workflowCount}
          depth={depth + 1}
          selected={selectedId === folder.id}
          expandable={hasChildren}
          expanded={expandedIds.has(folder.id)}
          mayEdit={folder.mayEdit}
          onToggle={() => onToggle(folder.id)}
          onSelect={() => onSelect(folder.id)}
          onDropWorkflow={
            onDropWorkflow && folder.mayEdit
              ? (definitionId) => onDropWorkflow(definitionId, folder.id)
              : undefined
          }
        />
      ))}

      {folders.length === 0 && (
        <p className="text-faint px-2 py-3 text-[12.5px] leading-normal">
          Noch keine Ordner. Mit „Ordner“ oben lässt sich eine Struktur anlegen.
        </p>
      )}
    </nav>
  );
}

interface FolderRowProps {
  icon: string;
  label: string;
  count: number;
  depth: number;
  selected: boolean;
  expandable?: boolean;
  expanded?: boolean;
  mayEdit?: boolean;
  onSelect: () => void;
  onToggle?: () => void;
  onDropWorkflow?: (definitionId: string) => void;
}

/** Datenformat des Ziehens: die Kennung des Workflows, den die Karte trägt. */
export const WORKFLOW_DRAG_TYPE = 'application/x-flowzer-definition';

function FolderRow({
  icon,
  label,
  count,
  depth,
  selected,
  expandable = false,
  expanded = false,
  mayEdit = false,
  onSelect,
  onToggle,
  onDropWorkflow,
}: FolderRowProps) {
  return (
    <div
      className={cn(
        'group flex items-center gap-1.5 rounded-lg py-1.5 pr-2 text-[13px] whitespace-nowrap',
        selected ? 'text-accent font-semibold' : 'text-muted hover:bg-surface-2',
        onDropWorkflow && 'data-[dropzone=on]:outline-accent data-[dropzone=on]:outline data-[dropzone=on]:outline-1',
      )}
      style={{
        paddingLeft: 6 + depth * 13,
        background: selected ? 'color-mix(in oklab, var(--accent) 12%, transparent)' : undefined,
      }}
      onDragOver={
        onDropWorkflow
          ? (event) => {
              if (!event.dataTransfer.types.includes(WORKFLOW_DRAG_TYPE)) return;
              event.preventDefault();
              event.currentTarget.dataset.dropzone = 'on';
            }
          : undefined
      }
      onDragLeave={onDropWorkflow ? (event) => delete event.currentTarget.dataset.dropzone : undefined}
      onDrop={
        onDropWorkflow
          ? (event) => {
              delete event.currentTarget.dataset.dropzone;
              const definitionId = event.dataTransfer.getData(WORKFLOW_DRAG_TYPE);
              if (!definitionId) return;
              event.preventDefault();
              onDropWorkflow(definitionId);
            }
          : undefined
      }
    >
      {/* Aufklappen und Auswaehlen sind zwei Handlungen und deshalb zwei Schaltflaechen:
          Sonst wechselte jeder Klick auf einen Ordner mit Unterordnern zugleich die Ansicht. */}
      {expandable ? (
        <button
          type="button"
          aria-label={expanded ? `${label} zuklappen` : `${label} aufklappen`}
          aria-expanded={expanded}
          className="text-faint hover:text-text -my-1 grid h-5 w-4 flex-none cursor-pointer place-items-center border-none bg-transparent p-0"
          onClick={onToggle}
        >
          <Icon
            name="chevron_right"
            size={16}
            className={cn('transition-transform duration-150', expanded && 'rotate-90')}
          />
        </button>
      ) : (
        <span className="w-4 flex-none" />
      )}

      <button
        type="button"
        className="flex min-w-0 flex-1 cursor-pointer items-center gap-1.5 border-none bg-transparent p-0 text-left text-inherit"
        aria-current={selected ? 'true' : undefined}
        onClick={onSelect}
      >
        <Icon name={icon} size={16} className="flex-none" />
        <span className="min-w-0 flex-1 truncate">{label}</span>
        {mayEdit && (
          <Icon name="edit" size={13} className="text-accent flex-none" title="Sie dürfen hier bearbeiten" />
        )}
      </button>

      <span className="text-faint flex-none text-[11px] tabular-nums">{count}</span>
    </div>
  );
}
