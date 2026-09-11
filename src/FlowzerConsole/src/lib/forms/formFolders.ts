import type { FormFolderDto } from '@/lib/api/types';

export interface FlatFormFolder {
  folder: FormFolderDto;
  depth: number;
  path: string;
}

/** Flacht den Katalogbaum stabil ab; beschädigte/verwaiste Ordner bleiben am Ende sichtbar. */
export function flattenFormFolders(folders: readonly FormFolderDto[]): FlatFormFolder[] {
  const children = new Map<string | null, FormFolderDto[]>();
  const ids = new Set(folders.map((folder) => folder.id));
  for (const folder of folders) {
    const parent = folder.parentId && ids.has(folder.parentId) ? folder.parentId : null;
    const entries = children.get(parent) ?? [];
    entries.push(folder);
    children.set(parent, entries);
  }
  children.forEach((entries) => entries.sort((a, b) => a.name.localeCompare(b.name, 'de')));

  const result: FlatFormFolder[] = [];
  const visited = new Set<string>();
  function visit(parentId: string | null, depth: number, path: string[]) {
    for (const folder of children.get(parentId) ?? []) {
      if (visited.has(folder.id)) continue;
      visited.add(folder.id);
      const nextPath = [...path, folder.name];
      result.push({ folder, depth, path: nextPath.join(' / ') });
      visit(folder.id, depth + 1, nextPath);
    }
  }
  visit(null, 0, []);
  // Auch ein bereits zyklisch gespeicherter Altbestand wird nicht unsichtbar.
  for (const folder of folders) {
    if (!visited.has(folder.id)) result.push({ folder, depth: 0, path: folder.name });
  }
  return result;
}

export function descendantFormFolderIds(folderId: string, folders: readonly FormFolderDto[]): Set<string> {
  const result = new Set<string>([folderId]);
  let changed = true;
  while (changed) {
    changed = false;
    for (const folder of folders) {
      if (folder.parentId && result.has(folder.parentId) && !result.has(folder.id)) {
        result.add(folder.id);
        changed = true;
      }
    }
  }
  return result;
}
