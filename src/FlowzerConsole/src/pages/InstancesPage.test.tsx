import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type * as InstanceView from '@/lib/instanceView';

import { InstancesPage } from './InstancesPage';

const mocks = vi.hoisted(() => ({
  instances: vi.fn(), migrationPreview: vi.fn(), migrate: vi.fn(),
}));
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/lib/api/queries', () => ({
  useInstances: mocks.instances,
  useInstanceMigrationPreview: mocks.migrationPreview,
  useMigrateInstances: () => ({
    mutate: mocks.migrate, isPending: false, data: undefined, error: null, reset: vi.fn(),
  }),
}));
vi.mock('@/lib/instanceView', async (importOriginal) => ({
  ...(await importOriginal<typeof InstanceView>()),
  useDefinitionModels: () => new Map(),
}));

const instance = {
  instanceId: 'a1b2c3d4-0000-0000-0000-000000000000', definitionId: 'definition-1',
  relatedDefinitionId: 'urlaub', relatedDefinitionName: 'Urlaubsantrag', state: 'Waiting',
  canInspect: true, userTaskSubscriptionCount: 0, messageSubscriptionCount: 0,
  signalSubscriptionCount: 0, serviceSubscriptionCount: 0, tokens: [],
  startedAt: '2026-09-08T10:00:00Z',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.migrationPreview.mockReturnValue({
    data: undefined, isPending: true, error: null, refetch: vi.fn(),
  });
});

describe('Instanzliste', () => {
  // Testzweck: Instanzen desselben Workflows laufen nach einem Deployment auf verschiedenen
  // Versionen. Die Liste nennt die Version je Zeile, damit man sie auseinanderhalten kann.
  it('nennt je Instanz die gebundene Workflow-Version', () => {
    mocks.instances.mockReturnValue({
      data: [
        { ...instance, definitionVersion: { major: 1, minor: 0 } },
        { ...instance, instanceId: 'ffffffff-0000-0000-0000-000000000000', definitionVersion: { major: 2, minor: 0 } },
      ],
      isPending: false,
    });
    render(<InstancesPage />);

    expect(screen.getByText(/v1\.0/)).toBeInTheDocument();
    expect(screen.getByText(/v2\.0/)).toBeInTheDocument();
  });

  // Testzweck: Wer eine Instanz abbricht, sucht sie anschließend bei den fertigen
  // Vorgängen. Unter „Fehler“ stünde sie als Störung, der jemand nachgehen müsste — und
  // die Schrittspalte behauptete mit „Abgeschlossen“ einen fachlichen Erfolg.
  it('führt eine abgebrochene Instanz unter „Fertig“ statt unter „Fehler“', async () => {
    mocks.instances.mockReturnValue({
      data: [{ ...instance, state: 'Terminated', finishedAt: '2026-09-08T11:00:00Z' }],
      isPending: false,
    });
    const user = userEvent.setup();
    render(<InstancesPage />);

    expect(screen.getByRole('tab', { name: 'Fertig 1' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Fehler 0' })).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Fertig 1' }));
    expect(screen.getAllByText('Abgebrochen').length).toBeGreaterThan(0);
    expect(screen.queryByText('Abgeschlossen')).not.toBeInTheDocument();
  });

  // Testzweck: Liegt die gebundene Definition nicht mehr vor, steht dort eine erkennbar
  // unbekannte Version statt einer geratenen oder gar keiner.
  it('kennzeichnet eine unbekannte Version', () => {
    mocks.instances.mockReturnValue({ data: [{ ...instance, definitionVersion: null }], isPending: false });
    render(<InstancesPage />);

    expect(screen.getByText(/v\?/)).toBeInTheDocument();
  });
});

const SECOND = 'ffffffff-0000-0000-0000-000000000000';
const THIRD = '11111111-0000-0000-0000-000000000000';
const FOURTH = '22222222-0000-0000-0000-000000000000';

describe('Filter nach Workflow und Version', () => {
  const urlaubAlt = { ...instance, definitionVersion: { major: 1, minor: 0 } };
  const urlaubNeu = { ...instance, instanceId: SECOND, definitionVersion: { major: 2, minor: 3 } };
  const urlaubVerwaist = { ...instance, instanceId: THIRD, definitionVersion: null };
  const reise = {
    ...instance, instanceId: FOURTH, relatedDefinitionId: 'reise',
    relatedDefinitionName: 'Reisekostenabrechnung', definitionVersion: { major: 1, minor: 0 },
  };

  function showList(data: unknown[]) {
    mocks.instances.mockReturnValue({ data, isPending: false });
    render(<InstancesPage />);
    return userEvent.setup();
  }

  // Testzweck: Im Betrieb laufen Dutzende Workflows nebeneinander. Wer einen davon prüft,
  // muss die fremden Vorgänge ausblenden können, statt sie zu überlesen.
  it('zeigt nach der Workflowwahl nur dessen Instanzen', async () => {
    const user = showList([urlaubAlt, urlaubNeu, reise]);

    await user.selectOptions(screen.getByLabelText('Workflow'), 'urlaub');

    expect(screen.getByText(/A1B2-C3D/)).toBeInTheDocument();
    expect(screen.getByText(/FFFF-FFF/)).toBeInTheDocument();
    expect(screen.queryByText(/2222-222/)).not.toBeInTheDocument();
  });

  // Testzweck: „v1.0“ bedeutet bei jedem Workflow etwas anderes. Die Versionswahl bleibt
  // deshalb erkennbar gesperrt, bis ein Workflow feststeht — und nennt die neueste zuerst.
  it('gibt die Versionen erst mit gewähltem Workflow frei', async () => {
    const user = showList([urlaubAlt, urlaubNeu, reise]);
    const versions = screen.getByLabelText('Version');
    expect(versions).toBeDisabled();

    await user.selectOptions(screen.getByLabelText('Workflow'), 'urlaub');

    expect(versions).toBeEnabled();
    expect(within(versions).getAllByRole('option').map((option) => option.textContent))
      .toEqual(['Alle Versionen', 'v2.3 (1)', 'v1.0 (1)']);
  });

  // Testzweck: Instanzen, deren Definition gelöscht wurde, hängen im Betrieb fest. Sie
  // müssen gezielt auffindbar bleiben, statt in der Versionsliste zu fehlen.
  it('macht Instanzen ohne bekannte Version gezielt wählbar', async () => {
    const user = showList([urlaubAlt, urlaubVerwaist]);

    await user.selectOptions(screen.getByLabelText('Workflow'), 'urlaub');
    await user.selectOptions(screen.getByLabelText('Version'), 'unknown');

    expect(screen.getByText(/1111-111/)).toBeInTheDocument();
    expect(screen.queryByText(/A1B2-C3D/)).not.toBeInTheDocument();
  });

  // Testzweck: Eine Version des vorigen Workflows bliebe sonst als unsichtbare
  // Einschränkung stehen — die Liste wirkte leer, obwohl Instanzen da sind.
  it('verwirft die Versionswahl beim Wechsel des Workflows', async () => {
    const user = showList([urlaubAlt, urlaubNeu, reise]);

    await user.selectOptions(screen.getByLabelText('Workflow'), 'urlaub');
    await user.selectOptions(screen.getByLabelText('Version'), '2.3');
    await user.selectOptions(screen.getByLabelText('Workflow'), 'reise');

    expect(screen.getByLabelText('Version')).toHaveValue('all');
    expect(screen.getByText(/2222-222/)).toBeInTheDocument();
  });

  // Testzweck: Die Zählmarken der Statusfilter sind ein Versprechen über die Liste. Zählten
  // sie den Gesamtbestand, widersprächen sie sichtbar dem, was darunter steht.
  it('zählt die Statusfilter über die übrigen Filter', async () => {
    const user = showList([
      urlaubAlt,
      { ...reise, state: 'Completed', finishedAt: '2026-09-08T11:00:00Z' },
      { ...urlaubNeu, state: 'Completed', finishedAt: '2026-09-08T11:00:00Z' },
    ]);

    await user.selectOptions(screen.getByLabelText('Workflow'), 'urlaub');

    expect(screen.getByRole('tab', { name: 'Alle 2' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Aktiv 1' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Fertig 1' })).toBeInTheDocument();

    await user.type(screen.getByPlaceholderText(/Instanz-ID/), 'A1B2');
    expect(screen.getByRole('tab', { name: 'Alle 1' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Fertig 0' })).toBeInTheDocument();
  });

  // Testzweck: Nach einer Filterwahl ändert sich nur die Liste weiter unten. Ohne Ansage
  // erfährt niemand, der die Seite vorgelesen bekommt, wie viel übrig geblieben ist.
  it('sagt die Trefferzahl an', async () => {
    const user = showList([urlaubAlt, urlaubNeu, reise]);
    expect(screen.getByRole('status')).toHaveTextContent('3 von 3 Instanzen');

    await user.selectOptions(screen.getByLabelText('Workflow'), 'reise');

    expect(screen.getByRole('status')).toHaveTextContent('1 von 3 Instanzen');
  });
});

describe('Auswahl für die Migration', () => {
  // Testzweck: Migrieren darf nur der Betrieb und nur laufende Instanzen. Wo die API
  // ohnehin ablehnen würde, bietet die Liste kein Kästchen an.
  it('bietet Kästchen nur für laufende Instanzen mit Betriebsrecht', () => {
    mocks.instances.mockReturnValue({
      data: [
        { ...instance, definitionVersion: { major: 1, minor: 0 } },
        { ...instance, instanceId: SECOND, canInspect: false },
        { ...instance, instanceId: THIRD, state: 'Completed' },
      ],
      isPending: false,
    });
    render(<InstancesPage />);

    // Die abgeschlossene Instanz fällt schon aus dem Filter „Aktiv“; die fremde bleibt
    // sichtbar, aber ohne Kästchen.
    expect(screen.getAllByRole('checkbox')).toHaveLength(1);
  });

  // Testzweck: Die Vorschau prüft genau eine Quellversion. Eine gemischte Auswahl würde
  // die API mit 400 ablehnen — die Liste sperrt den Einstieg vorher und begründet ihn.
  it('sperrt den Assistenten für gemischte Versionen mit Begründung', async () => {
    mocks.instances.mockReturnValue({
      data: [
        { ...instance, definitionVersion: { major: 1, minor: 0 } },
        { ...instance, instanceId: SECOND, definitionId: 'definition-2', definitionVersion: { major: 2, minor: 0 } },
      ],
      isPending: false,
    });
    const user = userEvent.setup();
    render(<InstancesPage />);

    for (const box of screen.getAllByRole('checkbox')) await user.click(box);

    expect(screen.getByText('2 ausgewählt')).toBeInTheDocument();
    const start = screen.getByRole('button', { name: 'Migrieren …' });
    expect(start).toBeDisabled();
    expect(start).toHaveAttribute('title', expect.stringContaining('Versionen'));
    expect(screen.getByText(/Instanzen verschiedener Versionen/)).toBeInTheDocument();
  });

  // Testzweck: Der Assistent prüft genau die ausgewählten Instanzen — nicht die ganze
  // Liste und nicht die zuletzt angeklickte Zeile.
  it('öffnet den Assistenten mit den ausgewählten Instanzen', async () => {
    mocks.instances.mockReturnValue({
      data: [
        { ...instance, definitionVersion: { major: 1, minor: 0 } },
        { ...instance, instanceId: SECOND, definitionVersion: { major: 1, minor: 0 } },
      ],
      isPending: false,
    });
    const user = userEvent.setup();
    render(<InstancesPage />);

    await user.click(screen.getAllByRole('checkbox')[0]!);
    expect(screen.getByText('1 ausgewählt')).toBeInTheDocument();
    // Ohne geöffneten Dialog wird nichts geprüft — die Auswahl allein ist folgenlos.
    expect(mocks.migrationPreview).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Auswahl aufheben' }));
    expect(screen.queryByText('1 ausgewählt')).not.toBeInTheDocument();

    await user.click(screen.getAllByRole('checkbox')[1]!);
    await user.click(screen.getByRole('button', { name: 'Migrieren …' }));
    // Nur die geprüften Kennungen zählen hier; welche Zuordnung der Assistent mitführt,
    // ist Sache seiner eigenen Tests.
    expect(mocks.migrationPreview.mock.calls.at(-1)?.[0]).toEqual([SECOND]);
  });

  // Testzweck: Endet eine ausgewählte Instanz, während die Liste offen ist, verliert sie ihr
  // Kästchen. Sie darf dann nicht unsichtbar in der Auswahl bleiben und mitmigriert werden.
  it('nimmt eine inzwischen beendete Instanz aus der Auswahl', async () => {
    const running = [
      { ...instance, definitionVersion: { major: 1, minor: 0 } },
      { ...instance, instanceId: SECOND, definitionVersion: { major: 1, minor: 0 } },
    ];
    mocks.instances.mockReturnValue({ data: running, isPending: false });
    const user = userEvent.setup();
    const view = render(<InstancesPage />);

    await user.click(screen.getAllByRole('checkbox')[0]!);
    await user.click(screen.getAllByRole('checkbox')[1]!);
    expect(screen.getByText('2 ausgewählt')).toBeInTheDocument();

    mocks.instances.mockReturnValue({
      data: [running[0], { ...running[1], state: 'Completed' }],
      isPending: false,
    });
    view.rerender(<InstancesPage />);

    expect(screen.getByText('1 ausgewählt')).toBeInTheDocument();
  });
});
