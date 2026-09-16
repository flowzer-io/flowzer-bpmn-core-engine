import { describe, expect, it, vi } from 'vitest';
import { FlowzerTaskTypes } from './flowzerTaskTypes';
import type { DiagramElement } from './moddle';
const setMode = vi.hoisted(() => vi.fn());
vi.mock('./bpmnEditor', () => ({ createBpmnEditor: () => ({ setServiceTaskMode: setMode }) }));

describe('KI im BPMN-Aufgabentypmenü', () => {
  // Testzweck: Der normale Typwechseldialog bekommt genau einen KI-Eintrag; Auswahl
  // und Erweiterungsänderung werden als ein gemeinsamer Undo-Befehl ausgeführt.
  it('ergänzt KI neben den vorhandenen Aufgabentypen', () => {
    const apply = vi.fn((_name, context) => context.apply());
    const replacement = { id: 'Task', type: 'bpmn:ServiceTask', businessObject: { $type: 'bpmn:ServiceTask' } };
    const replace = vi.fn(() => replacement);
    const provider = new FlowzerTaskTypes({ registerProvider: vi.fn() },
      { register: vi.fn(), execute: apply }, { replaceElement: replace }, { get: vi.fn() });
    const target: DiagramElement = { id: 'Task', type: 'bpmn:UserTask', businessObject: { $type: 'bpmn:UserTask' } };
    const entries = provider.getPopupMenuEntries(target)({ manual: { label: 'Manual task', action: vi.fn() } });
    expect(entries.manual?.label).toBe('Manual task');
    entries['replace-with-flowzer-ai-task']!.action();
    expect(apply).toHaveBeenCalledTimes(1);
    expect(replace).toHaveBeenCalledWith(target, { type: 'bpmn:ServiceTask' });
    expect(setMode).toHaveBeenCalledWith('Task', 'ai');
  });

  // Testzweck: Ereignisse und Gateways dürfen nicht versehentlich zur KI-Aufgabe
  // konvertiert werden, nur weil sie ebenfalls das BPMN-Typmenü benutzen.
  it('ändert keine Ereignismenüs', () => {
    const provider = new FlowzerTaskTypes({ registerProvider: vi.fn() },
      { register: vi.fn(), execute: vi.fn() }, { replaceElement: vi.fn() }, { get: vi.fn() });
    const target = { id: 'Start', type: 'bpmn:StartEvent', businessObject: { $type: 'bpmn:StartEvent' } };
    expect(provider.getPopupMenuEntries(target)({})).toEqual({});
  });
});
