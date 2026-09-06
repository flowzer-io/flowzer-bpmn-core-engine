import { describe, expect, it } from 'vitest';

import { NAV_ITEMS, visibleNavItems } from './navigation';
import type { FlowzerCapability } from '@/lib/auth/roles';

function only(...capabilities: FlowzerCapability[]) {
  const set = new Set(capabilities);
  return (capability: FlowzerCapability) => set.has(capability);
}

describe('visibleNavItems', () => {
  // Testzweck: Lesen darf jeder Zugelassene. Frueher hing die gesamte Konsole an den
  // Rollen fuers Modellieren oder den Betrieb; wer nur Aufgaben hatte, sah nichts.
  it('zeigt Zugelassenen alle Bereiche ausser dem Betrieb', () => {
    const keys = visibleNavItems(only('access')).map((item) => item.key);

    expect(keys).toContain('tasks');
    expect(keys).toContain('workflows');
    expect(keys).toContain('instances');
    expect(keys).toContain('forms');
    expect(keys).not.toContain('operations');
  });

  // Testzweck: Der Betrieb erscheint erst mit der zugehoerigen Rolle. Ein Eintrag, der
  // zu einer Ablehnung fuehrt, gehoert nicht in die Navigation.
  it('zeigt den Betrieb nur mit der Betriebsrolle', () => {
    expect(visibleNavItems(only('access', 'operator')).map((item) => item.key)).toContain('operations');
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
