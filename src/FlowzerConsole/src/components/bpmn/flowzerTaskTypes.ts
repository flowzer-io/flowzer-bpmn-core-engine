import { createBpmnEditor } from './bpmnEditor';
import { extension, type DiagramElement } from './moddle';

type Entry = { label: string; className?: string; action: (...args: unknown[]) => unknown };
type Entries = Record<string, Entry>;
interface Injector { get: <T>(name: string) => T }
interface Change { apply: () => void }
interface Commands {
  register: (name: string, handler: { preExecute: (context: Change) => void; execute: () => void; revert: () => void }) => void;
  execute: (name: string, context: Change) => void;
}
interface Replace { replaceElement: (target: DiagramElement, replacement: { type: string }) => DiagramElement }
interface Popup { registerProvider: (id: string, priority: number, provider: FlowzerTaskTypes) => void }

/**
 * KI ist eine Auswahl im normalen BPMN-Aufgabentyp-Menü, kein zweiter Editor-Modus.
 * Der übergeordnete Befehl fasst Typwechsel und Erweiterungen zu genau einem Undo zusammen.
 */
export class FlowzerTaskTypes {
  static readonly $inject = ['popupMenu', 'commandStack', 'bpmnReplace', 'injector'];
  private readonly editor: ReturnType<typeof createBpmnEditor>;

  private readonly commands: Commands;
  private readonly replace: Replace;

  constructor(popup: Popup, commands: Commands, replace: Replace, injector: Injector) {
    this.commands = commands;
    this.replace = replace;
    this.editor = createBpmnEditor(injector);
    commands.register('flowzer.changeTaskType', {
      preExecute: ({ apply }) => apply(),
      execute: () => {},
      revert: () => {},
    });
    // Nach dem Standardprovider ausführen, damit dessen vorhandene Einträge erhalten bleiben.
    popup.registerProvider('bpmn-replace', 500, this);
  }

  getPopupMenuEntries(target: DiagramElement) {
    return (entries: Entries): Entries => {
      if (!/^bpmn:(Task|UserTask|ManualTask|ServiceTask|SendTask|ReceiveTask|ScriptTask|BusinessRuleTask)$/.test(target.type)) return entries;
      const ai = Boolean(extension(target.businessObject, 'flowzer:AiTask'));
      const result = { ...entries };
      if (ai) {
        // Auch beim Wechsel zu Human/Manual dürfen keine versteckten KI-Einstellungen
        // am neuen Typ hängen bleiben. Bestehende Zuordnungen bleiben dagegen erhalten.
        for (const [id, entry] of Object.entries(entries)) result[id] = {
          ...entry,
          action: (...args) => this.change(() => {
            this.editor.setServiceTaskMode(target.id, 'worker');
            entry.action(...args);
          }),
        };
        result['replace-with-service-task'] = {
          label: 'Service Task', className: 'bpmn-icon-service-task',
          action: () => this.change(() => this.editor.setServiceTaskMode(target.id, 'worker')),
        };
      } else {
        result['replace-with-flowzer-ai-task'] = {
          label: 'KI-Task', className: 'bpmn-icon-service-task',
          action: () => this.change(() => {
            const task = target.type === 'bpmn:ServiceTask' ? target
              : this.replace.replaceElement(target, { type: 'bpmn:ServiceTask' });
            this.editor.setServiceTaskMode(task.id, 'ai');
          }),
        };
      }
      return result;
    };
  }

  private change(apply: () => void): void {
    this.commands.execute('flowzer.changeTaskType', { apply });
  }
}

export const FLOWZER_TASK_TYPES_MODULE = {
  __init__: ['flowzerTaskTypes'],
  flowzerTaskTypes: ['type', FlowzerTaskTypes],
};
