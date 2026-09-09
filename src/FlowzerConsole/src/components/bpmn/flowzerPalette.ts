import { AI_WORKER_TYPE, DEFAULT_AI_TASK } from '@/lib/aiTaskContract';

import type { BpmnFactoryLike, ModdleElement } from './moddle';

interface PaletteLike {
  registerProvider: (priority: number, provider: FlowzerPaletteProvider) => void;
}

interface CreateLike {
  start: (event: Event, shape: unknown) => void;
}

interface ElementFactoryLike {
  createShape: (options: { type: string; businessObject: ModdleElement }) => unknown;
}

interface PaletteEntry {
  group: string;
  className: string;
  title: string;
  action: { dragstart: (event: Event) => void; click: (event: Event) => void };
}

/** Erzeugt die verschachtelten Moddle-Objekte der KI-Kachel mit korrekten Elternbeziehungen. */
export function createAiTaskBusinessObject(factory: BpmnFactoryLike): ModdleElement {
  const task = factory.create('bpmn:ServiceTask', { name: 'KI-Aufgabe' });
  const extensions = factory.create('bpmn:ExtensionElements', { values: [] });
  const taskDefinition = factory.create('zeebe:TaskDefinition', { type: AI_WORKER_TYPE });
  const aiTask = factory.create('flowzer:AiTask', {
    ...DEFAULT_AI_TASK,
    connectionId: undefined,
    model: undefined,
    instruction: undefined,
    resultSchema: undefined,
  });
  const instruction = factory.create('flowzer:Instruction', { body: DEFAULT_AI_TASK.instruction });
  const resultSchema = factory.create('flowzer:ResultSchema', { body: DEFAULT_AI_TASK.resultSchema });

  extensions.$parent = task;
  taskDefinition.$parent = extensions;
  aiTask.$parent = extensions;
  instruction.$parent = aiTask;
  resultSchema.$parent = aiTask;
  aiTask.instruction = instruction;
  aiTask.resultSchema = resultSchema;
  extensions.values = [taskDefinition, aiTask];
  task.extensionElements = extensions;
  return task;
}

/** Ergänzt bpmn-js um eine sichtbare KI-Kachel, ohne einen zweiten Prozessstandard einzuführen. */
class FlowzerPaletteProvider {
  static readonly $inject = ['palette', 'create', 'elementFactory', 'bpmnFactory'];
  private readonly create: CreateLike;
  private readonly elementFactory: ElementFactoryLike;
  private readonly bpmnFactory: BpmnFactoryLike;

  constructor(
    palette: PaletteLike,
    create: CreateLike,
    elementFactory: ElementFactoryLike,
    bpmnFactory: BpmnFactoryLike,
  ) {
    this.create = create;
    this.elementFactory = elementFactory;
    this.bpmnFactory = bpmnFactory;
    palette.registerProvider(900, this);
  }

  getPaletteEntries(): Record<string, PaletteEntry> {
    const start = (event: Event) => {
      const businessObject = createAiTaskBusinessObject(this.bpmnFactory);
      const shape = this.elementFactory.createShape({ type: 'bpmn:ServiceTask', businessObject });
      this.create.start(event, shape);
    };

    return {
      'create.flowzer-ai-task': {
        group: 'activity',
        className: 'bpmn-icon-service-task flowzer-ai-task-palette',
        title: 'KI-Aufgabe erstellen',
        action: { dragstart: start, click: start },
      },
    };
  }
}

export const FLOWZER_PALETTE_MODULE = {
  __init__: ['flowzerPaletteProvider'],
  flowzerPaletteProvider: ['type', FlowzerPaletteProvider],
};
