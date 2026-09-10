import { describe, expect, it } from 'vitest';

import { NAV_ITEMS, visibleNavItems } from './navigation';
import type { FlowzerCapability } from '@/lib/auth/roles';

function only(...capabilities: FlowzerCapability[]) {
  const set = new Set(capabilities);
  return (capability: FlowzerCapability) => set.has(capability);
}

describe('visibleNavItems', () => {
  // Testzweck: Lesen darf jeder Zugelassene. Reine Pflegebereiche fuer Betrieb und
  // Abschnittsbibliothek duerfen ohne ihre jeweilige Rolle nicht im Menue erscheinen.
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

  // Testzweck: Die Abschnittsbibliothek ist ein Modellierungswerkzeug und erscheint
  // ausschließlich mit der serverseitig abgebildeten Modelliererfaehigkeit.
  it('zeigt die Abschnittsbibliothek nur Modellierenden', () => {
    expect(visibleNavItems(only('access', 'modeler')).map((item) => item.key))
      .toContain('form-sections');
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

  // Testzweck: Die eigenen Aufgaben stehen im Menue. Sie waren frueher nur ueber das
  // Dashboard erreichbar — wer wusste, dass es sie gibt, fand sie; sonst nicht.
  it('fuehrt die eigenen Aufgaben im Menue', () => {
    const eintrag = NAV_ITEMS.find((item) => item.key === 'tasks');

    expect(eintrag?.path).toBe('/tasks');
    expect(eintrag?.requires, 'Aufgaben verlangen keine Rolle ausser dem Zugang.').toBeUndefined();
  });

  // Testzweck: Ohne jede Faehigkeit bleibt nichts uebrig, was eine Rolle verlangt.
  it('blendet ohne Faehigkeiten die geschuetzten Bereiche aus', () => {
    const keys = visibleNavItems(() => false).map((item) => item.key);

    expect(keys).not.toContain('operations');
    expect(keys.length).toBe(NAV_ITEMS.filter((item) => !item.requires).length);
  });
});
