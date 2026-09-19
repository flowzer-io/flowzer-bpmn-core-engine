import { render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AnalyticsDetailPage } from './AnalyticsDetailPage';
import type { FlowNodeAnalyticsDto, WorkflowAnalyticsDetailDto } from '@/lib/api/types';

const mocks = vi.hoisted(() => ({
  can: vi.fn(() => true),
  detail: vi.fn(),
}));

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/stores/session', () => ({ useCan: () => mocks.can }));
vi.mock('@/lib/api/queries', () => ({ useAnalyticsDetail: mocks.detail }));

/**
 * Die Reihenfolge ist Absicht und nicht alphabetisch: Der Server sortiert nach
 * Median-Wartezeit absteigend, „Unterschrift einholen“ ist der Engpass.
 */
const nodes: FlowNodeAnalyticsDto[] = [
  {
    flowNodeId: 'Task_Unterschrift',
    name: 'Unterschrift einholen',
    executionCount: 9,
    waitingTokenCount: 2,
    waitTime: { sampleCount: 9, medianSeconds: 14_400, p90Seconds: 28_800, meanSeconds: 16_200, maxSeconds: 36_000 },
  },
  {
    flowNodeId: 'Task_Antrag',
    name: 'Antrag prüfen',
    executionCount: 12,
    waitingTokenCount: 1,
    waitTime: { sampleCount: 12, medianSeconds: 3600, p90Seconds: 7200, meanSeconds: 4200, maxSeconds: 9000 },
  },
  {
    flowNodeId: 'Task_Bescheid',
    name: 'Bescheid versenden',
    executionCount: 7,
    waitingTokenCount: 0,
    waitTime: { sampleCount: 7, medianSeconds: 210, p90Seconds: 600, meanSeconds: 240, maxSeconds: 900 },
  },
];

const detail: WorkflowAnalyticsDetailDto = {
  fromUtc: '2026-08-20T10:00:00Z',
  toUtc: '2026-09-19T10:00:00Z',
  summary: {
    metaDefinitionId: 'urlaubsantrag',
    name: 'Urlaubsantrag',
    totalCount: 12,
    runningCount: 3,
    completedCount: 7,
    cancelledCount: 1,
    failedCount: 1,
    cycleTime: { sampleCount: 7, medianSeconds: 8100, p90Seconds: 28_800, meanSeconds: 9000, maxSeconds: 273_600 },
  },
  definitionId: null,
  namingDefinitionId: 'a4f1a0aa-0000-0000-0000-000000000000',
  nodes,
  timeline: [
    { day: '2026-09-18', startedCount: 4, finishedCount: 2 },
    { day: '2026-09-19', startedCount: 1, finishedCount: 5 },
  ],
};

function answerWith(data: WorkflowAnalyticsDetailDto) {
  mocks.detail.mockReturnValue({ data, isPending: false, error: null, refetch: vi.fn() });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.can.mockReturnValue(true);
  answerWith(detail);
});

function render_() {
  render(<AnalyticsDetailPage metaDefinitionId="urlaubsantrag" search={{}} />);
}

/**
 * Die Kennzahlenkarte zu ihrer Überschrift. „Median“ steht auch in der Schritttabelle —
 * ohne diese Eingrenzung träfe eine Suche danach zwei Stellen.
 */
function karte(ueberschrift: string): HTMLElement {
  return screen.getByText(ueberschrift).parentElement as HTMLElement;
}

describe('Auswertungen — Detailseite', () => {
  // Testzweck: Die Kennzahlen sind der Grund, warum jemand die Seite öffnet: die Verteilung
  // nach Ausgang und die Durchlaufzeit in allen vier Maßen.
  it('zeigt die Kennzahlen des Workflows', () => {
    render_();

    const instanzen = karte('Instanzen');
    const durchlaufzeit = karte('Durchlaufzeit');

    expect(screen.getByRole('heading', { name: 'Urlaubsantrag' })).toBeInTheDocument();
    expect(within(instanzen).getByText('12')).toBeInTheDocument();
    expect(within(instanzen).getByText('abgebrochen')).toHaveTextContent(/abgebrochen\s*1/);
    expect(within(durchlaufzeit).getByText('Median').nextSibling).toHaveTextContent('2 h 15 min');
    expect(within(durchlaufzeit).getByText('p90').nextSibling).toHaveTextContent('8 h');
    expect(within(durchlaufzeit).getByText('Mittel').nextSibling).toHaveTextContent('2 h 30 min');
    expect(within(durchlaufzeit).getByText('Maximum').nextSibling).toHaveTextContent('3 d 4 h');
  });

  // Testzweck: Die Rangfolge der Engpässe rechnet der Server. Sortierte die Konsole nach,
  // stünde oben ein anderer Schritt als der berechnete — hier wäre das die alphabetische
  // Reihenfolge, die genau nicht der gelieferten entspricht.
  it('übernimmt die Reihenfolge der API, Engpass zuerst', () => {
    render_();

    const namen = screen.getAllByRole('listitem').map((eintrag) => eintrag.textContent ?? '');

    expect(namen[0]).toContain('Unterschrift einholen');
    expect(namen[1]).toContain('Antrag prüfen');
    expect(namen[2]).toContain('Bescheid versenden');
  });

  // Testzweck: Ein Balken lässt sich nur schätzen. Median und p90 stehen deshalb an jedem
  // Schritt zusätzlich als Text — ohne sie wäre das Diagramm mit einem Vorlesewerkzeug leer.
  it('nennt Median und p90 je Schritt als Text', () => {
    render_();

    expect(screen.getByText('Median 4 h · p90 8 h')).toBeInTheDocument();
    expect(screen.getByText('Median 1 h · p90 2 h')).toBeInTheDocument();
    expect(screen.getByText('Median 3,5 min · p90 10 min')).toBeInTheDocument();
  });

  // Testzweck: Die Zeitreihe darf nicht nur aus Balken bestehen; jeder Tag nennt seine
  // beiden Werte im Text, damit sie auch ohne Grafik ankommen.
  it('beschriftet jeden Tag der Zeitreihe mit seinen Werten', () => {
    render_();

    expect(screen.getByLabelText('2026-09-19: 1 gestartet, 5 beendet')).toBeInTheDocument();
    expect(screen.getByText('18.09.')).toBeInTheDocument();
  });

  // Testzweck: Ein Workflow ohne gemessene Schritte (nur laufende Instanzen) darf kein
  // leeres Diagramm zeigen, sondern muss sagen, warum dort nichts steht.
  it('erklärt einen Zeitraum ohne gemessene Schritte', () => {
    answerWith({ ...detail, nodes: [] });
    render_();

    expect(screen.getByText('Keine Schritte gemessen')).toBeInTheDocument();
  });

  // Testzweck: Ohne eine einzige Instanz gibt es nichts auszuwerten — dann steht dort ein
  // Hinweis statt Kennzahlen, die alle null sind.
  it('erklärt einen Zeitraum ohne Instanzen', () => {
    answerWith({ ...detail, summary: { ...detail.summary, totalCount: 0 }, nodes: [], timeline: [] });
    render_();

    expect(screen.getByText('Keine Instanzen im Zeitraum')).toBeInTheDocument();
    expect(screen.queryByText('Wartezeit je Schritt')).not.toBeInTheDocument();
  });

  // Testzweck: Ohne Betriebsrolle lehnt die API ab; die Seite stellt die Abfrage deshalb
  // gar nicht erst und zeigt denselben Hinweis wie die Übersicht.
  it('zeigt ohne Betriebsrolle den Hinweis statt der Auswertung', () => {
    mocks.can.mockReturnValue(false);
    render_();

    expect(screen.getByText(/Betriebsrolle vorbehalten/)).toBeInTheDocument();
    expect(screen.queryByText('Wartezeit je Schritt')).not.toBeInTheDocument();
    expect(mocks.detail).toHaveBeenCalledWith('urlaubsantrag', expect.anything(), null, { enabled: false });
  });
});
