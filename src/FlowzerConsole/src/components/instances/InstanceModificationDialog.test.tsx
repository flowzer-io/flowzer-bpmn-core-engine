import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError } from '@/lib/api/client';
import type {
  InstanceModificationPreviewDto,
  InstanceModificationRequestDto,
  ProcessInstanceInfoDto,
} from '@/lib/api/types';

import { InstanceModificationDialog } from './InstanceModificationDialog';

const mocks = vi.hoisted(() => ({
  preview: vi.fn(), modify: vi.fn(), mutate: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
}));
vi.mock('@/lib/api/queries', () => ({
  useInstanceModificationPreview: mocks.preview,
  useModifyInstance: mocks.modify,
}));

const INSTANCE_ID = 'a1b2c3d4-0000-0000-0000-000000000000';
const TOKEN_ID = '11111111-0000-0000-0000-000000000000';

const instance: ProcessInstanceInfoDto = {
  canInspect: true,
  instanceId: INSTANCE_ID,
  definitionId: 'definition-1',
  definitionVersion: { major: 1, minor: 0 },
  relatedDefinitionId: 'urlaub',
  relatedDefinitionName: 'Urlaubsantrag',
  messageSubscriptionCount: 0,
  signalSubscriptionCount: 0,
  userTaskSubscriptionCount: 1,
  serviceSubscriptionCount: 0,
  state: 'Waiting',
  tokens: [
    { id: 'master', state: 'Active', parentTokenId: null, variables: { betrag: 120, kunde: 'Meier' } },
    { id: TOKEN_ID, state: 'Active', parentTokenId: 'master', currentFlowNodeId: 'Pruefung' },
  ],
};

const steps: InstanceModificationPreviewDto = {
  instanceId: INSTANCE_ID,
  applicable: true,
  problems: [],
  notices: [],
  steps: [{ tokenId: TOKEN_ID, flowNodeId: 'Pruefung', name: 'Prüfung', type: 'UserTask' }],
  targets: [
    { id: 'Freigabe', name: 'Freigabe', type: 'UserTask' },
    { id: 'Weiche', name: null, type: 'ExclusiveGateway' },
  ],
};

/** Der Trockenlauf der echten Anfrage; er antwortet auf den zweiten Aufruf des Hooks. */
let check: InstanceModificationPreviewDto = { ...steps, notices: [] };

/** Ein Schrittaufruf trägt die leere Anfrage, der Prüfungsaufruf die zusammengebaute. */
function isStepsCall(request: InstanceModificationRequestDto): boolean {
  return !('moves' in request);
}

function renderDialog() {
  return render(<InstanceModificationDialog open onOpenChange={vi.fn()} instance={instance} />);
}

/** Die Anfrage, mit der zuletzt geprüft wurde. */
function lastCheckedRequest(): InstanceModificationRequestDto {
  const last = mocks.preview.mock.calls.filter(([, request]) => !isStepsCall(request)).at(-1);
  if (!last) throw new Error('Der Dialog hat keine Anfrage geprüft.');

  return last[1] as InstanceModificationRequestDto;
}

beforeEach(() => {
  vi.clearAllMocks();
  check = { ...steps, problems: [], notices: [] };
  mocks.preview.mockImplementation((instanceId: string | undefined, request: InstanceModificationRequestDto) => {
    const idle = { data: undefined, isPending: false, isFetching: false, error: null, refetch: mocks.refetch };
    if (isStepsCall(request)) return { ...idle, data: steps };
    if (!instanceId) return idle;
    return { ...idle, data: check };
  });
  mocks.modify.mockReturnValue({
    mutate: mocks.mutate, isPending: false, data: undefined, error: null, reset: mocks.reset,
  });
});

describe('Eingriffsdialog — Auswahl', () => {
  // Testzweck: Was nicht ausgewählt wurde, darf nicht verschoben werden. Stünde hier ein
  // Ziel voreingestellt, verschöbe ein einziger Klick einen Schritt, den niemand gemeint hat.
  it('lässt jeden wartenden Schritt zunächst auf „belassen“', () => {
    renderDialog();

    const select = screen.getByRole('combobox', { name: /Prüfung/ });
    expect(select).toHaveValue('');
    expect(within(select).getByRole('option', { name: 'belassen' })).toBeInTheDocument();
  });

  // Testzweck: Ein Modell hat schnell dreißig Knoten. Ohne Gruppierung nach Elementart
  // findet der Betrieb das gemeinte Ziel nicht wieder.
  it('bietet die Ziele nach Elementart gruppiert an', () => {
    renderDialog();

    const select = screen.getByRole('combobox', { name: /Prüfung/ });
    expect(within(select).getByRole('group', { name: 'Benutzeraufgaben' })).toBeInTheDocument();
    expect(within(select).getByRole('group', { name: 'Gateways' })).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: 'Freigabe' })).toBeInTheDocument();
  });

  // Testzweck: Ohne Verschiebung und ohne Variablenänderung gibt es nichts zu tun. Die API
  // lehnte mit `NothingToDo` ab — der Dialog fragt erst gar nicht.
  it('sperrt den Weg zur Bestätigung, solange nichts geändert ist', async () => {
    const user = userEvent.setup();
    renderDialog();

    expect(screen.getByRole('button', { name: 'Weiter' })).toBeDisabled();

    await user.selectOptions(screen.getByRole('combobox', { name: /Prüfung/ }), 'Freigabe');

    expect(screen.getByRole('button', { name: 'Weiter' })).toBeEnabled();
  });

  // Testzweck: Ein Tippfehler im JSON soll im Dialog stehen und nicht als 400 der API
  // zurückkommen, wenn der Eingriff längst losgeschickt ist.
  it('nennt fehlerhaftes JSON im Feld und sperrt den Weiterweg', async () => {
    const user = userEvent.setup();
    renderDialog();

    const feld = screen.getByRole('textbox', { name: /Variablen setzen/i });
    await user.clear(feld);
    await user.type(feld, '{{ "betrag": }');

    expect(feld).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText(/kein gültiges JSON/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Weiter' })).toBeDisabled();
  });
});

describe('Eingriffsdialog — Trockenlauf', () => {
  // Testzweck: Geprüft werden muss genau die Anfrage, die danach ausgeführt wird —
  // sonst gilt die Auskunft des Trockenlaufs für etwas anderes.
  it('prüft die gewählte Verschiebung', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.selectOptions(screen.getByRole('combobox', { name: /Prüfung/ }), 'Freigabe');
    await user.click(screen.getByRole('button', { name: 'Weiter' }));

    expect(lastCheckedRequest().moves).toEqual([
      { tokenId: TOKEN_ID, targetFlowNodeId: 'Freigabe' },
    ]);
  });

  // Testzweck: Das Feld ist mit allen Variablen vorbelegt. Ginge der ganze Stand mit,
  // überschriebe der Eingriff jede Variable und behauptete Korrekturen, die niemand vornahm.
  it('schickt nur geänderte Variablen mit', async () => {
    const user = userEvent.setup();
    renderDialog();

    const feld = screen.getByRole('textbox', { name: /Variablen setzen/i });
    await user.clear(feld);
    await user.type(feld, '{{ "betrag": 130, "kunde": "Meier" }');
    await user.click(screen.getByRole('button', { name: 'Weiter' }));

    expect(lastCheckedRequest().variables?.set).toEqual({ betrag: 130 });
  });

  // Testzweck: Eine Variable verschwindet nur, wenn sie ausdrücklich angehakt wurde — ein
  // im Feld fehlender Name ist keine Löschung, sondern schlicht nicht genannt.
  it('entfernt nur angehakte Variablen', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole('checkbox', { name: 'kunde' }));
    await user.click(screen.getByRole('button', { name: 'Weiter' }));

    expect(lastCheckedRequest().variables?.remove).toEqual(['kunde']);
    expect(lastCheckedRequest().variables?.set).toBeUndefined();
  });

  // Testzweck: Der Betrieb entscheidet über einen nicht rücknehmbaren Schritt. Hindernisse
  // und Folgen müssen vorher auf Deutsch dastehen, nicht als englische Entwicklermeldung.
  it('zeigt Hindernisse und Hinweise des Trockenlaufs auf Deutsch', async () => {
    check = {
      ...steps,
      applicable: false,
      problems: [{ code: 'TargetNotAllowed', flowNodeId: 'Start', message: 'Target not allowed.' }],
      notices: [{ code: 'UserTaskCancelled', flowNodeId: 'Pruefung', message: 'User task cancelled.' }],
    };
    const user = userEvent.setup();
    renderDialog();

    await user.selectOptions(screen.getByRole('combobox', { name: /Prüfung/ }), 'Freigabe');
    await user.click(screen.getByRole('button', { name: 'Weiter' }));

    expect(screen.getByText(/Start- oder ein angeheftetes Ereignis/)).toBeInTheDocument();
    expect(screen.getByText(/Aufgabe an „Pruefung“ verschwindet samt Kennung/)).toBeInTheDocument();
    // Ein nicht ausführbarer Eingriff bekommt gar keine Zustimmungsfrage.
    expect(screen.queryByRole('checkbox', { name: /nicht rückgängig/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Instanz anpassen' })).toBeDisabled();
  });
});

describe('Eingriffsdialog — Ausführen', () => {
  async function goToConfirmation() {
    const user = userEvent.setup();
    renderDialog();
    await user.selectOptions(screen.getByRole('combobox', { name: /Prüfung/ }), 'Freigabe');
    await user.click(screen.getByRole('button', { name: 'Weiter' }));
    return user;
  }

  // Testzweck: Der Eingriff lässt sich nicht zurücknehmen und Aufgaben beginnen mit neuer
  // Kennung neu. Er startet deshalb erst nach ausdrücklicher Bestätigung.
  it('gibt den Eingriff erst nach der Bestätigung frei', async () => {
    const user = await goToConfirmation();

    const start = screen.getByRole('button', { name: 'Instanz anpassen' });
    expect(start).toBeDisabled();

    await user.click(screen.getByRole('checkbox', { name: /nicht rückgängig/i }));

    expect(start).toBeEnabled();
  });

  // Testzweck: Ausgeführt werden muss genau die geprüfte Anfrage — Verschiebung und
  // Variablen zusammen, sonst bliebe die halbe Betriebsentscheidung liegen.
  it('schickt die geprüfte Anfrage ab', async () => {
    const user = await goToConfirmation();

    await user.click(screen.getByRole('checkbox', { name: /nicht rückgängig/i }));
    await user.click(screen.getByRole('button', { name: 'Instanz anpassen' }));

    expect(mocks.mutate).toHaveBeenCalledWith(
      {
        instanceId: INSTANCE_ID,
        request: {
          moves: [{ tokenId: TOKEN_ID, targetFlowNodeId: 'Freigabe' }],
          variables: { set: undefined, remove: [] },
        },
      },
      expect.anything(),
    );
  });

  // Testzweck: Lehnt die API die Ausführung ab, steht der Grund im Dialog und nicht in einem
  // Toast, der verschwindet — die Instanz ist unverändert, und der Betrieb muss weiterarbeiten.
  it('zeigt die Ablehnung der Ausführung im Dialog', async () => {
    mocks.modify.mockReturnValue({
      mutate: mocks.mutate, isPending: false, data: undefined, reset: mocks.reset,
      error: new ApiError('The modification request could not be processed', {
        status: 422,
        url: `/instance/${INSTANCE_ID}/modification`,
        body: {
          problems: [
            { code: 'TokenNotMovable', tokenId: TOKEN_ID, flowNodeId: 'Pruefung', message: 'Not movable.' },
          ],
        },
      }),
    });
    const user = await goToConfirmation();

    expect(screen.getByText(/Die Instanz ist unverändert/)).toBeInTheDocument();
    expect(screen.getByText(/wartet nicht oder liegt in einem Unterprozess/)).toBeInTheDocument();

    await user.click(screen.getByRole('checkbox', { name: /nicht rückgängig/i }));
    expect(screen.getByRole('button', { name: 'Instanz anpassen' })).toBeDisabled();
  });

  // Testzweck: Wer zurückgeht und die Auswahl ändert, hat etwas anderes bestätigt als
  // geprüft wurde. Die Zustimmung muss deshalb erneut gegeben werden.
  it('verlangt nach einer geänderten Auswahl erneut die Bestätigung', async () => {
    const user = await goToConfirmation();

    await user.click(screen.getByRole('checkbox', { name: /nicht rückgängig/i }));
    await user.click(screen.getByRole('button', { name: 'Zurück' }));
    await user.selectOptions(screen.getByRole('combobox', { name: /Prüfung/ }), 'Weiche');
    await user.click(screen.getByRole('button', { name: 'Weiter' }));

    expect(screen.getByRole('checkbox', { name: /nicht rückgängig/i })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Instanz anpassen' })).toBeDisabled();
  });
});
