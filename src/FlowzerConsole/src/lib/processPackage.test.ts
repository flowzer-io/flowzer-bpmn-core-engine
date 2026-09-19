import { describe, expect, it } from 'vitest';

import type {
  ProcessPackagePreviewDto,
  ProcessPackageReferenceOptionsDto,
} from '@/lib/api/types';
import {
  buildMapping,
  canImport,
  defaultTarget,
  formOutcomeLabel,
  informationalReferences,
  mappableReferences,
  suggestedChoices,
  unresolvedReferences,
} from './processPackage';

function gruppe(id: string, suggested: string | null): ProcessPackageReferenceOptionsDto {
  return {
    reference: {
      id,
      kind: 'directoryGroup',
      elementId: 'Check',
      elementName: 'Rechnung prüfen',
      label: '/team/pruefung',
      requiresMapping: true,
    },
    candidates: [{ id: 'gruppe-hier', label: '/team/pruefung' }],
    suggestedId: suggested,
  };
}

function hinweis(): ProcessPackageReferenceOptionsDto {
  return {
    reference: {
      id: 'jobType:invoice.check',
      kind: 'jobType',
      elementId: 'Book',
      label: 'invoice.check',
      requiresMapping: false,
    },
    candidates: [],
    suggestedId: null,
  };
}

function vorschau(
  references: ProcessPackageReferenceOptionsDto[],
  conflict: ProcessPackagePreviewDto['conflict'] = null,
): ProcessPackagePreviewDto {
  return {
    manifest: {
      format: 'flowzer.process-package/1',
      formatVersion: 1,
      exportedAt: '2026-09-19T08:00:00Z',
      flowzerVersion: '0.1.0',
      bpmnCapabilitiesContract: 4,
      formsContract: 'flowzer.forms/4',
      workflow: {
        definitionId: 'workflow-package',
        name: 'Rechnungsprüfung',
        version: '1.0',
        processIds: ['Process_Package'],
        source: 'deployed',
      },
      forms: [],
      references: references.map((option) => option.reference),
    },
    deployableHere: true,
    formsContractSupported: true,
    problems: [],
    notices: [],
    references,
    conflict,
  };
}

describe('Prozesspaket-Zuordnung', () => {
  // Testzweck: Zuzuordnende und rein informative Bezuege werden getrennt, damit die Oberflaeche
  // keine Auswahl fuer etwas anbietet, das sie gar nicht zuordnen kann.
  it('trennt zuzuordnende von informativen Bezügen', () => {
    const preview = vorschau([gruppe('ref-1', 'gruppe-hier'), hinweis()]);

    expect(mappableReferences(preview)).toHaveLength(1);
    expect(informationalReferences(preview)[0]?.reference.kind).toBe('jobType');
  });

  // Testzweck: Ein gleichnamiger Eintrag wird vorbelegt; ein Bezug ohne Vorschlag bleibt
  // ausdruecklich leer, statt still auf irgendeinen Eintrag zu zeigen.
  it('belegt nur Bezüge mit Vorschlag vor', () => {
    const preview = vorschau([gruppe('ref-1', 'gruppe-hier'), gruppe('ref-2', null)]);

    expect(suggestedChoices(preview)).toEqual({ 'ref-1': 'gruppe-hier' });
  });

  // Testzweck: Ohne vollstaendige Zuordnung wird nicht importiert — eine halb aufgeloeste
  // Definition waere schlimmer als ein abgelehnter Import.
  it('lässt erst importieren, wenn jeder Bezug zugeordnet ist', () => {
    const preview = vorschau([gruppe('ref-1', null)]);
    const target = defaultTarget(preview, null);

    expect(unresolvedReferences(preview, {})).toHaveLength(1);
    expect(canImport(preview, {}, target)).toBe(false);
    expect(canImport(preview, { 'ref-1': 'gruppe-hier' }, target)).toBe(true);
  });

  // Testzweck: Ein Modell, das hier (noch) nicht veroeffentlicht werden koennte, darf trotzdem
  // als Entwurf ankommen; sonst liesse es sich hier nie zu Ende bringen.
  it('hält ein hier nicht veröffentlichbares Modell nicht auf', () => {
    const preview = { ...vorschau([]), deployableHere: false };

    expect(canImport(preview, {}, defaultTarget(preview, null))).toBe(true);
  });

  // Testzweck: Steht die Kennung hier schon und darf die Person dort bearbeiten, ist der neue
  // Stand die Vorauswahl — sonst entstuende ein zweiter Workflow gleichen Namens.
  it('schlägt bei belegter Kennung den neuen Stand vor', () => {
    const preview = vorschau([], {
      definitionId: 'workflow-package',
      name: 'Rechnungsprüfung',
      latestVersion: '2.0',
      mayCreateNewVersion: true,
    });

    expect(defaultTarget(preview, 'ordner-1').mode).toBe('newVersionOf');
  });

  // Testzweck: Fehlt die Zustaendigkeit fuer den vorhandenen Eintrag, ist der neue Stand keine
  // Option — die Oberflaeche darf keinen Import anbieten, den die API ablehnen muss.
  it('bietet ohne Berechtigung keinen neuen Stand an', () => {
    const preview = vorschau([], {
      definitionId: 'workflow-package',
      name: 'Rechnungsprüfung',
      latestVersion: '2.0',
      mayCreateNewVersion: false,
    });

    expect(defaultTarget(preview, null).mode).toBe('new');
    expect(canImport(preview, {}, { mode: 'newVersionOf', name: 'x', folderId: null })).toBe(false);
  });

  // Testzweck: Bei freier Kennung reist die Kennung des Pakets mit; ist sie belegt, wird sie
  // weggelassen, damit die API eine neue vergibt statt den Import abzulehnen.
  it('schickt die Paketkennung nur mit, solange sie frei ist', () => {
    const frei = vorschau([gruppe('ref-1', 'gruppe-hier')]);
    const belegt = vorschau([], {
      definitionId: 'workflow-package',
      name: 'Rechnungsprüfung',
      latestVersion: '1.0',
      mayCreateNewVersion: true,
    });

    expect(
      buildMapping(frei, { 'ref-1': 'gruppe-hier' }, { mode: 'new', name: ' Neu ', folderId: 'ordner-1' }),
    ).toEqual({
      mode: 'new',
      definitionId: 'workflow-package',
      folderId: 'ordner-1',
      name: 'Neu',
      references: { 'ref-1': 'gruppe-hier' },
    });

    expect(buildMapping(belegt, {}, { mode: 'new', name: 'Kopie', folderId: null }).definitionId).toBeNull();
    expect(buildMapping(belegt, {}, { mode: 'newVersionOf', name: '', folderId: null })).toEqual({
      mode: 'newVersionOf',
      definitionId: 'workflow-package',
      references: {},
    });
  });

  // Testzweck: Der Bericht muss „wiederverwendet“ von „neue Fassung“ unterscheiden; sonst
  // bliebe unklar, ob der Import einen vorhandenen Formularstand angefasst hat.
  it('benennt jedes Formularergebnis eindeutig', () => {
    expect(formOutcomeLabel('created')).toBe('neu angelegt');
    expect(formOutcomeLabel('reused')).toBe('wiederverwendet');
    expect(formOutcomeLabel('revised')).toBe('neue Fassung');
    expect(formOutcomeLabel('embedded')).toBe('im Diagramm enthalten');
  });
});
