/**
 * Liest ein BPMN-Element in die flachen Werte, die das Eigenschaften-Panel anzeigt.
 *
 * Der Umfang folgt dem, was `src/core-engine/ModelParser.cs` auswertet. Was hier fehlt,
 * lässt sich in der Konsole nicht einstellen; was hier steht, ohne dass der Parser es liest,
 * wäre ein Feld ohne Wirkung. Beides ist der Fehler, den diese Datei verhindern soll.
 *
 * Bewusst ohne Modeler-Dienste: Reines Lesen über Moddle-Objekte, damit es sich mit
 * einfachen Objekten prüfen lässt.
 */

import {
  eventDefinition,
  expressionBody,
  extension,
  flag,
  text,
  type DiagramElement,
  type ModdleElement,
} from './moddle';

/** Die Elementgruppen, für die das Panel eigene Abschnitte zeigt. */
export type ElementKind = 'userTask' | 'serviceTask' | 'gateway' | 'sequenceFlow' | 'process' | 'other';

export interface IoMapping {
  source: string;
  target: string;
}

/** Ein ausgehender Fluss eines Tores — mit seiner Bedingung. */
export interface OutgoingFlow {
  id: string;
  name: string;
  targetLabel: string;
  condition: string;
  isDefault: boolean;
}

/** Ein Formular, das im Workflow selbst liegt (`zeebe:userTaskForm`). */
export interface EmbeddedForm {
  id: string;
  schema: string;
}

/** Eine menschliche Aufgabe des Diagramms samt ihrem Formularverweis. */
export interface UserTaskReference {
  id: string;
  name: string;
  formKey: string | null;
}

/** Die Art einer Zeitangabe. Genau eine gilt je Timer. */
export type TimerKind = 'duration' | 'date' | 'cycle';

export interface TimerDefinition {
  kind: TimerKind;
  expression: string;
}

/** Die Nachricht, auf die ein Ereignis oder eine Empfangsaufgabe wartet. */
export interface MessageReference {
  name: string;
  correlationKey: string;
}

/** Der aufgerufene Prozess einer Aufruf-Aktivität. */
export interface CalledProcess {
  processId: string;
  propagateAllChildVariables: boolean;
  propagateAllParentVariables: boolean;
}

export interface ScriptDefinition {
  expression: string;
  resultVariable: string;
}

/** Mehrfachausführung eines Schritts (`multiInstanceLoopCharacteristics`). */
export interface MultiInstance {
  /** Sequenziell oder parallel — das entscheidet die Elementart, nicht das Panel. */
  isSequential: boolean;
  inputCollection: string;
  inputElement: string;
  outputCollection: string;
  outputElement: string;
  completionCondition: string;
}

export interface Assignment {
  assignee: string;
  candidateGroups: string;
  candidateUsers: string;
}

export interface Schedule {
  dueDate: string;
  followUpDate: string;
}

/** Alle Werte eines ausgewählten Elements, die das Panel anzeigt. */
export interface ElementProperties {
  id: string;
  type: string;
  kind: ElementKind;
  name: string;

  /** Menschliche Aufgabe. */
  formKey: string | null;
  /**
   * Ein Formularverweis in `zeebe:externalReference`. Die Engine liest ihn nicht; er entsteht
   * in Camundas neuer User-Task-Semantik und stand früher auch in Diagrammen aus dieser
   * Konsole. Das Panel zeigt ihn, damit die Aufgabe nicht grundlos leer aussieht.
   */
  externalFormReference: string;
  assignee: string;
  candidateGroups: string;
  candidateUsers: string;
  dueDate: string;
  followUpDate: string;

  /** Auftrag an einen externen Worker. */
  jobType: string;
  retries: string;
  /** Ob die Engine an diesem Element einen Auftragstyp auswertet. */
  needsJobType: boolean;

  /** Zuordnungen zwischen Prozess- und Aufgabendaten. */
  inputs: IoMapping[];
  outputs: IoMapping[];
  supportsInputMappings: boolean;
  supportsOutputMappings: boolean;

  /** Sequenzfluss. */
  condition: string;
  isDefaultFlow: boolean;
  /** Nur an Toren und Aktivitäten wertet die Engine eine Bedingung aus. */
  conditionApplies: boolean;

  /** Tor: die ausgehenden Flüsse mit ihren Bedingungen. */
  outgoing: OutgoingFlow[];

  /** Zeitangabe, wenn das Element auf eine Zeit wartet. */
  timer: TimerDefinition | null;
  /**
   * Nachricht, auf die das Element wartet. Sendende Ereignisse haben hier `null`: Sie
   * verschicken die Nachricht über einen Worker-Auftrag, den Namen liest die Engine nicht.
   */
  message: MessageReference | null;
  /** Signal, das das Element empfängt oder auslöst. */
  signalName: string | null;
  /** Aufgerufener Prozess. */
  calledProcess: CalledProcess | null;
  /** Skript einer Skript-Aufgabe; `null`, wenn sie stattdessen als Auftrag läuft. */
  script: ScriptDefinition | null;
  isScriptTask: boolean;
  /** Mehrfachausführung, wenn das Element sie trägt. */
  multiInstance: MultiInstance | null;
}

const CONDITIONAL_SOURCES = ['bpmn:ExclusiveGateway', 'bpmn:InclusiveGateway', 'bpmn:Activity'];
const GATEWAY_TYPES = ['bpmn:ExclusiveGateway', 'bpmn:InclusiveGateway'];

/** Ereignisse, die auf etwas warten. Nur sie lösen eine Nachricht über `messageRef` auf. */
const CATCH_EVENT_TYPES = ['bpmn:StartEvent', 'bpmn:IntermediateCatchEvent', 'bpmn:BoundaryEvent'];

/** Ereignisse, die etwas aussenden. Eine Nachricht verschicken sie über einen Auftrag. */
const THROW_EVENT_TYPES = ['bpmn:IntermediateThrowEvent', 'bpmn:EndEvent'];

/** Elemente, für die der Parser Eingangszuordnungen liest. */
const INPUT_MAPPING_TYPES = [
  'bpmn:UserTask',
  'bpmn:ServiceTask',
  'bpmn:ScriptTask',
  'bpmn:SubProcess',
  'bpmn:CallActivity',
];

/** Ausgangszuordnungen liest der Parser zusätzlich am Start-Ereignis. */
const OUTPUT_MAPPING_TYPES = [...INPUT_MAPPING_TYPES, 'bpmn:StartEvent'];

function kindOf(element: DiagramElement): ElementKind {
  const type = element.businessObject?.$type ?? element.type;
  if (type === 'bpmn:UserTask') return 'userTask';
  if (type === 'bpmn:ServiceTask') return 'serviceTask';
  if (GATEWAY_TYPES.includes(type)) return 'gateway';
  if (type === 'bpmn:SequenceFlow') return 'sequenceFlow';
  if (type === 'bpmn:Process' || type === 'bpmn:Participant' || type === 'bpmn:Collaboration') return 'process';
  return 'other';
}

function conditionOf(element: DiagramElement): string {
  return expressionBody(element.businessObject, 'conditionExpression');
}

function label(element: DiagramElement | undefined): string {
  if (!element) return '—';
  return text(element.businessObject, 'name').trim() || element.id;
}

function outgoingFlows(element: DiagramElement): OutgoingFlow[] {
  return (element.outgoing ?? []).map((flow) => ({
    id: flow.id,
    name: text(flow.businessObject, 'name'),
    targetLabel: label(flow.target),
    condition: conditionOf(flow),
    isDefault: element.businessObject.default === flow.businessObject,
  }));
}

function ioMappings(element: DiagramElement, parameter: 'inputParameters' | 'outputParameters'): IoMapping[] {
  const mapping = extension(element.businessObject, 'zeebe:IoMapping');
  const entries = (mapping?.[parameter] as ModdleElement[] | undefined) ?? [];
  return entries.map((entry) => ({ source: text(entry, 'source'), target: text(entry, 'target') }));
}

/**
 * Die Zeitangabe eines Timers. Stehen mehrere im Diagramm, gilt dieselbe Reihenfolge wie im
 * Parser — sonst zeigte das Panel eine andere Angabe an, als die Engine benutzt.
 */
export function timerOf(businessObject: ModdleElement): TimerDefinition | null {
  const definition = eventDefinition(businessObject, 'bpmn:TimerEventDefinition');
  if (!definition) return null;

  const duration = expressionBody(definition, 'timeDuration');
  if (duration.length > 0) return { kind: 'duration', expression: duration };

  const date = expressionBody(definition, 'timeDate');
  if (date.length > 0) return { kind: 'date', expression: date };

  const cycle = expressionBody(definition, 'timeCycle');
  if (cycle.length > 0) return { kind: 'cycle', expression: cycle };

  return { kind: 'duration', expression: '' };
}

/**
 * Der Träger der Nachrichtenreferenz: die Empfangsaufgabe selbst oder ihre Ereignisdefinition.
 * Öffentlich, weil das Schreiben denselben Träger treffen muss wie das Lesen.
 */
export function messageHolder(businessObject: ModdleElement): ModdleElement | undefined {
  if (businessObject.$type === 'bpmn:ReceiveTask') return businessObject;
  if (!CATCH_EVENT_TYPES.includes(businessObject.$type)) return undefined;
  return eventDefinition(businessObject, 'bpmn:MessageEventDefinition');
}

function messageOf(businessObject: ModdleElement): MessageReference | null {
  const holder = messageHolder(businessObject);
  if (!holder) return null;

  const message = holder.messageRef as ModdleElement | undefined;
  return {
    name: text(message, 'name'),
    correlationKey: text(extension(message, 'zeebe:Subscription'), 'correlationKey'),
  };
}

function signalOf(businessObject: ModdleElement): string | null {
  const definition = eventDefinition(businessObject, 'bpmn:SignalEventDefinition');
  if (!definition) return null;
  return text(definition.signalRef as ModdleElement | undefined, 'name');
}

export function calledProcessOf(businessObject: ModdleElement): CalledProcess | null {
  if (businessObject.$type !== 'bpmn:CallActivity') return null;

  const called = extension(businessObject, 'zeebe:CalledElement');
  return {
    processId: text(called, 'processId'),
    // Ohne Angabe reicht die Engine alles durch — dieselbe Vorgabe wie im Parser.
    propagateAllChildVariables: flag(called, 'propagateAllChildVariables', true),
    propagateAllParentVariables: flag(called, 'propagateAllParentVariables', true),
  };
}

function scriptOf(businessObject: ModdleElement): ScriptDefinition | null {
  const script = extension(businessObject, 'zeebe:Script');
  if (!script) return null;
  return { expression: text(script, 'expression'), resultVariable: text(script, 'resultVariable') };
}

export function multiInstanceOf(businessObject: ModdleElement): MultiInstance | null {
  const loop = businessObject.loopCharacteristics as ModdleElement | undefined;
  if (loop?.$type !== 'bpmn:MultiInstanceLoopCharacteristics') return null;

  const zeebeLoop = extension(loop, 'zeebe:LoopCharacteristics');
  return {
    isSequential: flag(loop, 'isSequential'),
    inputCollection: text(zeebeLoop, 'inputCollection'),
    inputElement: text(zeebeLoop, 'inputElement'),
    outputCollection: text(zeebeLoop, 'outputCollection'),
    outputElement: text(zeebeLoop, 'outputElement'),
    completionCondition: expressionBody(loop, 'completionCondition'),
  };
}

/**
 * Ob die Engine an diesem Element einen Auftragstyp liest. Ein sendendes Nachrichtenereignis
 * verschickt die Nachricht über einen Worker-Auftrag — der Nachrichtenname spielt dort keine
 * Rolle. Eine Skript-Aufgabe läuft entweder als Skript oder als Auftrag.
 */
function needsJobType(businessObject: ModdleElement): boolean {
  if (businessObject.$type === 'bpmn:ServiceTask') return true;

  if (
    THROW_EVENT_TYPES.includes(businessObject.$type) &&
    eventDefinition(businessObject, 'bpmn:MessageEventDefinition')
  ) {
    return true;
  }

  return businessObject.$type === 'bpmn:ScriptTask' && !extension(businessObject, 'zeebe:Script');
}

/**
 * Prüft den Typ eines Elements einschließlich seiner Oberklassen. `bpmn:Activity` ist keine
 * eigene Elementart, sondern die Oberklasse von Aufgaben und Teilprozessen — ohne diese
 * Prüfung bekäme ein Fluss aus einer Aufgabe heraus kein Bedingungsfeld.
 */
function isTypeOrSubtype(element: DiagramElement | undefined, type: string): boolean {
  const descriptor = element?.businessObject?.$instanceOf;
  if (typeof descriptor === 'function') {
    return (descriptor as (candidate: string) => boolean).call(element!.businessObject, type);
  }
  return element?.businessObject?.$type === type;
}

/**
 * Liest alle Werte, die das Panel für ein Element anzeigt.
 *
 * Frei von Seiteneffekten und ohne Modeler-Dienste: Das Panel liest während des Zeichnens,
 * und ein Aufruf, der dabei das Modell veränderte, löste mitten im Rendern Ereignisse aus.
 */
export function readElementProperties(element: DiagramElement): ElementProperties {
  const businessObject = element.businessObject;
  const formDefinition = extension(businessObject, 'zeebe:FormDefinition');
  const assignment = extension(businessObject, 'zeebe:AssignmentDefinition');
  const schedule = extension(businessObject, 'zeebe:TaskSchedule');
  const taskDefinition = extension(businessObject, 'zeebe:TaskDefinition');
  const formKey = text(formDefinition, 'formKey') || text(formDefinition, 'formId');
  const type = businessObject?.$type ?? element.type;

  return {
    id: element.id,
    type,
    kind: kindOf(element),
    name: text(businessObject, 'name'),

    formKey: formKey.length > 0 ? formKey : null,
    externalFormReference: text(formDefinition, 'externalReference'),
    assignee: text(assignment, 'assignee'),
    candidateGroups: text(assignment, 'candidateGroups'),
    candidateUsers: text(assignment, 'candidateUsers'),
    dueDate: text(schedule, 'dueDate'),
    followUpDate: text(schedule, 'followUpDate'),

    jobType: text(taskDefinition, 'type'),
    retries: text(taskDefinition, 'retries'),
    needsJobType: needsJobType(businessObject),

    inputs: ioMappings(element, 'inputParameters'),
    outputs: ioMappings(element, 'outputParameters'),
    supportsInputMappings: INPUT_MAPPING_TYPES.includes(type),
    supportsOutputMappings: OUTPUT_MAPPING_TYPES.includes(type),

    condition: conditionOf(element),
    isDefaultFlow: element.source?.businessObject.default === businessObject,
    conditionApplies:
      kindOf(element) === 'sequenceFlow' &&
      CONDITIONAL_SOURCES.some((candidate) => isTypeOrSubtype(element.source, candidate)),

    outgoing: outgoingFlows(element),

    timer: timerOf(businessObject),
    message: messageOf(businessObject),
    signalName: signalOf(businessObject),
    calledProcess: calledProcessOf(businessObject),
    script: scriptOf(businessObject),
    isScriptTask: type === 'bpmn:ScriptTask',
    multiInstance: multiInstanceOf(businessObject),
  };
}
