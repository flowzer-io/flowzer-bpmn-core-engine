import { describe, expect, it } from 'vitest';

import { formatDuration, formatDurationSeconds } from './format';

describe('formatDurationSeconds', () => {
  // Testzweck: Sekunden bleiben Sekunden. Eine Wartezeit von 42 s als „0,7 min“ zu zeigen
  // wäre formal richtig und praktisch unlesbar.
  it('zeigt kurze Dauern in Sekunden', () => {
    expect(formatDurationSeconds(42)).toBe('42 s');
    expect(formatDurationSeconds(0)).toBe('0 s');
    expect(formatDurationSeconds(59)).toBe('59 s');
  });

  // Testzweck: Unter zehn Minuten trägt die Nachkommastelle noch eine Aussage — deutsch
  // mit Komma. Ein glatter Wert bekommt sie nicht, sonst stünde dort „3,0 min“.
  it('zeigt Minuten deutsch, mit Nachkommastelle nur wo sie etwas sagt', () => {
    expect(formatDurationSeconds(210)).toBe('3,5 min');
    expect(formatDurationSeconds(180)).toBe('3 min');
    expect(formatDurationSeconds(2700)).toBe('45 min');
  });

  // Testzweck: Ab einer Stunde gibt die große Einheit den Ton an, die Minute ergänzt nur.
  it('zeigt Stunden mit Minuten', () => {
    expect(formatDurationSeconds(8100)).toBe('2 h 15 min');
    expect(formatDurationSeconds(7200)).toBe('2 h');
  });

  // Testzweck: Lange Vorgänge laufen über Tage; ohne die Tagesstufe stünde dort „76 h“.
  it('zeigt Tage mit Stunden', () => {
    expect(formatDurationSeconds(273_600)).toBe('3 d 4 h');
    expect(formatDurationSeconds(259_200)).toBe('3 d');
  });

  // Testzweck: Die gerundete kleine Einheit darf die große nicht vollmachen — „1 h 60 min“
  // ist keine Dauer, die jemand lesen sollte.
  it('lässt die kleine Einheit nicht überlaufen', () => {
    expect(formatDurationSeconds(7199)).toBe('2 h');
    expect(formatDurationSeconds(86_399)).toBe('1 d');
  });

  // Testzweck: „keine Messung“ ist eine gültige Auskunft der API (cycleTime === null) und
  // muss als Gedankenstrich erscheinen, nicht als „0 s“ oder „NaN“.
  it('zeigt fehlende Werte als Gedankenstrich', () => {
    expect(formatDurationSeconds(null)).toBe('—');
    expect(formatDurationSeconds(undefined)).toBe('—');
    expect(formatDurationSeconds(Number.NaN)).toBe('—');
    expect(formatDurationSeconds(-5)).toBe('—');
  });

  // Testzweck: `formatDuration` rechnet in Millisekunden und wird vom Betriebsbild benutzt.
  // Die neue Funktion darf sie nicht verdrängen — 1500 heißt dort weiterhin 1,5 Sekunden.
  it('lässt die Millisekundenfassung unverändert', () => {
    expect(formatDuration(1500)).toBe('1.5 s');
    expect(formatDurationSeconds(1500)).toBe('25 min');
  });
});
