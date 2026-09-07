import { describe, expect, it } from 'vitest';

import { ancestorIds, buildFolderTree, folderPath, pathLabel, sortByPath, subtreeIds } from './folderTree';
import type { WorkflowFolderDto } from '@/lib/api/types';

function folder(id: string, name: string, parentId: string | null = null): WorkflowFolderDto {
  return {
    id,
    name,
    parentId,
    createdOn: '2026-01-01T00:00:00Z',
    assignments: [],
    inheritedAssignments: [],
    mayEdit: false,
    mayDelegate: false,
    workflowCount: 0,
  };
}

const FOLDERS = [
  folder('finanzen', 'Finanzen'),
  folder('personal', 'Personal'),
  folder('beschaffung', 'Beschaffung', 'finanzen'),
  folder('reisekosten', 'Reisekosten', 'finanzen'),
  folder('rahmen', 'Rahmenverträge', 'beschaffung'),
];

describe('buildFolderTree', () => {
  // Testzweck: Ein zugeklappter Ordner zeigt seine Unterordner nicht — sonst waere das
  // Aufklappen wirkungslos und der Baum bei tiefen Strukturen unbrauchbar lang.
  it('zeigt zugeklappt nur die oberste Ebene', () => {
    const zeilen = buildFolderTree(FOLDERS, new Set());

    expect(zeilen.map((zeile) => zeile.folder.id)).toEqual(['finanzen', 'personal']);
    expect(zeilen[0]?.hasChildren).toBe(true);
    expect(zeilen[1]?.hasChildren).toBe(false);
  });

  // Testzweck: Aufgeklappt steht jeder Ordner unmittelbar vor seinen eigenen Unterordnern,
  // Geschwister alphabetisch. Genau diese Reihenfolge macht die flache Liste lesbar.
  it('ordnet aufgeklappt in Baumreihenfolge und zaehlt die Tiefe', () => {
    const zeilen = buildFolderTree(FOLDERS, new Set(['finanzen', 'beschaffung']));

    expect(zeilen.map((zeile) => `${zeile.depth}:${zeile.folder.id}`)).toEqual([
      '0:finanzen',
      '1:beschaffung',
      '2:rahmen',
      '1:reisekosten',
      '0:personal',
    ]);
  });

  // Testzweck: Ein Ordner, dessen Elternordner fehlt, muss sichtbar bleiben. Haengte er
  // still am unbekannten Elternteil, waere er aus der Oberflaeche nicht mehr erreichbar
  // und liesse sich auch nicht reparieren.
  it('haengt Ordner mit unbekanntem Elternordner an die oberste Ebene', () => {
    const zeilen = buildFolderTree([folder('waise', 'Waise', 'weg')], new Set());

    expect(zeilen).toHaveLength(1);
    expect(zeilen[0]?.depth).toBe(0);
  });

  // Testzweck: Ein durch einen Fehler entstandener Ring darf die Ansicht nicht endlos
  // fuellen. Jeder Ordner erscheint hoechstens einmal.
  it('beendet einen Ring statt endlos zu zeichnen', () => {
    const ring = [folder('a', 'A', 'b'), folder('b', 'B', 'a')];

    const zeilen = buildFolderTree(ring, new Set(['a', 'b']));

    expect(zeilen.length).toBeLessThanOrEqual(ring.length);
  });
});

describe('folderPath', () => {
  // Testzweck: Der Pfad traegt die Brotkrumenleiste; er endet beim gewaehlten Ordner.
  it('liefert den Pfad von oben bis zum Ordner', () => {
    expect(folderPath('rahmen', FOLDERS).map((eintrag) => eintrag.id)).toEqual([
      'finanzen',
      'beschaffung',
      'rahmen',
    ]);
  });

  // Testzweck: Die oberste Ebene ist kein Ordner und hat keinen Pfad.
  it('liefert fuer die oberste Ebene einen leeren Pfad', () => {
    expect(folderPath(null, FOLDERS)).toEqual([]);
  });

  // Testzweck: Beim Wechsel des Ordners muss der Baum den Weg dorthin aufklappen —
  // der gewaehlte Ordner selbst gehoert nicht dazu, sonst klappte er ungefragt auf.
  it('nennt fuer das Aufklappen nur die Vorfahren', () => {
    expect(ancestorIds('rahmen', FOLDERS)).toEqual(['finanzen', 'beschaffung']);
  });
});

describe('sortByPath', () => {
  // Testzweck: Eine Auswahlliste zeigt Pfade. Nach dem blossen Namen sortiert stuende
  // „Finanzen › Beschaffung" vor „Finanzen", und Geschwister laegen weit auseinander.
  it('ordnet nach dem vollstaendigen Pfad statt nach dem Namen', () => {
    expect(sortByPath(FOLDERS).map((folder) => pathLabel(folder.id, FOLDERS))).toEqual([
      'Finanzen',
      'Finanzen › Beschaffung',
      'Finanzen › Beschaffung › Rahmenverträge',
      'Finanzen › Reisekosten',
      'Personal',
    ]);
  });
});

describe('subtreeIds', () => {
  // Testzweck: Beim Verschieben duerfen der Ordner selbst und alles darunter nicht als
  // Ziel angeboten werden — sonst schneidet sich der Ast vom Baum ab.
  it('nennt den Ordner und alles darunter', () => {
    expect(subtreeIds('finanzen', FOLDERS)).toEqual(
      new Set(['finanzen', 'beschaffung', 'reisekosten', 'rahmen']),
    );
  });
});
