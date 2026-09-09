import { describe, expect, it, vi } from 'vitest';

import { focusBpmnElement } from './bpmnFocus';

describe('BPMN-Elementfokus', () => {
  // Testzweck: Ein bekannter Befund soll Auswahl und Sichtbarkeit des BPMN-Elements
  // herstellen, ohne dass die UI die konkrete bpmn-js-Implementierung kennen muss.
  it('wählt ein vorhandenes Element an und scrollt dorthin', () => {
    const element = { id: 'Task_1' };
    const select = vi.fn();
    const scrollToElement = vi.fn();

    expect(focusBpmnElement('Task_1', {
      registry: { get: (id) => id === element.id ? element : undefined },
      selection: { select },
      canvas: { scrollToElement },
    })).toBe(true);
    expect(select).toHaveBeenCalledWith(element);
    expect(scrollToElement).toHaveBeenCalledWith(element);
  });

  // Testzweck: Veraltete oder fremde Elementkennungen dürfen den Modeler nicht
  // abbrechen und melden dem Aufrufer stattdessen ein nicht adressierbares Ziel.
  it('liefert bei unbekannter ID sicher false', () => {
    expect(focusBpmnElement('Missing', {
      registry: { get: () => undefined },
      selection: { select: vi.fn() },
      canvas: { scrollToElement: vi.fn() },
    })).toBe(false);
  });
});
