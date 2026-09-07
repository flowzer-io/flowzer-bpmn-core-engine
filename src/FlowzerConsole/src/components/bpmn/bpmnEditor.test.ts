import { describe, expect, it } from 'vitest';

import { createBpmnEditor } from './bpmnEditor';
import type { DiagramElement, ModdleElement } from './moddle';

/**
 * Ein Modeler-Doppel: So viel von bpmn-js, wie die Schreibpfade sehen. `updateProperties` und
 * `updateModdleProperties` verhalten sich wie im Original — sie setzen die genannten
 * Eigenschaften und entfernen die, die `undefined` sind.
 *
 * Geprüft wird damit, *wohin* geschrieben wird: an welches Element, in welche
 * `extensionElements`, mit welchen Attributen. Genau dort sitzen die Fehler, die im Browser
 * erst beim Speichern eines Workflows auffallen.
 */
function createModelerDouble(elements: DiagramElement[]) {
  const registry = {
    get: (id: string) => elements.find((element) => element.id === id),
    filter: (predicate: (element: DiagramElement) => boolean) => elements.filter(predicate),
  };

  const modeling = {
    updateProperties(element: DiagramElement, properties: Record<string, unknown>) {
      applyProperties(element.businessObject, properties);
    },
    updateModdleProperties(
      _element: DiagramElement,
      moddleElement: ModdleElement,
      properties: Record<string, unknown>,
    ) {
      applyProperties(moddleElement, properties);
    },
  };

  // Wie `BpmnFactory`: Wurzelelemente bekommen ihre Kennung von der Factory, nicht vom Aufrufer.
  let nextId = 1;
  const factory = {
    create: (type: string, properties: Record<string, unknown> = {}) => {
      const created = { $type: type } as ModdleElement;
      applyProperties(created, properties);
      if (!created.id && ROOT_ELEMENT_TYPES.includes(type)) {
        created.id = `${type.split(':')[1]}_${nextId++}`;
      }
      return created;
    },
  };

  const services: Record<string, unknown> = { elementRegistry: registry, modeling, bpmnFactory: factory };
  return createBpmnEditor({ get: <T,>(name: string) => services[name] as T });
}

const ROOT_ELEMENT_TYPES = ['bpmn:Message', 'bpmn:Signal'];

function applyProperties(target: ModdleElement, properties: Record<string, unknown>): void {
  for (const [key, value] of Object.entries(properties)) {
    if (value === undefined) delete target[key];
    else target[key] = value;
  }
}

function shape(businessObject: Partial<ModdleElement> & { $type: string }, id: string): DiagramElement {
  return { id, type: businessObject.$type, businessObject: businessObject as ModdleElement };
}

/** Ein Prozess mit einem Element darin — samt der Elternkette, die der Adapter hochläuft. */
function diagram(child: Partial<ModdleElement> & { $type: string }, childId = 'Element_1') {
  const definitions = { $type: 'bpmn:Definitions', rootElements: [] as ModdleElement[] } as ModdleElement;
  const process = { $type: 'bpmn:Process', id: 'Process_1', $parent: definitions } as ModdleElement;
  definitions.rootElements = [process];

  const businessObject = { ...child, $parent: process } as ModdleElement;
  const processShape = shape(process as Partial<ModdleElement> & { $type: string }, 'Process_1');
  const childShape = shape(businessObject as Partial<ModdleElement> & { $type: string }, childId);

  return {
    definitions,
    process,
    businessObject,
    editor: createModelerDouble([processShape, childShape]),
  };
}

function extensionsOf(owner: ModdleElement): ModdleElement[] {
  return ((owner.extensionElements as ModdleElement | undefined)?.values as ModdleElement[] | undefined) ?? [];
}

function extensionOf(owner: ModdleElement, type: string): ModdleElement | undefined {
  return extensionsOf(owner).find((value) => value.$type === type);
}

// Testzweck: Der Form-Key gehoert in `zeebe:formDefinition` und schliesst die beiden anderen
// Schreibweisen aus. Blieben `formId` oder `externalReference` stehen, entschiede die
// Lesereihenfolge der Engine, welches Formular gilt.
describe('setFormKey', () => {
  it('legt die Erweiterung an und räumt die anderen Schreibweisen weg', () => {
    const { businessObject, editor } = diagram({
      $type: 'bpmn:UserTask',
      extensionElements: {
        $type: 'bpmn:ExtensionElements',
        values: [{ $type: 'zeebe:FormDefinition', formId: 'alt', externalReference: 'auch alt' }],
      } as ModdleElement,
    });

    editor.setFormKey('Element_1', 'Urlaubsantrag');

    const formDefinition = extensionOf(businessObject, 'zeebe:FormDefinition')!;
    expect(formDefinition.formKey).toBe('Urlaubsantrag');
    expect(formDefinition.formId).toBeUndefined();
    expect(formDefinition.externalReference).toBeUndefined();
  });

  it('entfernt mit dem Verweis auch die leer gewordenen extensionElements', () => {
    const { businessObject, editor } = diagram({
      $type: 'bpmn:UserTask',
      extensionElements: {
        $type: 'bpmn:ExtensionElements',
        values: [{ $type: 'zeebe:FormDefinition', formKey: 'Urlaubsantrag' }],
      } as ModdleElement,
    });

    editor.setFormKey('Element_1', null);

    expect(businessObject.extensionElements).toBeUndefined();
  });
});

// Testzweck: Ein Timer gilt über genau eine Zeitangabe. Bliebe beim Wechsel der Art die alte
// stehen, entschiede die Lesereihenfolge der Engine, welche wirkt.
describe('setTimer', () => {
  it('schreibt die gewählte Art und entfernt die beiden anderen', () => {
    const definition = { $type: 'bpmn:TimerEventDefinition', timeCycle: { $type: 'bpmn:FormalExpression', body: 'R3/PT1H' } };
    const { businessObject, editor } = diagram({
      $type: 'bpmn:IntermediateCatchEvent',
      eventDefinitions: [definition as ModdleElement],
    });

    editor.setTimer('Element_1', { kind: 'duration', expression: 'PT2H' });

    const written = (businessObject.eventDefinitions as ModdleElement[])[0]!;
    expect((written.timeDuration as ModdleElement).body).toBe('PT2H');
    expect(written.timeCycle).toBeUndefined();
    expect(written.timeDate).toBeUndefined();
  });

  it('behält beim Wechsel der Art den bereits eingetragenen Wert', () => {
    const definition = { $type: 'bpmn:TimerEventDefinition', timeDuration: { $type: 'bpmn:FormalExpression', body: 'PT2H' } };
    const { businessObject, editor } = diagram({
      $type: 'bpmn:IntermediateCatchEvent',
      eventDefinitions: [definition as ModdleElement],
    });

    editor.setTimer('Element_1', { kind: 'cycle' });

    const written = (businessObject.eventDefinitions as ModdleElement[])[0]!;
    expect((written.timeCycle as ModdleElement).body).toBe('PT2H');
    expect(written.timeDuration).toBeUndefined();
  });
});

// Testzweck: Jedes Feld schickt nur seine eigene Aenderung. Gaebe es die ganze Gruppe mit,
// machte ein Klick aus einem Textfeld heraus auf einen Schalter derselben Gruppe die eben
// getippte Eingabe wieder zunichte — das Feld schreibt erst beim Verlassen.
describe('Teiländerungen', () => {
  it('lässt die übrigen Werte der Zuweisung stehen', () => {
    const { businessObject, editor } = diagram({
      $type: 'bpmn:UserTask',
      extensionElements: {
        $type: 'bpmn:ExtensionElements',
        values: [{ $type: 'zeebe:AssignmentDefinition', assignee: 'anna', candidateGroups: 'personal' }],
      } as ModdleElement,
    });

    editor.setAssignment('Element_1', { candidateUsers: 'bruno' });

    const assignment = extensionOf(businessObject, 'zeebe:AssignmentDefinition')!;
    expect(assignment.assignee).toBe('anna');
    expect(assignment.candidateGroups).toBe('personal');
    expect(assignment.candidateUsers).toBe('bruno');
  });

  it('entfernt die Zuweisung, wenn ihr letzter Wert geleert wird', () => {
    const { businessObject, editor } = diagram({
      $type: 'bpmn:UserTask',
      extensionElements: {
        $type: 'bpmn:ExtensionElements',
        values: [{ $type: 'zeebe:AssignmentDefinition', assignee: 'anna' }],
      } as ModdleElement,
    });

    editor.setAssignment('Element_1', { assignee: '' });

    expect(businessObject.extensionElements).toBeUndefined();
  });
});

// Testzweck: Der Parser liest `processId` als Pflichtangabe und laeuft ohne sie in einen
// Nullverweis. Eine Erweiterung ohne Kennung waere ein 500 beim Speichern statt einer Meldung.
describe('setCalledProcess', () => {
  it('schreibt die Datenweitergabe zusammen mit der Prozesskennung', () => {
    const { businessObject, editor } = diagram({ $type: 'bpmn:CallActivity' });

    editor.setCalledProcess('Element_1', { processId: 'Process_Sub' });
    editor.setCalledProcess('Element_1', { propagateAllChildVariables: false });

    const called = extensionOf(businessObject, 'zeebe:CalledElement')!;
    expect(called.processId).toBe('Process_Sub');
    expect(called.propagateAllChildVariables).toBe(false);
    expect(called.propagateAllParentVariables).toBe(true);
  });

  it('legt ohne Prozesskennung keine Erweiterung an', () => {
    const { businessObject, editor } = diagram({ $type: 'bpmn:CallActivity' });

    editor.setCalledProcess('Element_1', { propagateAllChildVariables: false });

    expect(businessObject.extensionElements).toBeUndefined();
  });
});

// Testzweck: Nachricht und Signal sind Wurzelelemente des Dokuments. Wuerden sie am Ereignis
// selbst angelegt, faende die Engine sie nicht — sie sucht sie neben den Prozessen.
describe('setMessage und setSignal', () => {
  it('legt eine fehlende Nachricht als Wurzelelement an und verweist darauf', () => {
    const definition = { $type: 'bpmn:MessageEventDefinition' };
    const { definitions, businessObject, editor } = diagram({
      $type: 'bpmn:StartEvent',
      eventDefinitions: [definition as ModdleElement],
    });

    editor.setMessage('Element_1', { name: 'Antrag eingegangen', correlationKey: '=antragsnummer' });

    const roots = definitions.rootElements as ModdleElement[];
    const message = roots.find((root) => root.$type === 'bpmn:Message')!;
    expect(message.name).toBe('Antrag eingegangen');
    expect(message.id).toMatch(/^Message_/);
    expect((businessObject.eventDefinitions as ModdleElement[])[0]!.messageRef).toBe(message);
    expect(extensionOf(message, 'zeebe:Subscription')!.correlationKey).toBe('=antragsnummer');
  });

  it('benennt eine vorhandene Nachricht um, statt eine zweite anzulegen', () => {
    const message = { $type: 'bpmn:Message', id: 'Message_alt', name: 'Alt' } as ModdleElement;
    const definition = { $type: 'bpmn:MessageEventDefinition', messageRef: message };
    const { definitions, editor } = diagram({
      $type: 'bpmn:StartEvent',
      eventDefinitions: [definition as ModdleElement],
    });
    (definitions.rootElements as ModdleElement[]).push(message);

    editor.setMessage('Element_1', { name: 'Neu' });

    expect(message.name).toBe('Neu');
    expect((definitions.rootElements as ModdleElement[]).filter((root) => root.$type === 'bpmn:Message')).toHaveLength(1);
  });

  it('nimmt an einer Empfangsaufgabe die Aufgabe selbst als Träger', () => {
    const { definitions, businessObject, editor } = diagram({ $type: 'bpmn:ReceiveTask' });

    editor.setMessage('Element_1', { name: 'Antwort' });

    const message = (definitions.rootElements as ModdleElement[]).find((root) => root.$type === 'bpmn:Message');
    expect(businessObject.messageRef).toBe(message);
  });

  it('legt ein fehlendes Signal als Wurzelelement an', () => {
    const definition = { $type: 'bpmn:SignalEventDefinition' };
    const { definitions, editor } = diagram({
      $type: 'bpmn:EndEvent',
      eventDefinitions: [definition as ModdleElement],
    });

    editor.setSignal('Element_1', 'Freigabe erteilt');

    const signal = (definitions.rootElements as ModdleElement[]).find((root) => root.$type === 'bpmn:Signal')!;
    expect(signal.name).toBe('Freigabe erteilt');
    expect(signal.id).toMatch(/^Signal_/);
    expect((definition as ModdleElement).signalRef).toBe(signal);
  });
});

// Testzweck: Eine Skript-Aufgabe laeuft entweder als Ausdruck oder als Auftrag. Blieben beide
// Erweiterungen stehen, nähme die Engine still das Skript — der Auftragstyp im Panel wäre eine
// Angabe ohne Wirkung.
describe('setScriptMode', () => {
  it('entfernt beim Wechsel zum Skript den Auftrag', () => {
    const { businessObject, editor } = diagram({
      $type: 'bpmn:ScriptTask',
      extensionElements: {
        $type: 'bpmn:ExtensionElements',
        values: [{ $type: 'zeebe:TaskDefinition', type: 'rechnen' }],
      } as ModdleElement,
    });

    editor.setScriptMode('Element_1', 'script');

    expect(extensionOf(businessObject, 'zeebe:TaskDefinition')).toBeUndefined();
    expect(extensionOf(businessObject, 'zeebe:Script')).toBeDefined();
  });

  it('entfernt beim Wechsel zum Auftrag das Skript', () => {
    const { businessObject, editor } = diagram({
      $type: 'bpmn:ScriptTask',
      extensionElements: {
        $type: 'bpmn:ExtensionElements',
        values: [{ $type: 'zeebe:Script', expression: '=1' }],
      } as ModdleElement,
    });

    editor.setScriptMode('Element_1', 'job');

    expect(extensionOf(businessObject, 'zeebe:Script')).toBeUndefined();
  });
});

// Testzweck: Die Angaben zur Mehrfachausfuehrung gehoeren an die Schleife, nicht an das
// Element. Landeten sie am Element, liefe der Schritt genau einmal.
describe('setMultiInstance', () => {
  it('schreibt Liste und Abbruchbedingung an die Schleife', () => {
    const loop = { $type: 'bpmn:MultiInstanceLoopCharacteristics', isSequential: true } as ModdleElement;
    const { businessObject, editor } = diagram({ $type: 'bpmn:ServiceTask', loopCharacteristics: loop });

    editor.setMultiInstance('Element_1', {
      inputCollection: '=positionen',
      inputElement: 'position',
      outputCollection: '',
      outputElement: '',
      completionCondition: '=anzahl > 3',
    });

    expect(businessObject.extensionElements).toBeUndefined();
    expect((loop.completionCondition as ModdleElement).body).toBe('=anzahl > 3');
    const zeebeLoop = extensionOf(loop, 'zeebe:LoopCharacteristics')!;
    expect(zeebeLoop.inputCollection).toBe('=positionen');
    expect(zeebeLoop.inputElement).toBe('position');
    expect(zeebeLoop.outputCollection).toBeUndefined();
  });
});

// Testzweck: Ein Formular im Workflow gehoert an den Prozess der Aufgabe. In einer
// Kollaboration waere der erstbeste Prozess der falsche — und ein Export dieses Pools
// verloere das Formular.
describe('saveEmbeddedForm', () => {
  it('legt das Formular am Prozess der Aufgabe an', () => {
    const { process, editor } = diagram({ $type: 'bpmn:UserTask' });

    editor.saveEmbeddedForm('Form_1', '{"components":[]}', 'Element_1');

    const form = extensionOf(process, 'zeebe:UserTaskForm')!;
    expect(form.id).toBe('Form_1');
    expect(form.body).toBe('{"components":[]}');
    expect(editor.listEmbeddedForms()).toEqual([{ id: 'Form_1', schema: '{"components":[]}' }]);
  });

  it('ersetzt das Schema eines vorhandenen Formulars, statt ein zweites anzulegen', () => {
    const { process, editor } = diagram({ $type: 'bpmn:UserTask' });
    editor.saveEmbeddedForm('Form_1', 'alt', 'Element_1');

    editor.saveEmbeddedForm('Form_1', 'neu', 'Element_1');

    expect(extensionsOf(process).filter((value) => value.$type === 'zeebe:UserTaskForm')).toHaveLength(1);
    expect(extensionOf(process, 'zeebe:UserTaskForm')!.body).toBe('neu');
  });
});
