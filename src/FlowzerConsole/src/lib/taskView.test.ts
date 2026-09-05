import { describe, expect, it } from 'vitest';

import { normalizePriority, parseDueDate, parseIsoDuration, sortTasks, toTaskView } from './taskView';
import type { ExtendedUserTaskSubscriptionDto } from './api/types';

const NOW = new Date('2026-07-27T09:00:00Z');

function task(overrides: Partial<ExtendedUserTaskSubscriptionDto> = {}): ExtendedUserTaskSubscriptionDto {
  return {
    id: overrides.id ?? 'task-1',
    name: overrides.name ?? 'Freigabe erteilen',
    token: {
      id: 'token-1',
      state: 'Active',
      currentFlowNodeId: 'Activity_1',
      startTime: '2026-07-27T08:00:00',
      ...overrides.token,
    },
    userCandidates: [],
    userGroups: [],
    definitionId: 'def-1',
    processId: 'Process_1',
    definitionMetaName: overrides.definitionMetaName ?? 'Rechnungsfreigabe',
    definitionVersion: { major: 1, minor: 0 },
    ...overrides,
  };
}

// Testzweck: Fälligkeiten kommen als ISO-Zeitpunkt, ISO-Dauer oder FEEL-Ausdruck
// aus dem BPMN. Nur die ersten beiden dürfen zu einem Datum werden — ein
// Ausdruck darf nicht stillschweigend als Termin interpretiert werden.
describe('parseDueDate', () => {
  it('erkennt einen ISO-Zeitpunkt', () => {
    const { date, raw } = parseDueDate('2026-07-28T12:00:00Z', NOW);
    expect(date?.toISOString()).toBe('2026-07-28T12:00:00.000Z');
    expect(raw).toBe('2026-07-28T12:00:00Z');
  });

  it('rechnet eine ISO-Dauer auf den Bezugszeitpunkt', () => {
    const { date } = parseDueDate('PT48H', NOW);
    expect(date?.toISOString()).toBe('2026-07-29T09:00:00.000Z');
  });

  it('lässt FEEL-Ausdrücke unausgewertet', () => {
    const { date, raw } = parseDueDate('=now() + duration("PT2H")', NOW);
    expect(date).toBeNull();
    expect(raw).toBe('=now() + duration("PT2H")');
  });

  it('meldet fehlende Angaben als leer', () => {
    expect(parseDueDate(null, NOW)).toEqual({ date: null, raw: null });
    expect(parseDueDate('   ', NOW)).toEqual({ date: null, raw: null });
  });
});

describe('parseIsoDuration', () => {
  it('rechnet Tage, Stunden, Minuten und Sekunden um', () => {
    expect(parseIsoDuration('PT1H')).toBe(3_600_000);
    expect(parseIsoDuration('P1D')).toBe(86_400_000);
    expect(parseIsoDuration('PT90M')).toBe(5_400_000);
    expect(parseIsoDuration('P1DT2H30M')).toBe(86_400_000 + 2 * 3_600_000 + 30 * 60_000);
  });

  it('weist Nicht-Dauern zurück', () => {
    expect(parseIsoDuration('2026-07-28')).toBeNull();
    expect(parseIsoDuration('P')).toBeNull();
    expect(parseIsoDuration('')).toBeNull();
  });
});

describe('normalizePriority', () => {
  it('erkennt deutsche und englische Stufen', () => {
    expect(normalizePriority('Hoch')).toBe('Hoch');
    expect(normalizePriority('high')).toBe('Hoch');
    expect(normalizePriority('MEDIUM')).toBe('Mittel');
    expect(normalizePriority('low')).toBe('Niedrig');
  });

  it('stuft numerische Prioritäten ein', () => {
    expect(normalizePriority('90')).toBe('Hoch');
    expect(normalizePriority('50')).toBe('Mittel');
    expect(normalizePriority('10')).toBe('Niedrig');
  });

  it('gibt bei unbekannten Werten nichts zurück', () => {
    expect(normalizePriority('dringend-ish')).toBeNull();
    expect(normalizePriority(null)).toBeNull();
  });
});

describe('toTaskView', () => {
  it('stuft eine überschrittene Fälligkeit als überfällig ein', () => {
    const view = toTaskView(task({ dueDate: '2026-07-26T12:00:00Z' }), NOW);
    expect(view.dueBucket).toBe('overdue');
  });

  it('stuft eine Fälligkeit am selben Tag als heute ein', () => {
    const view = toTaskView(task({ dueDate: '2026-07-27T17:00:00Z' }), NOW);
    expect(view.dueBucket).toBe('today');
  });

  it('behandelt Aufgaben ohne Termin gesondert', () => {
    const view = toTaskView(task(), NOW);
    expect(view.dueBucket).toBe('undated');
    expect(view.dueLabel).toBe('ohne Termin');
  });

  it('übernimmt den Form-Key aus der API', () => {
    const view = toTaskView(task({ formKey: 'Rechnungsfreigabe:1.0' }), NOW);
    expect(view.formKey).toBe('Rechnungsfreigabe:1.0');
  });
});

describe('sortTasks', () => {
  it('sortiert überfällige vor heutigen vor terminlosen Aufgaben', () => {
    const views = [
      toTaskView(task({ id: 'c' }), NOW),
      toTaskView(task({ id: 'a', dueDate: '2026-07-26T12:00:00Z' }), NOW),
      toTaskView(task({ id: 'b', dueDate: '2026-07-27T17:00:00Z' }), NOW),
    ];

    expect(sortTasks(views).map((view) => view.id)).toEqual(['a', 'b', 'c']);
  });
});
