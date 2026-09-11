import { useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { InlineSpinner } from '@/components/ui/States';
import { cn } from '@/lib/cn';
import { useCreateFormFolder, useDeleteFormFolder, useUpdateFormFolder } from '@/lib/api/queries';
import type { FormFolderDto } from '@/lib/api/types';
import { descendantFormFolderIds, flattenFormFolders } from '@/lib/forms/formFolders';

interface FormFolderNavigationProps {
  folders: readonly FormFolderDto[];
  selected: string;
  onSelect: (folderId: string) => void;
  mayEdit: boolean;
  pending?: boolean;
}

/** Katalognavigation und kleine Ordnerpflege; Formulare selbst bleiben Eigentum der Seite. */
export function FormFolderNavigation({ folders, selected, onSelect, mayEdit, pending }: FormFolderNavigationProps) {
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [parentId, setParentId] = useState('');
  const createFolder = useCreateFormFolder();
  const updateFolder = useUpdateFormFolder();
  const deleteFolder = useDeleteFormFolder();
  const flat = useMemo(() => flattenFormFolders(folders), [folders]);

  useEffect(() => {
    if (creating) return;
    const folder = folders.find((item) => item.id === selected);
    setName(folder?.name ?? '');
    setParentId(folder?.parentId ?? '');
  }, [creating, folders, selected]);

  function save() {
    const normalized = name.trim();
    if (!normalized) return;
    if (creating) {
      createFolder.mutate({ name: normalized, parentId: parentId || null }, {
        onSuccess: (folder) => {
          setCreating(false);
          onSelect(folder.id);
          toast.success(`Ordner „${folder.name}" angelegt`);
        },
        onError: (error) => toast.error('Ordner konnte nicht angelegt werden', {
          description: error instanceof Error ? error.message : undefined,
        }),
      });
      return;
    }
    if (selected === 'all' || selected === 'root') return;
    updateFolder.mutate({ id: selected, folder: { name: normalized, parentId: parentId || null } }, {
      onSuccess: (folder) => toast.success(`Ordner „${folder.name}" gespeichert`),
      onError: (error) => toast.error('Ordner konnte nicht gespeichert werden', {
        description: error instanceof Error ? error.message : undefined,
      }),
    });
  }

  return (
    <>
      <Card className="p-2">
        <div className="text-muted flex items-center justify-between px-2 pb-1 pt-0.5 text-[11px] font-semibold uppercase tracking-wide">
          <span>Ordner</span>
          {mayEdit && (
            <button
              type="button"
              title="Neuen Formularordner anlegen"
              className="text-muted hover:text-accent"
              onClick={() => {
                setCreating(true);
                setName('');
                setParentId(selected !== 'all' && selected !== 'root' ? selected : '');
              }}
            >
              <Icon name="create_new_folder" size={18} />
            </button>
          )}
        </div>
        <FolderButton icon="folder_open" active={selected === 'all'} onClick={() => { onSelect('all'); setCreating(false); }}>
          Alle Formulare
        </FolderButton>
        <FolderButton icon="folder_off" active={selected === 'root'} onClick={() => { onSelect('root'); setCreating(false); }}>
          Ohne Ordner
        </FolderButton>
        {flat.map(({ folder, depth }) => (
          <FolderButton
            key={folder.id}
            icon="folder"
            active={selected === folder.id}
            depth={depth}
            onClick={() => { onSelect(folder.id); setCreating(false); }}
          >
            {folder.name}
          </FolderButton>
        ))}
        {pending && <InlineSpinner label="Ordner werden geladen …" />}
      </Card>

      {mayEdit && (creating || (selected !== 'all' && selected !== 'root')) && (
        <Card className="space-y-2 p-3">
          <TextInput value={name} onChange={(event) => setName(event.target.value)} placeholder="Ordnername" aria-label="Ordnername" />
          <select
            value={parentId}
            onChange={(event) => setParentId(event.target.value)}
            aria-label="Übergeordneter Ordner"
            className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2 text-[13px]"
          >
            <option value="">Oberste Ebene</option>
            {flat
              .filter(({ folder }) => creating
                || (folder.id !== selected && !descendantFormFolderIds(selected, folders).has(folder.id)))
              .map(({ folder, depth }) => (
                <option key={folder.id} value={folder.id}>{'— '.repeat(depth)}{folder.name}</option>
              ))}
          </select>
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="primary"
              loading={createFolder.isPending || updateFolder.isPending}
              disabled={!name.trim()}
              onClick={save}
            >
              {creating ? 'Anlegen' : 'Speichern'}
            </Button>
            {creating ? (
              <Button size="sm" variant="ghost" onClick={() => setCreating(false)}>Abbrechen</Button>
            ) : (
              <Button
                size="sm"
                variant="danger"
                icon="delete"
                loading={deleteFolder.isPending}
                onClick={() => deleteFolder.mutate(selected, {
                  onSuccess: () => { onSelect('all'); toast.success('Ordner gelöscht'); },
                  onError: (error) => toast.error('Ordner konnte nicht gelöscht werden', {
                    description: error instanceof Error ? error.message : undefined,
                  }),
                })}
              >
                Löschen
              </Button>
            )}
          </div>
        </Card>
      )}
    </>
  );
}

function FolderButton({
  active, icon, depth = 0, onClick, children,
}: {
  active: boolean;
  icon: string;
  depth?: number;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex w-full items-center gap-2 rounded-[var(--r-sm)] py-1.5 pr-2 text-left text-sm',
        active ? 'bg-surface-2 text-accent font-semibold' : 'hover:bg-surface-2',
      )}
      style={{ paddingLeft: `${8 + depth * 16}px` }}
    >
      <Icon name={icon} size={17} />
      <span className="truncate">{children}</span>
    </button>
  );
}
