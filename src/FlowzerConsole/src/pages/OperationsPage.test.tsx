import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { OperationsPage } from './OperationsPage';
import type { OperationsDiagnosticsDto, ProcessInstanceInfoDto } from '@/lib/api/types';

const mocks = vi.hoisted(() => {
  const instance = (instanceId: string, state: string): Record<string, unknown> => ({
    instanceId,
    definitionId: 'definition-1',
    relatedDefinitionId: 'urlaubsantrag',
    relatedDefinitionName: 'Urlaubsantrag',
    messageSubscriptionCount: 0,
    signalSubscriptionCount: 0,
    userTaskSubscriptionCount: 0,
    serviceSubscriptionCount: 0,
    state,
    tokens: [],
    startedAt: '2026-09-17T08:00:00Z',
  });

  return {
    diagnostics: {
      checkedAtUtc: '2026-09-17T09:00:00Z',
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
    instances: [
      instance('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Terminated'),
      instance('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Failed'),
    ],
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
  useInstances: () => ({
    data: mocks.instances as unknown as ProcessInstanceInfoDto[],
    isPending: false,
    error: null,
    refetch: vi.fn(),
  }),
  useTimers: () => ({ data: [], isPending: false, error: null, refetch: vi.fn() }),
}));

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

  // Testzweck: Die Karte „Fehlgeschlagene Instanzen“ zählt über `instanceBucket`; eine
  // abgebrochene Instanz darf dort nicht mehr auftauchen, eine fehlgeschlagene schon.
  it('führt abgebrochene Instanzen nicht unter den fehlgeschlagenen', () => {
    render(<OperationsPage />);

    expect(screen.getByText(/BBBB-BBB/)).toBeInTheDocument();
    expect(screen.queryByText(/AAAA-AAA/)).not.toBeInTheDocument();
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
