import { describe, expect, it } from 'vitest';

import { descendantFormFolderIds, flattenFormFolders } from './formFolders';

describe('formFolders', () => {
  it('ordnet verschachtelte Ordner als lesbaren Pfad', () => {
    // Testzweck: Der Katalog zeigt Hierarchie unabhängig von der Reihenfolge der API-Antwort.
    const result = flattenFormFolders([
      { id: 'child', parentId: 'root', name: 'Abwesenheit' },
      { id: 'root', name: 'Personal' },
    ]);
    expect(result.map((entry) => [entry.path, entry.depth])).toEqual([
      ['Personal', 0],
      ['Personal / Abwesenheit', 1],
    ]);
  });

  it('ermittelt den gesamten Unterbaum für Filter und Verschiebeziele', () => {
    // Testzweck: Eine Ordnerauswahl umfasst auch darin liegende Unterordner.
    expect([...descendantFormFolderIds('a', [
      { id: 'a', name: 'A' },
      { id: 'b', parentId: 'a', name: 'B' },
      { id: 'c', parentId: 'b', name: 'C' },
    ])]).toEqual(['a', 'b', 'c']);
  });
});
