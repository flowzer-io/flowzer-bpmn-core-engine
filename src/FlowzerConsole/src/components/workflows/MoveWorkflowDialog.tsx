import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { FieldLabel } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import type { WorkflowFolderDto } from '@/lib/api/types';
import { pathLabel, sortByPath } from '@/lib/folderTree';

interface MoveWorkflowDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workflowName: string;
  currentFolderId: string | null;
  folders: readonly WorkflowFolderDto[];
  /** Darf die Person Workflows auf der obersten Ebene ablegen? */
  mayUseRoot: boolean;
  busy?: boolean;
  onSubmit: (folderId: string | null) => void;
}

/**
 * Verschiebt einen Workflow in einen anderen Ordner.
 *
 * Ziehen und Ablegen im Baum tut dasselbe und ist schneller — aber nur mit der Maus.
 * Dieser Dialog ist der Weg, der auch mit der Tastatur funktioniert.
 */
export function MoveWorkflowDialog({
  open,
  onOpenChange,
  workflowName,
  currentFolderId,
  folders,
  mayUseRoot,
  busy = false,
  onSubmit,
}: MoveWorkflowDialogProps) {
  const [target, setTarget] = useState<string | null>(currentFolderId);

  useEffect(() => {
    if (open) setTarget(currentFolderId);
  }, [open, currentFolderId]);

  const waehlbar = sortByPath(folders.filter((folder) => folder.mayEdit));
  const unveraendert = target === currentFolderId;

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={`„${workflowName}“ verschieben`}
      icon="drive_file_move"
      description="Zur Wahl stehen die Ordner, in denen Sie bearbeiten dürfen."
      footer={
        <>
          <Button size="sm" onClick={() => onOpenChange(false)} disabled={busy}>
            Abbrechen
          </Button>
          <Button
            size="sm"
            variant="primary"
            icon="drive_file_move"
            loading={busy}
            disabled={unveraendert}
            onClick={() => onSubmit(target)}
          >
            Verschieben
          </Button>
        </>
      }
    >
      <div className="pb-3">
        <FieldLabel>Zielordner</FieldLabel>
        <select
          autoFocus
          className="bg-surface-2 border-border text-text w-full cursor-pointer rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-[var(--accent)]"
          value={target ?? ''}
          onChange={(event) => setTarget(event.target.value || null)}
        >
          <option value="" disabled={!mayUseRoot}>
            Oberste Ebene{mayUseRoot ? '' : ' (nicht erlaubt)'}
          </option>
          {waehlbar.map((folder) => (
            <option key={folder.id} value={folder.id}>
              {pathLabel(folder.id, folders)}
            </option>
          ))}
        </select>
      </div>
    </Modal>
  );
}
