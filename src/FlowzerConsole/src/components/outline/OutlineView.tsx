import { Meta, Rail, RowActions } from '@/components/outline/OutlineRow';
import { toneSurface } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';
import { insertIntoBranch, newStep } from '@/lib/outline/edit';
import {
  blockLabel,
  branchLabel,
  type OutlineBlock,
  type OutlineChoice,
  type OutlineDocument,
  type OutlineParallel,
} from '@/lib/outline/model';

interface OutlineViewProps {
  document: OutlineDocument;
  selectedId: string | null;
  editable: boolean;
  onSelect: (id: string) => void;
  onChange: (next: OutlineDocument) => void;
}

/** Alles, was die verschachtelten Zeilen brauchen — als ein Prop statt sechs. */
interface RenderContext extends Omit<OutlineViewProps, 'document'> {
  document: OutlineDocument;
}

/**
 * Der Workflow als senkrechte Gliederung: Schritte untereinander, parallele
 * Bloecke und Zweige eingerueckt. Die Schiene links zeigt den Ablauf, rechts
 * steht, was den Schritt ausmacht.
 */
export function OutlineView({ document, selectedId, editable, onSelect, onChange }: OutlineViewProps) {
  const context: RenderContext = { document, selectedId, editable, onSelect, onChange };

  return (
    <div className="flex flex-col">
      <div className="grid grid-cols-[26px_1fr] items-start gap-3">
        <div className="flex h-full flex-col items-center">
          <span className="border-border-strong bg-surface grid h-[22px] w-[22px] place-items-center rounded-full border-2">
            <Icon name="play_arrow" size={13} className="text-muted" />
          </span>
          <span className="bg-border min-h-5 w-0.5 flex-1" />
        </div>
        <div className="pb-4">
          <div className="text-[14px] font-semibold">{document.startName?.trim() || 'Start'}</div>
          <div className="text-muted text-[12px]">Der Ablauf beginnt hier.</div>
        </div>
      </div>

      <Sequence blocks={document.blocks} fallthrough="" context={context} />
    </div>
  );
}

interface SequenceProps {
  blocks: readonly OutlineBlock[];
  /** Womit es weitergeht, wenn diese Folge zu Ende ist — fuer leere Zweige. */
  fallthrough: string;
  context: RenderContext;
}

function Sequence({ blocks, fallthrough, context }: SequenceProps) {
  if (blocks.length === 0) return null;

  return (
    <>
      {blocks.map((block, index) => {
        const following = blocks[index + 1];
        return (
          <BlockRow
            key={block.id}
            block={block}
            continuation={following ? blockLabel(following) : fallthrough}
            context={context}
          />
        );
      })}
    </>
  );
}

interface BlockRowProps {
  block: OutlineBlock;
  continuation: string;
  context: RenderContext;
}

function BlockRow({ block, continuation, context }: BlockRowProps) {
  const selected = context.selectedId === block.id;

  return (
    <div className="grid grid-cols-[26px_1fr] items-start gap-3">
      <Rail block={block} />
      <div className="min-w-0 pb-4">
        <button
          type="button"
          onClick={() => context.onSelect(block.id)}
          className={cn(
            'w-full cursor-pointer rounded-[var(--r-sm)] border px-3 py-2 text-left transition-colors',
            selected ? 'border-accent bg-surface' : 'border-transparent hover:bg-surface-2',
          )}
        >
          <div className="flex items-start gap-2">
            <div className="min-w-0 flex-1">
              <div className="truncate text-[14.5px] font-semibold">{blockLabel(block)}</div>
              <Meta block={block} />
            </div>
            {context.editable && (
              <RowActions block={block} document={context.document} onChange={context.onChange} />
            )}
          </div>
        </button>

        {block.kind === 'parallel' && <ParallelBranches block={block} context={context} />}
        {block.kind === 'choice' && (
          <ChoiceBranches block={block} continuation={continuation} context={context} />
        )}
      </div>
    </div>
  );
}

function BranchFrame({ children }: { children: React.ReactNode }) {
  return <div className="border-border-strong mt-1 ml-[5px] border-l-2 border-dashed pl-4">{children}</div>;
}

function ParallelBranches({ block, context }: { block: OutlineParallel; context: RenderContext }) {
  return (
    <BranchFrame>
      {block.branches.map((branch, index) => (
        <div key={branch.flowId ?? index} className="pt-2">
          <div className="text-faint font-mono text-[10.5px] tracking-[0.12em] uppercase">Zweig {index + 1}</div>
          <Sequence blocks={branch.blocks} fallthrough="" context={context} />
          {branch.blocks.length === 0 && <EmptyBranch block={block} index={index} context={context} />}
        </div>
      ))}
    </BranchFrame>
  );
}

interface ChoiceBranchesProps {
  block: OutlineChoice;
  continuation: string;
  context: RenderContext;
}

function ChoiceBranches({ block, continuation, context }: ChoiceBranchesProps) {
  return (
    <BranchFrame>
      {block.branches.map((branch, index) => (
        <div key={branch.flowId ?? index} className="pt-2">
          <div className="mb-1 flex flex-wrap items-baseline gap-2">
            <span
              className="rounded-full px-2 py-0.5 text-[11.5px] font-semibold"
              style={{ background: toneSurface(branch.isDefault ? 'muted' : 'accent'), color: 'var(--text)' }}
            >
              {branchLabel(branch)}
            </span>
            {!branch.isDefault && branch.condition && (
              <code className="text-muted font-mono text-[11.5px]">{branch.condition}</code>
            )}
          </div>
          <Sequence blocks={branch.blocks} fallthrough={continuation} context={context} />
          {branch.blocks.length === 0 && (
            <div className="text-muted flex flex-wrap items-center gap-2 pb-2 text-[12.5px]">
              <Icon name="subdirectory_arrow_right" size={15} className="text-faint" />
              {continuation ? `weiter mit „${continuation}"` : 'ohne eigenen Schritt'}
              {context.editable && <EmptyBranch block={block} index={index} context={context} />}
            </div>
          )}
        </div>
      ))}
    </BranchFrame>
  );
}

interface EmptyBranchProps {
  block: OutlineParallel | OutlineChoice;
  index: number;
  context: RenderContext;
}

function EmptyBranch({ block, index, context }: EmptyBranchProps) {
  if (!context.editable) return null;

  return (
    <button
      type="button"
      onClick={() =>
        context.onChange(insertIntoBranch(context.document, block.id, index, newStep(context.document, 'user')))
      }
      className="text-accent hover:bg-surface-2 inline-flex cursor-pointer items-center gap-1 rounded-md border-none bg-transparent px-1.5 py-1 text-[12.5px] font-semibold"
    >
      <Icon name="add" size={15} />
      Schritt hinzufügen
    </button>
  );
}
