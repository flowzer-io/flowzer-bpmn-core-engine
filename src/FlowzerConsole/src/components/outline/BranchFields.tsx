import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { addBranch, removeBranch, updateBranch } from '@/lib/outline/edit';
import type { OutlineChoice, OutlineDocument, OutlineParallel } from '@/lib/outline/model';

interface BranchFieldsProps {
  document: OutlineDocument;
  block: OutlineParallel | OutlineChoice;
  onChange: (next: OutlineDocument) => void;
}

/** Die Zweige eines Blocks: bei einer Verzweigung mit Beschriftung und Bedingung. */
export function BranchFields({ document, block, onChange }: BranchFieldsProps) {
  return (
    <div className="flex flex-col gap-4">
      {block.kind === 'choice'
        ? block.branches.map((branch, index) => (
            <div key={branch.flowId ?? index} className="border-border rounded-[var(--r-sm)] border p-3">
              <div className="mb-2 flex items-center justify-between">
                <FieldLabel className="mb-0">Zweig {index + 1}</FieldLabel>
                <RemoveBranch document={document} block={block} index={index} onChange={onChange} />
              </div>

              <TextInput
                value={branch.label ?? ''}
                placeholder="Beschriftung, z. B. „ja“"
                onChange={(event) =>
                  onChange(updateBranch(document, block.id, index, { label: event.target.value || undefined }))
                }
              />

              <label className="text-muted mt-2.5 flex cursor-pointer items-center gap-2 text-[12.5px]">
                <input
                  type="checkbox"
                  checked={branch.isDefault}
                  onChange={(event) =>
                    onChange(
                      updateBranch(document, block.id, index, {
                        isDefault: event.target.checked,
                        condition: event.target.checked ? undefined : branch.condition,
                      }),
                    )
                  }
                />
                Standardweg — greift, wenn keine andere Bedingung zutrifft
              </label>

              {!branch.isDefault && (
                <div className="mt-2.5">
                  <FieldLabel>Bedingung</FieldLabel>
                  <TextInput
                    className="font-mono text-[12.5px]"
                    value={branch.condition ?? ''}
                    placeholder='z. B. =entscheidung = "freigegeben"'
                    onChange={(event) =>
                      onChange(updateBranch(document, block.id, index, { condition: event.target.value || undefined }))
                    }
                  />
                </div>
              )}
            </div>
          ))
        : block.branches.map((branch, index) => (
            <div
              key={branch.flowId ?? index}
              className="border-border flex items-center justify-between rounded-[var(--r-sm)] border px-3 py-2.5"
            >
              <span className="text-[13px] font-semibold">
                Zweig {index + 1} · {branch.blocks.length} Schritte
              </span>
              <RemoveBranch document={document} block={block} index={index} onChange={onChange} />
            </div>
          ))}

      <Button size="sm" icon="add" onClick={() => onChange(addBranch(document, block.id))}>
        Zweig hinzufügen
      </Button>
    </div>
  );
}

interface RemoveBranchProps extends BranchFieldsProps {
  index: number;
}

function RemoveBranch({ document, block, index, onChange }: RemoveBranchProps) {
  if (block.branches.length <= 2) return null;

  return (
    <button
      type="button"
      title="Zweig entfernen"
      onClick={() => onChange(removeBranch(document, block.id, index))}
      className="text-muted hover:text-fail grid h-7 w-7 cursor-pointer place-items-center rounded-md border-none bg-transparent"
    >
      <Icon name="delete" size={16} />
      <span className="sr-only">Zweig {index + 1} entfernen</span>
    </button>
  );
}
