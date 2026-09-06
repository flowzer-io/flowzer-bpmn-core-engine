import { BranchFields } from '@/components/outline/BranchFields';
import { StepFields } from '@/components/outline/StepFields';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/Card';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { insertAfter, insertBefore, newChoice, newParallel, newStep, renameBlock } from '@/lib/outline/edit';
import { blockLabel, type OutlineBlock, type OutlineDocument } from '@/lib/outline/model';

interface BlockEditorProps {
  document: OutlineDocument;
  block: OutlineBlock | undefined;
  editable: boolean;
  onChange: (next: OutlineDocument) => void;
}

const KIND_LABELS: Record<OutlineBlock['kind'], string> = {
  step: 'Schritt',
  parallel: 'Gleichzeitig',
  choice: 'Verzweigung',
  end: 'Ende',
};

/** Die rechte Spalte: alles, was am ausgewaehlten Block einstellbar ist. */
export function BlockEditor({ document, block, editable, onChange }: BlockEditorProps) {
  if (!block) {
    return (
      <EmptyState
        icon="touch_app"
        title="Nichts ausgewählt"
        description="Wähle links einen Schritt, um Formular, Zuständigkeit und Frist zu bearbeiten."
      />
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div>
        <FieldLabel>{KIND_LABELS[block.kind]}</FieldLabel>
        <TextInput
          value={block.name ?? ''}
          disabled={!editable}
          placeholder={blockLabel(block)}
          onChange={(event) => onChange(renameBlock(document, block.id, event.target.value))}
        />
        <p className="text-faint mt-1.5 font-mono text-[11px]">{block.id}</p>
      </div>

      {editable && block.kind === 'step' && <StepFields document={document} step={block} onChange={onChange} />}
      {editable && (block.kind === 'parallel' || block.kind === 'choice') && (
        <BranchFields document={document} block={block} onChange={onChange} />
      )}

      {editable && <InsertActions document={document} block={block} onChange={onChange} />}
    </div>
  );
}

interface InsertActionsProps {
  document: OutlineDocument;
  block: OutlineBlock;
  onChange: (next: OutlineDocument) => void;
}

/** Neue Bloecke entstehen relativ zum ausgewaehlten — vor dem Ende, sonst dahinter. */
function InsertActions({ document, block, onChange }: InsertActionsProps) {
  const before = block.kind === 'end';
  const place = (created: OutlineBlock) =>
    onChange(
      before ? insertBefore(document, block.id, created) : insertAfter(document, block.id, created),
    );

  const options = [
    { icon: 'person', label: 'Schritt für Menschen', create: () => newStep(document, 'user') },
    { icon: 'settings', label: 'Dienstaufruf', create: () => newStep(document, 'service') },
    { icon: 'call_split', label: 'Verzweigung', create: () => newChoice(document) },
    { icon: 'add', label: 'Gleichzeitig', create: () => newParallel(document) },
  ];

  return (
    <div className="border-border flex flex-col gap-2 border-t pt-4">
      <FieldLabel>{before ? 'Davor einfügen' : 'Danach einfügen'}</FieldLabel>
      {options.map((option) => (
        <Button key={option.label} size="sm" icon={option.icon} onClick={() => place(option.create())}>
          {option.label}
        </Button>
      ))}
    </div>
  );
}
