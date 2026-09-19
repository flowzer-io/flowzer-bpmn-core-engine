import { describe, expect, it } from 'vitest';

import type { FlowNodeAnalyticsDto, WorkflowAnalyticsSummaryDto } from '@/lib/api/types';

import {
  barWidthPercent,
  dayLabel,
  flowNodeLabel,
  outcomeSlices,
  rangeSelection,
  resolveRange,
  timelineScaleMax,
  waitTimeScaleMax,
} from './analytics';

const NOW = new Date('2026-09-19T10:00:00.000Z');

describe('resolveRange', () => {
  // Testzweck: Ohne Angabe in der Adresse gelten die letzten 30 Tage — derselbe Zeitraum,
  // den auch der Server annimmt. Wären beide Voreinstellungen verschieden, zeigte ein
  // Neuladen andere Zahlen als der erste Aufruf.
  it('nimmt ohne Suchparameter die letzten 30 Tage', () => {
    expect(resolveRange({}, NOW)).toEqual({
      fromIso: '2026-08-20T10:00:00.000Z',
      toIso: '2026-09-19T10:00:00.000Z',
      days: 30,
    });
  });

  // Testzweck: Die Zeitraumknöpfe schreiben `days` in die Adresse; daraus muss wieder
  // genau diese Spanne werden.
  it('übernimmt eine gewählte Spanne aus der Adresse', () => {
    const range = resolveRange({ days: 7 }, NOW);

    expect(range.fromIso).toBe('2026-09-12T10:00:00.000Z');
    expect(range.days).toBe(7);
    expect(rangeSelection(range)).toBe('7');
  });

  // Testzweck: Der eigene Zeitraum schließt den genannten Endtag ein. Ohne das fehlte der
  // letzte gewählte Tag vollständig in der Auswertung.
  it('nimmt einen eigenen Zeitraum inklusive des Endtags', () => {
    const range = resolveRange({ from: '2026-09-01', to: '2026-09-10' }, NOW);

    expect(range).toEqual({
      fromIso: '2026-09-01T00:00:00.000Z',
      toIso: '2026-09-10T23:59:59.999Z',
      days: 'custom',
    });
    expect(rangeSelection(range)).toBe('custom');
  });

  // Testzweck: Eine halbe Angabe ist kein Zeitraum. Die fehlende Grenze zu erfinden wäre
  // eine Auswertung, die niemand angefordert hat — es gilt wieder die Voreinstellung.
  it('fällt bei nur einer Grenze auf die Voreinstellung zurück', () => {
    expect(resolveRange({ from: '2026-09-01' }, NOW).days).toBe(30);
    expect(resolveRange({ to: '2026-09-10' }, NOW).days).toBe(30);
  });

  // Testzweck: Eine von Hand veränderte Adresse darf die Seite nicht in die 422 der API
  // laufen lassen; unsinnige Spannen werden begrenzt statt weitergereicht.
  it('begrenzt unsinnige Spannen', () => {
    expect(resolveRange({ days: 0 }, NOW).days).toBe(30);
    expect(resolveRange({ days: Number.NaN }, NOW).days).toBe(30);
    expect(resolveRange({ days: 99_999 }, NOW).days).toBe(366);
  });

  // Testzweck: Ein unlesbares Datum in der Adresse darf keinen „Invalid Date“-Zeitstempel
  // an die API schicken.
  it('ignoriert ein unlesbares Datum', () => {
    expect(resolveRange({ from: 'gestern', to: 'heute' }, NOW).days).toBe(30);
  });
});

describe('barWidthPercent', () => {
  // Testzweck: Der Normalfall — der Wert im Verhältnis zum Maßstab.
  it('rechnet den Anteil in Prozent', () => {
    expect(barWidthPercent(5, 10)).toBe(50);
    expect(barWidthPercent(10, 10)).toBe(100);
  });

  // Testzweck: Ohne Maßstab (kein einziger gemessener Wert) darf keine Division durch
  // null entstehen; der Balken bleibt leer.
  it('bleibt ohne Maßstab bei 0', () => {
    expect(barWidthPercent(5, 0)).toBe(0);
    expect(barWidthPercent(0, 0)).toBe(0);
    expect(barWidthPercent(5, -3)).toBe(0);
  });

  // Testzweck: Negative oder nicht endliche Werte dürfen keinen Balken erzeugen, der aus
  // seiner Spur läuft.
  it('bleibt zwischen 0 und 100', () => {
    expect(barWidthPercent(-5, 10)).toBe(0);
    expect(barWidthPercent(50, 10)).toBe(100);
    expect(barWidthPercent(Number.NaN, 10)).toBe(0);
  });
});

const summary: WorkflowAnalyticsSummaryDto = {
  metaDefinitionId: 'urlaubsantrag',
  name: 'Urlaubsantrag',
  totalCount: 12,
  runningCount: 3,
  completedCount: 7,
  cancelledCount: 1,
  failedCount: 1,
  cycleTime: null,
};

describe('outcomeSlices', () => {
  // Testzweck: Der Abbruch ist ein regulärer Ausgang und bekommt deshalb den Ton „wait“ —
  // rot stünde für eine Störung, der jemand nachgehen müsste.
  it('färbt den Abbruch nicht wie einen Fehler', () => {
    const slices = outcomeSlices(summary);

    expect(slices.map((slice) => slice.key)).toEqual(['running', 'completed', 'cancelled', 'failed']);
    expect(slices.find((slice) => slice.key === 'cancelled')?.tone).toBe('wait');
    expect(slices.find((slice) => slice.key === 'failed')?.tone).toBe('fail');
  });
});

const node = (flowNodeId: string, p90: number | null): FlowNodeAnalyticsDto => ({
  flowNodeId,
  name: null,
  executionCount: 4,
  waitingTokenCount: 0,
  waitTime: p90 === null ? null : { sampleCount: 4, medianSeconds: p90 / 2, p90Seconds: p90, meanSeconds: p90 / 2, maxSeconds: p90 },
});

describe('Maßstäbe der Diagramme', () => {
  // Testzweck: Der höchste p90 des Datensatzes ist die 100-%-Marke. Ein Schritt ohne
  // Messung darf den Maßstab nicht auf null drücken.
  it('nimmt den höchsten p90 als Maßstab', () => {
    expect(waitTimeScaleMax([node('a', 100), node('b', null), node('c', 400)])).toBe(400);
    expect(waitTimeScaleMax([])).toBe(0);
  });

  // Testzweck: Gestartet und beendet teilen sich eine Skala, sonst wären die beiden
  // Balken eines Tages nicht vergleichbar.
  it('nimmt den höchsten Tageswert beider Reihen', () => {
    expect(
      timelineScaleMax([
        { day: '2026-09-18', startedCount: 3, finishedCount: 9 },
        { day: '2026-09-19', startedCount: 5, finishedCount: 1 },
      ]),
    ).toBe(9);
  });
});

describe('Beschriftungen', () => {
  // Testzweck: Ein Schritt ohne Namen bleibt über seine technische Kennung auffindbar,
  // statt als leere Zeile zu erscheinen.
  it('fällt ohne Namen auf die Knotenkennung zurück', () => {
    expect(flowNodeLabel(node('Task_1', 100))).toBe('Task_1');
    expect(flowNodeLabel({ ...node('Task_1', 100), name: 'Antrag prüfen' })).toBe('Antrag prüfen');
    expect(flowNodeLabel({ ...node('Task_1', 100), name: '  ' })).toBe('Task_1');
  });

  // Testzweck: Der Tag der Zeitreihe ist ein reines Datum. Über einen Zeitstempel geparst
  // könnte er westlich von Greenwich als Vortag erscheinen.
  it('beschriftet den Tag ohne Zeitzone', () => {
    expect(dayLabel('2026-09-19')).toBe('19.09.');
    expect(dayLabel('kaputt')).toBe('kaputt');
  });
});
