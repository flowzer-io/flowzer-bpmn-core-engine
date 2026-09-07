import type { WorkflowFolderDto } from '@/lib/api/types';

/**
 * Die API liefert Ordner als flache Liste. Hier entsteht daraus der Baum, den die Ansicht
 * zeichnet — und zwar als *flache* Liste mit Tiefenangabe, nicht als verschachtelte Struktur:
 * Eine Baumzeile ist eine Zeile, und eine Liste laesst sich ohne Rekursion zeichnen, filtern
 * und mit den Pfeiltasten durchlaufen.
 */
export interface FolderNode {
  folder: WorkflowFolderDto;
  /** 0 fuer die oberste Ebene. */
  depth: number;
  /** Hat der Ordner Unterordner? Entscheidet ueber das Aufklapp-Zeichen. */
  hasChildren: boolean;
}

/** Kennung der obersten Ebene in der Ansicht. Sie ist kein Ordner, aber ein Ziel. */
export const ROOT_FOLDER_ID = null;

/**
 * Sortiert die Ordner in Baumreihenfolge: jeder Ordner unmittelbar gefolgt von seinen
 * Unterordnern, Geschwister alphabetisch.
 *
 * Ordner, deren Elternordner unbekannt ist, haengen sonst nirgends und wuerden verschwinden.
 * Sie kommen deshalb auf die oberste Ebene — sichtbar und damit reparierbar, statt
 * stillschweigend verloren.
 */
export function buildFolderTree(
  folders: readonly WorkflowFolderDto[],
  expanded: ReadonlySet<string>,
): FolderNode[] {
  const known = new Set(folders.map((folder) => folder.id));
  const childrenOf = new Map<string | null, WorkflowFolderDto[]>();

  for (const folder of folders) {
    const parentId = folder.parentId && known.has(folder.parentId) ? folder.parentId : null;
    const siblings = childrenOf.get(parentId);
    if (siblings) siblings.push(folder);
    else childrenOf.set(parentId, [folder]);
  }

  for (const siblings of childrenOf.values()) {
    siblings.sort((a, b) => a.name.localeCompare(b.name, 'de'));
  }

  const nodes: FolderNode[] = [];
  const visited = new Set<string>();

  function append(parentId: string | null, depth: number): void {
    for (const folder of childrenOf.get(parentId) ?? []) {
      // Ein durch einen Fehler entstandener Ring wuerde die Ansicht sonst endlos fuellen.
      if (visited.has(folder.id)) continue;
      visited.add(folder.id);

      const hasChildren = (childrenOf.get(folder.id) ?? []).length > 0;
      nodes.push({ folder, depth, hasChildren });
      if (hasChildren && expanded.has(folder.id)) {
        append(folder.id, depth + 1);
      }
    }
  }

  append(null, 0);
  return nodes;
}

/** Der Pfad von der obersten Ebene bis zu diesem Ordner, einschliesslich. */
export function folderPath(
  folderId: string | null,
  folders: readonly WorkflowFolderDto[],
): WorkflowFolderDto[] {
  const byId = new Map(folders.map((folder) => [folder.id, folder]));
  const path: WorkflowFolderDto[] = [];
  const seen = new Set<string>();

  let current = folderId ? byId.get(folderId) : undefined;
  while (current && !seen.has(current.id)) {
    seen.add(current.id);
    path.unshift(current);
    current = current.parentId ? byId.get(current.parentId) : undefined;
  }

  return path;
}

/** Der Pfad als Text, wie ihn eine Auswahlliste zeigt: `Finanzen › Beschaffung`. */
export function pathLabel(folderId: string, folders: readonly WorkflowFolderDto[]): string {
  return folderPath(folderId, folders)
    .map((folder) => folder.name)
    .join(' › ');
}

/**
 * Sortiert eine flache Ordnerliste so, wie ein Baum sie zeigen würde: nach dem vollständigen
 * Pfad. Die API sortiert nach dem blossen Namen — in einer Auswahlliste stünde dann
 * „Finanzen › Beschaffung" vor „Finanzen", und Geschwister lägen weit auseinander.
 */
export function sortByPath(folders: readonly WorkflowFolderDto[]): WorkflowFolderDto[] {
  return [...folders].sort((a, b) =>
    pathLabel(a.id, folders).localeCompare(pathLabel(b.id, folders), 'de'),
  );
}

/** Die Kennungen aller Vorfahren — gebraucht, um den Pfad zum gewaehlten Ordner aufzuklappen. */
export function ancestorIds(
  folderId: string | null,
  folders: readonly WorkflowFolderDto[],
): string[] {
  return folderPath(folderId, folders)
    .slice(0, -1)
    .map((folder) => folder.id);
}

/**
 * Der Ordner selbst und alles darunter. Ein Ordner darf nicht in seinen eigenen Ast
 * verschoben werden; die Auswahl im Dialog blendet diese Ordner deshalb aus.
 */
export function subtreeIds(folderId: string, folders: readonly WorkflowFolderDto[]): Set<string> {
  const childrenOf = new Map<string, string[]>();
  for (const folder of folders) {
    if (!folder.parentId) continue;
    const siblings = childrenOf.get(folder.parentId);
    if (siblings) siblings.push(folder.id);
    else childrenOf.set(folder.parentId, [folder.id]);
  }

  const result = new Set<string>();
  const pending = [folderId];
  while (pending.length > 0) {
    const current = pending.pop()!;
    if (result.has(current)) continue;
    result.add(current);
    pending.push(...(childrenOf.get(current) ?? []));
  }

  return result;
}
