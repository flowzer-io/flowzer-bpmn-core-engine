import { render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AnalyticsPage } from './AnalyticsPage';
import type { WorkflowAnalyticsSummaryDto } from '@/lib/api/types';

const mocks = vi.hoisted(() => ({
  can: vi.fn(() => true),
  overview: vi.fn(),
}));

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/stores/session', () => ({ useCan: () => mocks.can }));
vi.mock('@/lib/api/queries', () => ({ useAnalyticsOverview: mocks.overview }));

const urlaubsantrag: WorkflowAnalyticsSummaryDto = {
  metaDefinitionId: 'urlaubsantrag',
  name: 'Urlaubsantrag',
  totalCount: 12,
  runningCount: 3,
  completedCount: 7,
  cancelledCount: 1,
  failedCount: 1,
  cycleTime: { sampleCount: 7, medianSeconds: 8100, p90Seconds: 28_800, meanSeconds: 9000, maxSeconds: 86_400 },
};

const beschaffung: WorkflowAnalyticsSummaryDto = {
  metaDefinitionId: 'beschaffung',
  name: 'Beschaffung',
  totalCount: 2,
  runningCount: 2,
  completedCount: 0,
  cancelledCount: 0,
  failedCount: 0,
  cycleTime: null,
};

function answerWith(workflows: WorkflowAnalyticsSummaryDto[]) {
  mocks.overview.mockReturnValue({
    data: { fromUtc: '2026-08-20T10:00:00Z', toUtc: '2026-09-19T10:00:00Z', workflows },
    isPending: false,
    error: null,
    refetch: vi.fn(),
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.can.mockReturnValue(true);
  answerWith([urlaubsantrag, beschaffung]);
});

describe('Auswertungen — Übersicht', () => {
  // Testzweck: Die Zeile beantwortet ohne weiteren Klick, wie der Workflow im Zeitraum
  // ausgegangen ist. Jede Zahl steht als Text da — ein Balken allein liesse sich nur
  // schaetzen und waere fuer Vorlesewerkzeuge gar nicht lesbar.
  it('nennt je Workflow die Anzahlen nach Ausgang und die Median-Durchlaufzeit', () => {
    render(<AnalyticsPage search={{}} />);

    const zeile = screen.getByRole('button', { name: /Urlaubsantrag/ });

    expect(within(zeile).getByText('12')).toBeInTheDocument();
    expect(within(zeile).getByTitle('laufend: 3')).toBeInTheDocument();
    expect(within(zeile).getByTitle('abgeschlossen: 7')).toBeInTheDocument();
    expect(within(zeile).getByTitle('abgebrochen: 1')).toBeInTheDocument();
    expect(within(zeile).getByTitle('gescheitert: 1')).toBeInTheDocument();
    expect(within(zeile).getByText('2 h 15 min')).toBeInTheDocument();
  });

  // Testzweck: Ein Abbruch ist ein regulaerer Ausgang. Rot stuende fuer eine Stoerung,
  // der jemand nachgehen muesste — die Instanzliste fuehrt ihn ebenfalls nicht so.
  it('zeigt Abbrueche nicht in der Fehlerfarbe', () => {
    render(<AnalyticsPage search={{}} />);

    expect(screen.getByTitle('abgebrochen: 1').style.background).toBe('var(--wait)');
    expect(screen.getByTitle('gescheitert: 1').style.background).toBe('var(--fail)');
  });

  // Testzweck: Ohne abgeschlossene Instanz gibt es keine Durchlaufzeit. Eine gerechnete
  // Null waere eine Behauptung ueber Vorgaenge, die noch laufen.
  it('zeigt fehlende Durchlaufzeiten als Gedankenstrich', () => {
    render(<AnalyticsPage search={{}} />);

    const zeile = screen.getByRole('button', { name: /Beschaffung/ });
    expect(within(zeile).getByText(/—/)).toBeInTheDocument();
    expect(within(zeile).getByText('keine Messung')).toBeInTheDocument();
  });

  // Testzweck: Ein leerer Zeitraum ist kein Fehler. Die Seite sagt, was los ist, statt
  // eine Tabelle mit Ueberschriften und ohne Zeilen zu zeigen.
  it('erklaert einen Zeitraum ohne Instanzen', () => {
    answerWith([]);
    render(<AnalyticsPage search={{}} />);

    expect(screen.getByText('Keine Instanzen im Zeitraum')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Urlaubsantrag/ })).not.toBeInTheDocument();
  });

  // Testzweck: Ohne Betriebsrolle lehnt die API ab. Die Seite stellt die Abfrage deshalb
  // gar nicht erst und zeigt denselben freundlichen Hinweis wie der Betriebsbereich.
  it('zeigt ohne Betriebsrolle den Hinweis statt der Tabelle', () => {
    mocks.can.mockReturnValue(false);
    render(<AnalyticsPage search={{}} />);

    expect(screen.getByText('Auswertungen')).toBeInTheDocument();
    expect(screen.getByText(/Betriebsrolle vorbehalten/)).toBeInTheDocument();
    expect(screen.queryByText('Median-Durchlaufzeit')).not.toBeInTheDocument();
    expect(mocks.overview).toHaveBeenCalledWith(expect.anything(), { enabled: false });
  });
});
