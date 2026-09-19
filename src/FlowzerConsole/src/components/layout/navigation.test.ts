import { describe, expect, it } from 'vitest';

import { activeNavKey, NAV_ITEMS, visibleNavItems } from './navigation';
import type { FlowzerCapability } from '@/lib/auth/roles';

function only(...capabilities: FlowzerCapability[]) {
  const set = new Set(capabilities);
  return (capability: FlowzerCapability) => set.has(capability);
}

describe('visibleNavItems', () => {
  // Testzweck: Lesen darf jeder Zugelassene. Reine Pflegebereiche fuer Betrieb und
  // Verwaltungsbereiche duerfen ohne ihre jeweilige Rolle nicht im Menue erscheinen.
  it('zeigt Zugelassenen nur die allgemein lesbaren Bereiche', () => {
    const keys = visibleNavItems(only('access')).map((item) => item.key);

    expect(keys).toContain('tasks');
    expect(keys).toContain('workflows');
    expect(keys).toContain('instances');
    expect(keys).toContain('forms');
    expect(keys).not.toContain('operations');
    expect(keys).not.toContain('form-sections');
    expect(keys).not.toContain('ai-connections');
  });

  // Testzweck: Nach der Migration ist ein Formular selbst eine Komponente. Ein zweiter
  // Navigationspunkt würde fälschlich zwei getrennte Bibliotheken suggerieren.
  it('zeigt auch Modellierenden keine separate Abschnittsbibliothek', () => {
    expect(visibleNavItems(only('access', 'modeler')).map((item) => item.key))
      .not.toContain('form-sections');
  });

  // Testzweck: Der Betrieb erscheint erst mit der zugehoerigen Rolle. Ein Eintrag, der
  // zu einer Ablehnung fuehrt, gehoert nicht in die Navigation.
  it('zeigt den Betrieb nur mit der Betriebsrolle', () => {
    expect(visibleNavItems(only('access', 'operator')).map((item) => item.key)).toContain('operations');
  });

  // Testzweck: Der Verwaltungsbereich fuer Providerziele und Secret-Referenzen erscheint
  // nur mit der getrennten KI-Managerfaehigkeit, nicht bereits fuer Modellierende oder Nutzer.
  it('zeigt KI-Verbindungen nur deren Verwaltung', () => {
    expect(visibleNavItems(only('access', 'modeler', 'aiConnectionUse')).map((item) => item.key))
      .not.toContain('ai-connections');
    expect(visibleNavItems(only('access', 'aiConnectionManage')).map((item) => item.key))
      .toContain('ai-connections');
  });

  // Testzweck: Eingehende Ausloeser sind Betriebssache — ihre Schluessel und Adressen
  // gehoeren nicht in die Navigation von Personen ohne Betriebsrolle.
  it('zeigt Ausloeser nur mit der Betriebsrolle', () => {
    expect(visibleNavItems(only('access', 'modeler')).map((item) => item.key))
      .not.toContain('triggers');
    expect(visibleNavItems(only('access', 'operator')).map((item) => item.key))
      .toContain('triggers');
  });

  // Testzweck: Die eigenen Aufgaben stehen im Menue. Sie waren frueher nur ueber das
  // Dashboard erreichbar — wer wusste, dass es sie gibt, fand sie; sonst nicht.
  it('fuehrt die eigenen Aufgaben im Menue', () => {
    const eintrag = NAV_ITEMS.find((item) => item.key === 'tasks');

    expect(eintrag?.path).toBe('/tasks');
    expect(eintrag?.requires, 'Aufgaben verlangen keine Rolle ausser dem Zugang.').toBeUndefined();
  });

  // Testzweck: Die Auswertungen lesen dieselbe Laufzeithistorie wie die Diagnose und
  // verlangen deshalb dieselbe Rolle. Ohne sie lehnt die API ab — der Eintrag fuehrte
  // dann nur zu einer Fehlerseite und gehoert deshalb nicht ins Menue.
  it('zeigt die Auswertungen nur mit der Betriebsrolle', () => {
    expect(visibleNavItems(only('access', 'modeler')).map((item) => item.key)).not.toContain('analytics');
    expect(visibleNavItems(only('access', 'operator')).map((item) => item.key)).toContain('analytics');
  });

  // Testzweck: Die Detailseite eines Workflows liegt unter der Uebersicht. Der Menuepunkt
  // muss auch dort aktiv bleiben und darf nicht zum Betrieb daneben springen.
  it('haelt den Menuepunkt auch auf der Auswertungs-Detailseite aktiv', () => {
    expect(activeNavKey('/analytics')).toBe('analytics');
    expect(activeNavKey('/analytics/urlaubsantrag')).toBe('analytics');
    expect(activeNavKey('/operations')).toBe('operations');
  });

  // Testzweck: Ohne jede Faehigkeit bleibt nichts uebrig, was eine Rolle verlangt.
  it('blendet ohne Faehigkeiten die geschuetzten Bereiche aus', () => {
    const keys = visibleNavItems(() => false).map((item) => item.key);

    expect(keys).not.toContain('operations');
    expect(keys.length).toBe(NAV_ITEMS.filter((item) => !item.requires).length);
  });
});
