import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { ProcessPackagePreviewDto } from '@/lib/api/types';

import { ImportPackageDialog } from './ImportPackageDialog';

const mocks = vi.hoisted(() => ({
  preview: vi.fn(),
  import: vi.fn(),
}));

vi.mock('@/lib/api/queries', () => ({
  usePreviewPackage: () => ({ mutate: mocks.preview, isPending: false }),
  useImportPackage: () => ({ mutate: mocks.import, isPending: false }),
}));

const PREVIEW: ProcessPackagePreviewDto = {
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
    forms: [
      {
        formId: 'f0000000-0000-4000-8000-000000000001',
        name: 'Prüfformular',
        revision: '1.0',
        formKey: 'Prüfformular',
        file: 'forms/f0000000-0000-4000-8000-000000000001.json',
        embedded: false,
      },
    ],
    references: [],
  },
  deployableHere: true,
  formsContractSupported: true,
  problems: [],
  notices: [],
  references: [
    {
      reference: {
        id: 'f1002ef0-0000-4000-8000-000000000001',
        kind: 'directoryGroup',
        elementId: 'Check',
        elementName: 'Rechnung prüfen',
        label: '/team/pruefung',
        requiresMapping: true,
      },
      candidates: [
        { id: 'gruppe-a', label: '/team/pruefung' },
        { id: 'gruppe-b', label: '/team/andere' },
      ],
      suggestedId: 'gruppe-a',
    },
    {
      reference: {
        id: 'f1002ef0-0000-4000-8000-000000000002',
        kind: 'directoryUser',
        elementId: 'Check',
        label: 'Berta Beispiel',
        requiresMapping: true,
      },
      candidates: [{ id: 'person-a', label: 'Carla Ziel' }],
      suggestedId: null,
    },
    {
      reference: {
        id: 'jobType:invoice.check',
        kind: 'jobType',
        elementId: 'Book',
        label: 'invoice.check',
        requiresMapping: false,
      },
      candidates: [],
      suggestedId: null,
    },
  ],
  conflict: null,
};

function paket(): File {
  return new File(['PK'], 'rechnungspruefung.flowzer.zip', { type: 'application/zip' });
}

function oeffnen(overrides: Partial<React.ComponentProps<typeof ImportPackageDialog>> = {}) {
  const onImported = vi.fn();
  const onOpenInModeler = vi.fn();
  render(
    <ImportPackageDialog
      open
      onOpenChange={vi.fn()}
      folders={[]}
      selectedFolderId={null}
      mayUseRoot
      onImported={onImported}
      onOpenInModeler={onOpenInModeler}
      {...overrides}
    />,
  );
  return { onImported, onOpenInModeler };
}

async function dateiWaehlen(preview: ProcessPackagePreviewDto = PREVIEW) {
  mocks.preview.mockImplementation((_file: File, options: { onSuccess: (value: unknown) => void }) =>
    options.onSuccess(preview),
  );
  fireEvent.change(screen.getByLabelText('Paketdatei'), { target: { files: [paket()] } });
  await screen.findByText('Rechnungsprüfung');
}

describe('Prozesspaket importieren', () => {
  beforeEach(() => vi.clearAllMocks());

  // Testzweck: Nach der Dateiwahl zeigt die Vorschau, was im Paket steht und ob diese
  // Installation das Modell veroeffentlichen koennte — vor jeder Entscheidung.
  it('zeigt die Vorschau samt Prüfergebnis', async () => {
    oeffnen();
    await dateiWaehlen();

    expect(screen.getByText('v1.0')).toBeInTheDocument();
    expect(screen.getByText('Veröffentlicht')).toBeInTheDocument();
    expect(screen.getByText(/ließe sich hier veröffentlichen/)).toBeInTheDocument();
    expect(screen.getByText(/1 Formular\(e\)/)).toBeInTheDocument();
  });

  // Testzweck: Ein rein informativer Bezug bekommt keine Auswahl, wird aber genannt — sonst
  // bliebe unbemerkt, dass die Zielinstallation einen Worker braucht.
  it('nennt informative Bezüge, ohne sie zuzuordnen', async () => {
    oeffnen();
    await dateiWaehlen();

    expect(screen.getByText(/Auftragstyp „invoice.check“/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Zuordnung für invoice.check')).not.toBeInTheDocument();
  });

  // Testzweck: Der gleichnamige Eintrag ist vorbelegt, der ohne Vorschlag bleibt leer — und
  // solange er leer ist, bleibt der Import gesperrt.
  it('belegt Vorschläge vor und sperrt den Import bis zur letzten Zuordnung', async () => {
    oeffnen();
    await dateiWaehlen();

    expect((screen.getByLabelText('Zuordnung für /team/pruefung') as HTMLSelectElement).value)
      .toBe('gruppe-a');
    const person = screen.getByLabelText('Zuordnung für Berta Beispiel') as HTMLSelectElement;
    expect(person.value).toBe('');
    expect(screen.getByRole('button', { name: /Importieren/ })).toBeDisabled();
    expect(screen.getByText(/Noch offen: 1 Zuordnung/)).toBeInTheDocument();

    fireEvent.change(person, { target: { value: 'person-a' } });

    expect(screen.getByRole('button', { name: /Importieren/ })).toBeEnabled();
  });

  // Testzweck: Der Import schickt genau die getroffenen Zuordnungen und die Zielentscheidung —
  // und den Bericht zeigt der Dialog danach, statt sich zu schliessen.
  it('importiert mit den gewählten Zuordnungen und zeigt den Bericht', async () => {
    const { onImported, onOpenInModeler } = oeffnen();
    await dateiWaehlen();

    fireEvent.change(screen.getByLabelText('Zuordnung für Berta Beispiel'), {
      target: { value: 'person-a' },
    });
    fireEvent.change(screen.getByLabelText('Name im Katalog'), {
      target: { value: 'Rechnungsprüfung (Kopie)' },
    });

    mocks.import.mockImplementation(
      (_input: unknown, options: { onSuccess: (value: unknown) => void }) =>
        options.onSuccess({
          definitionId: 'definition_neu',
          name: 'Rechnungsprüfung (Kopie)',
          versionId: 'v1',
          version: { major: 1, minor: 0 },
          forms: [
            { formKey: 'Prüfformular', name: 'Prüfformular', revision: '1.0', outcome: 'created' },
          ],
          appliedReferences: [],
          notices: [{ code: 'package.import.not_deployed', message: 'Noch nicht veröffentlicht.' }],
        }),
    );

    fireEvent.click(screen.getByRole('button', { name: /Importieren/ }));

    await waitFor(() => expect(mocks.import).toHaveBeenCalledTimes(1));
    expect(mocks.import.mock.calls[0]?.[0]).toEqual({
      file: expect.any(File),
      mapping: {
        mode: 'new',
        definitionId: 'workflow-package',
        folderId: null,
        name: 'Rechnungsprüfung (Kopie)',
        references: {
          'f1002ef0-0000-4000-8000-000000000001': 'gruppe-a',
          'f1002ef0-0000-4000-8000-000000000002': 'person-a',
        },
      },
    });

    expect(onImported).toHaveBeenCalledTimes(1);
    expect(await screen.findByText(/wurde angelegt/)).toBeInTheDocument();
    expect(screen.getByText(/neu angelegt/)).toBeInTheDocument();
    expect(screen.getByText('Noch nicht veröffentlicht.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Im Modellierer öffnen/ }));
    expect(onOpenInModeler).toHaveBeenCalledWith('definition_neu');
  });

  // Testzweck: Eine belegte Kennung fuehrt zum neuen Stand statt zu einem zweiten Workflow
  // gleichen Namens — und die Anfrage nennt dann den vorhandenen Katalogeintrag.
  it('importiert bei belegter Kennung als neuen Stand', async () => {
    oeffnen();
    await dateiWaehlen({
      ...PREVIEW,
      references: [],
      conflict: {
        definitionId: 'workflow-package',
        name: 'Rechnungsprüfung',
        latestVersion: '2.0',
        mayCreateNewVersion: true,
      },
    });

    expect(screen.getByText(/gibt es hier schon/)).toBeInTheDocument();
    mocks.import.mockImplementation(
      (_input: unknown, options: { onSuccess: (value: unknown) => void }) =>
        options.onSuccess({
          definitionId: 'workflow-package',
          name: 'Rechnungsprüfung',
          versionId: 'v2',
          version: { major: 2, minor: 1 },
          forms: [],
          appliedReferences: [],
          notices: [],
        }),
    );

    fireEvent.click(screen.getByRole('button', { name: /Importieren/ }));

    await waitFor(() => expect(mocks.import).toHaveBeenCalledTimes(1));
    expect(mocks.import.mock.calls[0]?.[0]).toMatchObject({
      mapping: { mode: 'newVersionOf', definitionId: 'workflow-package' },
    });
  });

  // Testzweck: Ein unlesbares Paket meldet sich als Fehler im Dialog und laesst keine
  // Zielentscheidung entstehen, die auf nichts zeigt.
  it('meldet ein unlesbares Paket', async () => {
    oeffnen();
    mocks.preview.mockImplementation(
      (_file: File, options: { onError: (cause: unknown) => void }) =>
        options.onError(new Error('Die Datei ist kein lesbares ZIP-Archiv.')),
    );

    fireEvent.change(screen.getByLabelText('Paketdatei'), { target: { files: [paket()] } });

    expect(await screen.findByRole('alert')).toHaveTextContent('kein lesbares ZIP-Archiv');
    expect(screen.getByRole('button', { name: /Importieren/ })).toBeDisabled();
  });
});
