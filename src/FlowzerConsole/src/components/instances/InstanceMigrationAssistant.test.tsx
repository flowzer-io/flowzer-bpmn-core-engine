import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError } from '@/lib/api/client';
import type { InstanceMigrationPreviewDto } from '@/lib/api/types';

import { InstanceMigrationAssistant } from './InstanceMigrationAssistant';

const mocks = vi.hoisted(() => ({
  preview: vi.fn(), migration: vi.fn(), mutate: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
}));
vi.mock('@/lib/api/queries', () => ({
  useInstanceMigrationPreview: mocks.preview,
  useMigrateInstances: mocks.migration,
}));

const FIRST = 'a1b2c3d4-0000-0000-0000-000000000000';
const SECOND = 'ffffffff-0000-0000-0000-000000000000';

const preview: InstanceMigrationPreviewDto = {
  relatedDefinitionId: 'urlaub',
  relatedDefinitionName: 'Urlaubsantrag',
  sourceDefinitionId: 'definition-1',
  sourceVersion: { major: 1, minor: 0 },
  targetDefinitionId: 'definition-2',
  targetVersion: { major: 2, minor: 0 },
  instances: [
    { instanceId: FIRST, migratable: true, problems: [], notices: [] },
    {
      instanceId: SECOND,
      migratable: false,
      problems: [{ code: 'FlowNodeMissing', flowNodeId: 'Review', message: 'Flow node Review is missing.' }],
      notices: [],
    },
  ],
};

function showPreview(data: InstanceMigrationPreviewDto = preview) {
  mocks.preview.mockReturnValue({ data, isPending: false, error: null, refetch: mocks.refetch });
}

function renderAssistant(instanceIds: string[] = [FIRST, SECOND]) {
  return render(
    <InstanceMigrationAssistant open onOpenChange={vi.fn()} instanceIds={instanceIds} />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.preview.mockReturnValue({ data: undefined, isPending: true, error: null, refetch: mocks.refetch });
  mocks.migration.mockReturnValue({
    mutate: mocks.mutate, isPending: false, data: undefined, error: null, reset: mocks.reset,
  });
});

describe('Migrationsassistent — Prüfung', () => {
  // Testzweck: Eine Migration ist nur dann nachvollziehbar, wenn im Dialog steht, von
  // welcher auf welche Version gehoben wird — sonst migriert man ins Blaue.
  it('nennt Workflow, Quell- und Zielversion', () => {
    showPreview();
    renderAssistant();

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Urlaubsantrag');
    expect(dialog).toHaveTextContent('v1.0');
    expect(dialog).toHaveTextContent('v2.0');
  });

  // Testzweck: Nicht deckungsgleiche Instanzen bleiben zurück. Wer sie in der Auswahl
  // hatte, muss den Grund lesen können, statt sie kommentarlos zu vermissen.
  it('führt nicht migrierbare Instanzen mit ihrem Grund auf', () => {
    showPreview();
    renderAssistant();

    expect(screen.getByText('Nicht migrierbar')).toBeInTheDocument();
    expect(screen.getByText(/Der Schritt „Review“ fehlt in der Zielversion\./)).toBeInTheDocument();
  });

  // Testzweck: Ein gespeicherter Entwurf geht verloren, wenn sich die Formularbindung
  // geändert hat. Diese Folge muss vor der Migration sichtbar sein, nicht danach.
  it('warnt vor einem verworfenen Aufgabenentwurf vor der Migration', () => {
    showPreview({
      ...preview,
      instances: [
        {
          instanceId: FIRST, migratable: true, problems: [],
          notices: [{ code: 'UserTaskDraftDiscarded', flowNodeId: 'Antrag', message: 'Draft discarded.' }],
        },
      ],
    });
    renderAssistant([FIRST]);

    expect(screen.getByText(/Entwurf zur Aufgabe „Antrag“ geht verloren/)).toBeInTheDocument();
  });

  // Testzweck: Die Migration lässt sich nicht zurücknehmen. Sie startet deshalb erst,
  // wenn der Betrieb das ausdrücklich bestätigt hat — nicht schon beim Öffnen.
  it('gibt die Migration erst nach der Bestätigung frei', async () => {
    showPreview();
    const user = userEvent.setup();
    renderAssistant();

    const start = screen.getByRole('button', { name: '1 Instanz migrieren' });
    expect(start).toBeDisabled();

    await user.click(screen.getByRole('checkbox', { name: /nicht rückgängig/i }));

    expect(start).toBeEnabled();
    await user.click(start);
    expect(mocks.mutate).toHaveBeenCalledWith(
      { instanceIds: [FIRST], targetDefinitionId: 'definition-2' },
      expect.anything(),
    );
  });

  // Testzweck: Läuft alles schon auf der Zielversion, gibt es nichts zu tun. Der Dialog
  // sagt das, statt eine wirkungslose Schaltfläche anzubieten.
  it('bietet ohne migrierbare Instanz nur das Schließen an', () => {
    showPreview({
      ...preview,
      instances: [
        {
          instanceId: FIRST, migratable: false, notices: [],
          problems: [{ code: 'AlreadyOnTargetVersion', flowNodeId: null, message: 'Already on target.' }],
        },
      ],
    });
    renderAssistant([FIRST]);

    expect(screen.getByText(/läuft bereits auf der Zielversion/)).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /nicht rückgängig/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /migrieren$/ })).not.toBeInTheDocument();
    // Das Kreuz des Dialogs heißt ebenfalls „Schließen“; gemeint ist die Schaltfläche im Fuß.
    expect(screen.getByText('Schließen')).toBeInTheDocument();
  });

  // Testzweck: Schlägt schon die Prüfung fehl, bleibt der Dialog bedienbar und bietet
  // einen neuen Anlauf an — die Vorschau ist folgenlos und darf wiederholt werden.
  it('bietet nach einer fehlgeschlagenen Prüfung einen neuen Anlauf', async () => {
    mocks.preview.mockReturnValue({
      data: undefined, isPending: false, refetch: mocks.refetch,
      error: new ApiError('Keine Version deployt.', { status: 422, url: '/instance/migration/preview' }),
    });
    const user = userEvent.setup();
    renderAssistant();

    expect(screen.getByText('Keine Version deployt.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Erneut prüfen' }));
    expect(mocks.refetch).toHaveBeenCalled();
  });
});

describe('Migrationsassistent — Ergebnis', () => {
  // Testzweck: Die API migriert je Instanz einzeln; ein Teilerfolg ist normal. Das
  // Ergebnis muss deshalb je Instanz stehen und nicht als pauschales „erledigt“.
  it('zeigt je Instanz, ob sie migriert wurde', () => {
    showPreview();
    mocks.migration.mockReturnValue({
      mutate: mocks.mutate, isPending: false, error: null, reset: mocks.reset,
      data: {
        targetDefinitionId: 'definition-2', targetVersion: { major: 2, minor: 0 },
        instances: [
          { instanceId: FIRST, migrated: true, problems: [] },
          {
            instanceId: SECOND, migrated: false,
            problems: [{ code: 'TokenNotAtRest', flowNodeId: 'Review', message: 'Token busy.' }],
          },
        ],
      },
    });
    renderAssistant();

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Migriert')).toBeInTheDocument();
    expect(within(dialog).getByText('Nicht migriert')).toBeInTheDocument();
    expect(within(dialog).getByText(/arbeitet gerade an „Review“/)).toBeInTheDocument();
    expect(within(dialog).queryByRole('checkbox', { name: /nicht rückgängig/i })).not.toBeInTheDocument();
  });

  // Testzweck: Zwischen Prüfung und Migration kann jemand anders deployen. Dann ist die
  // geprüfte Zielversion falsch — der Assistent verlangt eine neue Prüfung.
  it('verlangt nach einem zwischenzeitlichen Deployment eine neue Prüfung', async () => {
    showPreview();
    mocks.migration.mockReturnValue({
      mutate: mocks.mutate, isPending: false, data: undefined, reset: mocks.reset,
      error: new ApiError('Target version changed.', { status: 409, url: '/instance/migration' }),
    });
    const user = userEvent.setup();
    renderAssistant();

    expect(screen.getByText('Inzwischen wurde eine andere Version deployt.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Erneut prüfen' }));
    expect(mocks.reset).toHaveBeenCalled();
    expect(mocks.refetch).toHaveBeenCalled();
  });
});
