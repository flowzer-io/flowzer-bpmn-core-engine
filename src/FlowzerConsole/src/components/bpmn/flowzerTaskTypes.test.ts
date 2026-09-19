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

  // Testzweck: Der Business-Rule-Task steht genau einmal im Menü — mit der eigenen
  // Beschriftung statt des englischen Standardeintrags — und wechselt den Elementtyp
  // im selben übergeordneten Undo-Befehl.
  it('bietet den Business-Rule-Task mit eigener Beschriftung an', () => {
    const apply = vi.fn((_name, context) => context.apply());
    const replace = vi.fn();
    const provider = new FlowzerTaskTypes({ registerProvider: vi.fn() },
      { register: vi.fn(), execute: apply }, { replaceElement: replace }, { get: vi.fn() });
    const target: DiagramElement = { id: 'Task', type: 'bpmn:Task', businessObject: { $type: 'bpmn:Task' } };

    const entries = provider.getPopupMenuEntries(target)({
      'replace-with-rule-task': { label: 'Business rule task', action: vi.fn() },
    });

    expect(entries['replace-with-rule-task']).toBeUndefined();
    expect(entries['replace-with-business-rule-task']?.label).toBe('Business-Rule-Task');
    expect(entries['replace-with-business-rule-task']?.className).toBe('bpmn-icon-business-rule-task');

    entries['replace-with-business-rule-task']!.action();
    expect(apply).toHaveBeenCalledTimes(1);
    expect(replace).toHaveBeenCalledWith(target, { type: 'bpmn:BusinessRuleTask' });
  });

  // Testzweck: Wer von einer KI-Aufgabe zum Business-Rule-Task wechselt, darf keine
  // unsichtbare KI-Konfiguration am neuen Schritt behalten.
  it('raeumt die KI-Konfiguration beim Wechsel zum Business-Rule-Task ab', () => {
    const apply = vi.fn((_name, context) => context.apply());
    const provider = new FlowzerTaskTypes({ registerProvider: vi.fn() },
      { register: vi.fn(), execute: apply }, { replaceElement: vi.fn() }, { get: vi.fn() });
    const target: DiagramElement = {
      id: 'Task',
      type: 'bpmn:ServiceTask',
      businessObject: {
        $type: 'bpmn:ServiceTask',
        extensionElements: { $type: 'bpmn:ExtensionElements', values: [{ $type: 'flowzer:AiTask' }] },
      },
    };

    const entries = provider.getPopupMenuEntries(target)({});
    entries['replace-with-business-rule-task']!.action();

    expect(setMode).toHaveBeenCalledWith('Task', 'worker');
  });

  // Testzweck: An einem Business-Rule-Task fehlt der Eintrag, der auf denselben Typ
  // wechseln wuerde — er waere ein Menuepunkt ohne Wirkung.
  it('bietet am Business-Rule-Task keinen Wechsel auf sich selbst an', () => {
    const provider = new FlowzerTaskTypes({ registerProvider: vi.fn() },
      { register: vi.fn(), execute: vi.fn() }, { replaceElement: vi.fn() }, { get: vi.fn() });
    const target: DiagramElement = {
      id: 'Rule', type: 'bpmn:BusinessRuleTask', businessObject: { $type: 'bpmn:BusinessRuleTask' },
    };

    expect(provider.getPopupMenuEntries(target)({})['replace-with-business-rule-task']).toBeUndefined();
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
