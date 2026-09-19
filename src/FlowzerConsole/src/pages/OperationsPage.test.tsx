import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { OperationsPage } from './OperationsPage';
import type { OperationsDiagnosticsDto, OperationsIncidentDto } from '@/lib/api/types';

const mocks = vi.hoisted(() => {
  const incident = (overrides: Record<string, unknown>): Record<string, unknown> => ({
    kind: 'jobExhausted',
    instanceId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    metaDefinitionId: 'urlaubsantrag',
    definitionId: 'definition-1',
    definitionName: 'Urlaubsantrag',
    flowNodeId: 'ServiceTask_1',
    flowNodeName: 'Zahlung auslösen',
    jobId: 'job-1',
    jobType: 'zahlung',
    message: 'IBAN ungültig',
    since: '2026-09-19T08:00:00Z',
    manualRetries: 0,
    variables: { iban: 'DE00', betrag: 42 },
    ...overrides,
  });

  return {
    incident,
    diagnostics: {
      checkedAtUtc: '2026-09-19T09:00:00Z',
      environment: 'Production',
      storage: {
        storageRootHint: '(default)',
        totalDefinitions: 3,
        activeDefinitions: 2,
        definitionMetadataEntries: 3,
        formMetadataEntries: 1,
        totalInstances: 20,
        activeInstances: 4,
        completedInstances: 10,
        failedInstances: 2,
        cancelledInstances: 4,
        pendingMessages: 0,
        pendingTimers: 0,
        openUserTasks: 0,
        pendingSignals: 0,
        pendingServices: 0,
      },
      incidents: { jobExhausted: 1, instanceFailed: 2 },
      timerScheduler: {
        enabled: true,
        pollIntervalSeconds: 30,
        status: 'Running',
        lastProcessedTimers: 0,
        successfulTickCount: 1,
        failedTickCount: 0,
        totalProcessedTimers: 0,
      },
      // days und lastErrorMessage stellen einzelne Tests um; ohne den weiten Typ leitet
      // TypeScript aus dem Literal `number` ab und kennt lastErrorMessage gar nicht.
      retention: {
        enabled: true,
        days: 90 as number | null,
        pollIntervalMinutes: 60,
        batchSize: 100,
        status: 'Healthy',
        lastRunCompletedAtUtc: '2026-09-17T08:30:00Z',
        lastRunDurationMs: 12,
        lastDeletedInstances: 3,
        successfulRunCount: 5,
        failedRunCount: 0,
        totalDeletedInstances: 17,
        lastErrorMessage: undefined as string | undefined,
      },
      instrumentation: {
        meterName: 'Flowzer.WebApi',
        activitySourceName: 'Flowzer.WebApi',
        notes: 'Diagnose',
      },
      observability: {
        enabled: false,
        consoleExporterEnabled: false,
        otlpExporterEnabled: false,
        prometheusEnabled: false,
        prometheusPath: null as string | null,
        serviceName: 'Flowzer.WebApi',
        serviceVersion: '1.0.0',
      },
    },
    incidents: vi.fn(),
    retryJob: vi.fn(),
  };
});

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/stores/session', () => ({ useCan: () => () => true }));
vi.mock('@/lib/api/queries', () => ({
  useDiagnostics: () => ({
    data: mocks.diagnostics as unknown as OperationsDiagnosticsDto,
    isPending: false,
    error: null,
    refetch: vi.fn(),
  }),
  useHealth: () => ({ data: { status: 'Healthy' }, isPending: false, error: null }),
  useIncidents: mocks.incidents,
  useRetryJob: () => ({ mutate: mocks.retryJob, isPending: false }),
  useTimers: () => ({ data: [], isPending: false, error: null, refetch: vi.fn() }),
}));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.incidents.mockReturnValue({
    data: [] as unknown as OperationsIncidentDto[],
    isPending: false,
    error: null,
    refetch: vi.fn(),
  });
});

describe('Betrieb und Diagnose', () => {
  // Testzweck: Der Betrieb sieht die wirksame Frist und was der letzte Lauf getan hat. Ohne die
  // Frist im Bild müsste man in der Konfiguration nachschlagen, wie lange Vorgangsdaten noch da
  // sind — die eine Zahl, um die es bei der Aufbewahrung geht.
  it('zeigt Frist und letzten Lauf der Aufbewahrung', () => {
    render(<OperationsPage />);

    // Der Name steht zweimal: einmal als Gesundheitskachel, einmal als Kartenkopf.
    expect(screen.getAllByText('Aufbewahrung')).toHaveLength(2);
    expect(screen.getByTitle('90 Tage')).toBeInTheDocument();
    expect(screen.getByTitle('3')).toBeInTheDocument();
    expect(screen.getByTitle('17')).toBeInTheDocument();
  });

  // Testzweck: Ohne gesetzte Frist sagt die Seite ausdrücklich, dass nichts gelöscht wird.
  // Ein leerer Block ließe offen, ob die Aufbewahrung aus ist oder nur noch nichts getan hat.
  it('sagt deutlich, wenn keine Aufbewahrungsfrist gesetzt ist', () => {
    mocks.diagnostics.retention.enabled = false;
    mocks.diagnostics.retention.days = null;
    try {
      render(<OperationsPage />);

      expect(screen.getByText('Keine Aufbewahrungsfrist gesetzt')).toBeInTheDocument();
    } finally {
      mocks.diagnostics.retention.enabled = true;
      mocks.diagnostics.retention.days = 90;
    }
  });

  // Testzweck: Ein gescheiterter Lauf erscheint im Betriebsbild. Eine Aufbewahrung, die still
  // nicht mehr läuft, fällt sonst erst auf, wenn die Datenbank zu groß geworden ist.
  it('zeigt den Fehler des letzten Aufbewahrungslaufs', () => {
    mocks.diagnostics.retention.lastErrorMessage = 'Ablage kurz nicht erreichbar.';
    try {
      render(<OperationsPage />);

      // Zweimal, und das ist gewollt: in der Gesundheitskachel ganz oben und ausgeschrieben
      // am Aufbewahrungsblock. Wer die Seite nur überfliegt, soll die Störung trotzdem sehen.
      expect(screen.getAllByText(/Ablage kurz nicht erreichbar/)).toHaveLength(2);
    } finally {
      mocks.diagnostics.retention.lastErrorMessage = undefined;
    }
  });

  // Testzweck: Der Abbruch ist ein regulärer Ausgang und bekommt in der Verteilung eine eigene,
  // nicht rote Kategorie neben „abgeschlossen“ und „fehlerhaft“ — sonst widerspricht das
  // Betriebsbild der Instanzliste, die denselben Zustand als „Abgebrochen“ führt.
  it('zeigt Abbrüche als eigene Kategorie neben abgeschlossen und fehlerhaft', () => {
    render(<OperationsPage />);

    const cancelled = screen.getByTitle('abgebrochen: 4');
    expect(cancelled).toBeInTheDocument();
    expect(cancelled.style.background).toBe('var(--wait)');
    expect(screen.getByTitle('fehlerhaft: 2')).toBeInTheDocument();
    expect(screen.getByTitle('abgeschlossen: 10')).toBeInTheDocument();
  });

  // Testzweck: Die Zähler kommen aus der Diagnose und stehen schon in der Kachelreihe. Wer die
  // Seite öffnet, soll oben lesen, ob etwas zu tun ist, ohne bis zur Liste zu scrollen.
  it('zeigt die Störungszähler aus der Diagnose', () => {
    render(<OperationsPage />);

    expect(screen.getByText('1 Aufträge liegen · 2 Instanzen gescheitert')).toBeInTheDocument();
    expect(screen.getByText('1 liegen')).toBeInTheDocument();
    expect(screen.getByText('2 gescheitert')).toBeInTheDocument();
  });
});

describe('Störungsliste', () => {
  // Testzweck: Ein liegen gebliebener Auftrag steht mit Workflow, Schritt, Meldung und Alter in
  // der Liste — das ist alles, was der Betrieb braucht, um zu entscheiden.
  it('nennt Workflow, Schritt und Meldung einer Störung', () => {
    mocks.incidents.mockReturnValue({
      data: [mocks.incident({})] as unknown as OperationsIncidentDto[],
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    render(<OperationsPage />);

    expect(screen.getByText('Auftrag liegt')).toBeInTheDocument();
    expect(screen.getByText(/Zahlung auslösen/)).toBeInTheDocument();
    expect(screen.getByText('IBAN ungültig')).toBeInTheDocument();
  });

  // Testzweck: Eine gescheiterte Instanz lässt sich nicht erneut freigeben — Failed bleibt
  // Failed. Die Aktion darf dort deshalb gar nicht erst erscheinen.
  it('bietet die Freigabe nur für liegen gebliebene Aufträge an', () => {
    mocks.incidents.mockReturnValue({
      data: [
        mocks.incident({
          kind: 'instanceFailed',
          jobId: null,
          jobType: null,
          variables: null,
          manualRetries: null,
          message: "Unhandled BPMN error 'BONITAET' at 'ServiceTask_1'.",
        }),
      ] as unknown as OperationsIncidentDto[],
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    render(<OperationsPage />);

    expect(screen.getByText('Instanz gescheitert')).toBeInTheDocument();
    expect(screen.getByText(/Unhandled BPMN error 'BONITAET'/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Erneut freigeben' })).not.toBeInTheDocument();
  });

  // Testzweck: Der Dialog belegt die Korrektur mit den aktuellen Eingaben vor und schickt sie
  // zusammen mit der Anzahl Versuche ab. Ohne die Vorbelegung müsste der Betrieb die Werte
  // abtippen, die die API ohnehin schon geliefert hat.
  it('schickt Versuche und korrigierte Eingaben ab', async () => {
    mocks.incidents.mockReturnValue({
      data: [mocks.incident({})] as unknown as OperationsIncidentDto[],
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    const user = userEvent.setup();
    render(<OperationsPage />);

    await user.click(screen.getByRole('button', { name: 'Erneut freigeben' }));
    const dialog = screen.getByRole('dialog');
    const corrections = within(dialog).getByLabelText('Eingaben korrigieren (JSON)');
    expect(corrections).toHaveValue(JSON.stringify({ iban: 'DE00', betrag: 42 }, null, 2));

    await user.clear(corrections);
    await user.type(corrections, '{{ "iban": "DE99" }');
    const attempts = within(dialog).getByLabelText('Anzahl Versuche');
    await user.clear(attempts);
    await user.type(attempts, '3');
    await user.click(within(dialog).getByRole('button', { name: 'Erneut freigeben' }));

    expect(mocks.retryJob).toHaveBeenCalledWith(
      { jobId: 'job-1', retries: 3, variables: { iban: 'DE99' } },
      expect.anything(),
    );
  });

  // Testzweck: Ungültiges JSON blockiert die Freigabe mit einem Hinweis. Ohne die Prüfung
  // liefe die Korrektur in eine Fehlermeldung der API — nachdem die Absicht schon weg war.
  it('blockiert die Freigabe bei ungültigem JSON und sagt warum', async () => {
    mocks.incidents.mockReturnValue({
      data: [mocks.incident({})] as unknown as OperationsIncidentDto[],
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    const user = userEvent.setup();
    render(<OperationsPage />);

    await user.click(screen.getByRole('button', { name: 'Erneut freigeben' }));
    const dialog = screen.getByRole('dialog');
    const corrections = within(dialog).getByLabelText('Eingaben korrigieren (JSON)');
    await user.clear(corrections);
    await user.type(corrections, '{{ "iban": ');

    expect(within(dialog).getByText(/kein gültiges JSON/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Erneut freigeben' }));
    expect(mocks.retryJob).not.toHaveBeenCalled();
  });

  afterEach(() => {
    mocks.diagnostics.observability.prometheusEnabled = false;
    mocks.diagnostics.observability.prometheusPath = null;
  });

  // Testzweck: Ist der Prometheus-Scrape-Endpunkt offen, nennt die Betriebsseite seinen Pfad. Er
  // antwortet ohne Anmeldung; wer ihn nicht sieht, prüft auch nicht, ob das Gateway diese
  // Metrikquelle versehentlich nach außen durchreicht.
  it('nennt den Pfad des Prometheus-Scrape-Endpunkts, wenn er eingeschaltet ist', () => {
    mocks.diagnostics.observability.prometheusEnabled = true;
    mocks.diagnostics.observability.prometheusPath = '/metrics';

    render(<OperationsPage />);

    expect(screen.getByText(/Prometheus-Scrape:/).closest('li')).toHaveTextContent('/metrics');
  });

  // Testzweck: Ohne eingeschalteten Endpunkt steht dort ausdrücklich „inaktiv“. Eine fehlende
  // Zeile ließe offen, ob der Endpunkt aus ist oder die Diagnose ihn nur nicht meldet.
  it('meldet den Prometheus-Scrape-Endpunkt als inaktiv, wenn er aus ist', () => {
    render(<OperationsPage />);

    expect(screen.getByText(/Prometheus-Scrape:/).closest('li')).toHaveTextContent('inaktiv');
  });
});
