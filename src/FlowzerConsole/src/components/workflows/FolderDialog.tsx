import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import type { WorkflowFolderDto, WorkflowFolderRequestDto } from '@/lib/api/types';
import { pathLabel, sortByPath, subtreeIds } from '@/lib/folderTree';

/** Muss zu <c>FolderController.MaxFolderNameLength</c> passen. */
const MAX_NAME_LENGTH = 120;

const FORM_ID = 'ordner-formular';

interface FolderDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Gesetzt heißt bearbeiten, leer heißt neu anlegen. */
  folder?: WorkflowFolderDto | null;
  /** Beim Anlegen der vorbelegte Elternordner. */
  defaultParentId?: string | null;
  folders: readonly WorkflowFolderDto[];
  busy?: boolean;
  onSubmit: (request: WorkflowFolderRequestDto) => void;
}

/**
 * Ordner anlegen und ändern. Der übergeordnete Ordner ist Teil desselben Dialogs —
 * Umbenennen und Verschieben sind aus Sicht der API dieselbe Änderung, und zwei getrennte
 * Dialoge für eine Änderung wären eine Erfindung der Oberfläche.
 */
export function FolderDialog({
  open,
  onOpenChange,
  folder,
  defaultParentId = null,
  folders,
  busy = false,
  onSubmit,
}: FolderDialogProps) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [parentId, setParentId] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setName(folder?.name ?? '');
    setDescription(folder?.description ?? '');
    setParentId(folder ? (folder.parentId ?? null) : defaultParentId);
  }, [open, folder, defaultParentId]);

  const trimmed = name.trim();
  const tooLong = trimmed.length > MAX_NAME_LENGTH;
  const valid = trimmed.length > 0 && !tooLong;

  // Ein Ordner kann nicht in sich selbst oder in einen seiner Unterordner wandern —
  // der Ast waere danach vom Baum abgeschnitten. Die API lehnt das ab; hier steht es
  // gar nicht erst zur Wahl.
  const gesperrt = folder ? subtreeIds(folder.id, folders) : new Set<string>();
  const waehlbar = sortByPath(folders.filter((candidate) => !gesperrt.has(candidate.id) && candidate.mayDelegate));

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={folder ? `„${folder.name}“ ändern` : 'Neuer Ordner'}
      icon={folder ? 'folder' : 'create_new_folder'}
      description={
        folder
          ? 'Name, Beschreibung und Ablageort. Die Zuständigkeiten stehen in einem eigenen Dialog.'
          : 'Ordner gliedern den Katalog und tragen die Zuständigkeit für die Workflows darin.'
      }
      footer={
        <>
          <Button size="sm" onClick={() => onOpenChange(false)} disabled={busy}>
            Abbrechen
          </Button>
          <Button
            type="submit"
            form={FORM_ID}
            size="sm"
            variant="primary"
            icon={folder ? 'check' : 'add'}
            loading={busy}
            disabled={!valid}
          >
            {folder ? 'Übernehmen' : 'Anlegen'}
          </Button>
        </>
      }
    >
      <form
        id={FORM_ID}
        className="flex flex-col gap-4 pb-3"
        onSubmit={(event) => {
          event.preventDefault();
          if (!valid || busy) return;
          onSubmit({
            name: trimmed,
            parentId,
            description: description.trim() || null,
          });
        }}
      >
        <div>
          <FieldLabel>Name</FieldLabel>
          <TextInput
            autoFocus
            value={name}
            maxLength={MAX_NAME_LENGTH + 1}
            placeholder="z. B. Beschaffung"
            onChange={(event) => setName(event.target.value)}
          />
          {tooLong && <div className="text-fail mt-1.5 text-[12.5px]">Höchstens {MAX_NAME_LENGTH} Zeichen.</div>}
        </div>

        <div>
          <FieldLabel>Beschreibung (optional)</FieldLabel>
          <TextInput
            value={description}
            placeholder="Wofür ist dieser Ordner da?"
            onChange={(event) => setDescription(event.target.value)}
          />
        </div>

        <div>
          <FieldLabel>Übergeordneter Ordner</FieldLabel>
          <select
            className="bg-surface-2 border-border text-text w-full cursor-pointer rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-[var(--accent)]"
            value={parentId ?? ''}
            onChange={(event) => setParentId(event.target.value || null)}
          >
            <option value="">Oberste Ebene</option>
            {waehlbar.map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {pathLabel(candidate.id, folders)}
              </option>
            ))}
          </select>
          <p className="text-faint mt-1.5 text-[12px] leading-normal">
            Zur Wahl stehen nur Ordner, für die Sie die Fachverantwortung tragen. Die oberste Ebene
            ist der Rolle fürs Modellieren vorbehalten.
          </p>
        </div>
      </form>
    </Modal>
  );
}
