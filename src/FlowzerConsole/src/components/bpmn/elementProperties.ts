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
  enclosing,
  eventDefinition,
  expressionBody,
  extension,
  flag,
  text,
  type DiagramElement,
  type ModdleElement,
} from './moddle';
import { AI_WORKER_TYPE, type AiTaskConfiguration, type ServiceTaskMode } from '@/lib/aiTaskContract';

/** Die Elementgruppen, für die das Panel eigene Abschnitte zeigt. */
export type ElementKind =
  | 'userTask'
  | 'serviceTask'
  | 'startEvent'
  | 'gateway'
  | 'sequenceFlow'
  | 'process'
  | 'other';

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

/**
 * Ein Element des Diagramms, das auf ein Formular zeigt: eine menschliche Aufgabe oder das
 * Startereignis mit dem Startformular. Beide stehen in derselben Liste, damit die Übersicht
 * und die Markierung im Diagramm alle Formulare eines Workflows zeigen.
 */
export interface FormOwner {
  id: string;
  name: string;
  kind: 'userTask' | 'startEvent';
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

export type AssignmentMode = 'text' | 'directory' | 'invalid';

/** Stabile lokale Verzeichnis-IDs einer Directory-Zuweisung. */
export interface DirectoryAssignment {
  assigneeId: string;
  candidateUserIds: string[];
  candidateGroupIds: string[];
}

export interface Schedule {
  dueDate: string;
  followUpDate: string;
}

/** Ein `bpmn:Error` des Dokuments, wie ihn die Auswahlliste anbietet. */
export interface ErrorOption {
  id: string;
  name: string;
  code: string;
}

/**
 * Der Fehlerbezug eines Error-Ereignisses. `errorId` ist leer, solange das Ereignis auf keinen
 * `bpmn:Error` zeigt — ein Error-Boundary faengt dann jeden Fehler, ein Error-Ende wirft einen
 * Fehler ohne Code.
 */
export interface ErrorReference {
  errorId: string;
  name: string;
  code: string;
  available: ErrorOption[];
}

/** Alle Werte eines ausgewählten Elements, die das Panel anzeigt. */
export interface ElementProperties {
  id: string;
  type: string;
  kind: ElementKind;
  name: string;

  /** Menschliche Aufgabe oder Startereignis. */
  formKey: string | null;
  /**
   * Ob an diesem Element ein Startformular gilt — also am reinen Startereignis ohne Timer-,
   * Nachrichten- oder Signaldefinition. Nur dort liest die Engine den Form-Key; an einem
   * Zeitstart gäbe es niemanden, der das Formular ausfüllt.
   */
  startFormApplies: boolean;
  /**
   * Ein Formularverweis in `zeebe:externalReference`. Die Engine liest ihn nicht; er entsteht
   * in Camundas neuer User-Task-Semantik und stand früher auch in Diagrammen aus dieser
   * Konsole. Das Panel zeigt ihn, damit die Aufgabe nicht grundlos leer aussieht.
   */
  externalFormReference: string;
  assignee: string;
  candidateGroups: string;
  candidateUsers: string;
  assignmentMode: AssignmentMode;
  directoryAssignment: DirectoryAssignment;
  /** Erklaert einen nicht unterstützten Vertrag, ohne ihn beim Öffnen umzuschreiben. */
  assignmentContractWarning: string | null;
  dueDate: string;
  followUpDate: string;

  /** Auftrag an einen externen Worker. */
  jobType: string;
  retries: string;
  serviceTaskMode: ServiceTaskMode;
  aiTask: AiTaskConfiguration | null;
  /** Ob die Engine an diesem Element einen Auftragstyp auswertet. */
  needsJobType: boolean;
  /**
   * Ob der Auftragstyp an diesem Element freiwillig ist. An sendenden Nachrichtenelementen
   * ist er es: Ohne ihn stellt die Engine die Nachricht selbst zu, mit ihm übernimmt ein
   * Worker den Versand.
   */
  jobTypeOptional: boolean;

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
   * Nachricht, auf die das Element wartet oder die es aussendet. Beide Seiten tragen denselben
   * Verweis auf ein `bpmn:Message` samt Korrelationsschlüssel; nur so finden Werfen und Fangen
   * zueinander.
   */
  message: MessageReference | null;
  /** Ob dieses Element die Nachricht aussendet statt auf sie zu warten. */
  sendsMessage: boolean;
  /** Signal, das das Element empfängt oder auslöst. */
  signalName: string | null;
  /** Fehlerbezug eines Error-Ende- oder Error-Boundary-Ereignisses. */
  error: ErrorReference | null;
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

/** Ereignisse, die auf etwas warten. */
const CATCH_EVENT_TYPES = ['bpmn:StartEvent', 'bpmn:IntermediateCatchEvent', 'bpmn:BoundaryEvent'];

/** Ereignisse, die etwas aussenden. */
const THROW_EVENT_TYPES = ['bpmn:IntermediateThrowEvent', 'bpmn:EndEvent'];

/** Elemente, für die der Parser Eingangszuordnungen liest. */
const INPUT_MAPPING_TYPES = [
  'bpmn:UserTask',
  'bpmn:ServiceTask',
  'bpmn:ScriptTask',
  'bpmn:SubProcess',
  'bpmn:CallActivity',
  // An einem sendenden Element bestimmt der Eingang, was mit der Nachricht mitgeht.
  'bpmn:SendTask',
];

/** Ausgangszuordnungen liest der Parser zusätzlich am Start-Ereignis. */
const OUTPUT_MAPPING_TYPES = [
  'bpmn:UserTask',
  'bpmn:ServiceTask',
  'bpmn:ScriptTask',
  'bpmn:SubProcess',
  'bpmn:CallActivity',
  'bpmn:StartEvent',
];

function kindOf(element: DiagramElement): ElementKind {
  const type = element.businessObject?.$type ?? element.type;
  if (type === 'bpmn:UserTask') return 'userTask';
  if (type === 'bpmn:ServiceTask') return 'serviceTask';
  if (type === 'bpmn:StartEvent') return 'startEvent';
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
  // Aufgaben tragen den Verweis selbst, Ereignisse an ihrer Nachrichtendefinition.
  if (businessObject.$type === 'bpmn:ReceiveTask' || businessObject.$type === 'bpmn:SendTask') {
    return businessObject;
  }
  if (!CATCH_EVENT_TYPES.includes(businessObject.$type) && !THROW_EVENT_TYPES.includes(businessObject.$type)) {
    return undefined;
  }
  return eventDefinition(businessObject, 'bpmn:MessageEventDefinition');
}

/**
 * Ob das Element eine Nachricht aussendet: ein Send-Task oder ein werfendes Ereignis mit
 * Nachrichtendefinition. Das entscheidet über die Beschriftung im Panel — warten und senden
 * benutzen dieselben Felder, meinen aber Gegenteiliges.
 */
export function sendsMessage(businessObject: ModdleElement): boolean {
  if (businessObject.$type === 'bpmn:SendTask') return true;
  return (
    THROW_EVENT_TYPES.includes(businessObject.$type) &&
    Boolean(eventDefinition(businessObject, 'bpmn:MessageEventDefinition'))
  );
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

/** Ereignisse, an denen die Engine eine Fehlerdefinition auswertet. */
const ERROR_EVENT_TYPES = ['bpmn:EndEvent', 'bpmn:BoundaryEvent'];

/**
 * Der Träger der Fehlerreferenz. Öffentlich, weil das Schreiben denselben Träger treffen muss
 * wie das Lesen.
 */
export function errorHolder(businessObject: ModdleElement): ModdleElement | undefined {
  if (!ERROR_EVENT_TYPES.includes(businessObject.$type)) return undefined;
  return eventDefinition(businessObject, 'bpmn:ErrorEventDefinition');
}

/** Die `bpmn:Error`-Wurzelelemente des Dokuments, in Dokumentreihenfolge. */
export function availableErrors(businessObject: ModdleElement): ErrorOption[] {
  const definitions = enclosing(businessObject, 'bpmn:Definitions');
  const rootElements = (definitions?.rootElements as ModdleElement[] | undefined) ?? [];
  return rootElements
    .filter((rootElement) => rootElement.$type === 'bpmn:Error')
    .map((rootElement) => ({
      id: text(rootElement, 'id'),
      name: text(rootElement, 'name'),
      code: text(rootElement, 'errorCode'),
    }))
    .filter((option) => option.id.length > 0);
}

function errorOf(businessObject: ModdleElement): ErrorReference | null {
  const holder = errorHolder(businessObject);
  if (!holder) return null;

  const error = holder.errorRef as ModdleElement | undefined;
  return {
    errorId: text(error, 'id'),
    name: text(error, 'name'),
    code: text(error, 'errorCode'),
    available: availableErrors(businessObject),
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
 * Ob die Engine an diesem Element einen Auftragstyp liest. Am Service-Task ist er Pflicht, an
 * einem sendenden Nachrichtenelement freiwillig: Ohne ihn stellt die Engine die Nachricht
 * selbst zu. Eine Skript-Aufgabe läuft entweder als Skript oder als Auftrag.
 */
function needsJobType(businessObject: ModdleElement): boolean {
  if (businessObject.$type === 'bpmn:ServiceTask') return true;
  if (sendsMessage(businessObject)) return true;

  return businessObject.$type === 'bpmn:ScriptTask' && !extension(businessObject, 'zeebe:Script');
}

/**
 * Ereignisdefinitionen, bei denen der Parser ein `formDefinition` am Startereignis übergeht:
 * Ein Zeit-, Nachrichten- oder Signalstart läuft ohne jemanden, der etwas ausfüllen könnte.
 * Alle übrigen Definitionen liest `ModelParser.HandleStartEvent` als gewöhnlichen Start —
 * dort gilt der Form-Key also sehr wohl.
 */
const AUTOMATIC_START_DEFINITIONS = [
  'bpmn:TimerEventDefinition',
  'bpmn:MessageEventDefinition',
  'bpmn:SignalEventDefinition',
];

/**
 * Ob an diesem Element ein Startformular gilt. Dieselbe Bedingung wertet die Engine aus:
 * ein Startereignis unmittelbar im Prozess (`ModelParser.HandleStartEvent` für den Form-Key,
 * `FlowzerStartForm.FormKeyOf` für die Zuordnung zum Prozess), das nicht auf Zeit, Nachricht
 * oder Signal wartet.
 *
 * Das Startereignis eines Subprozesses zählt nicht: Es startet den Subprozess, nicht den
 * Workflow — ein Formular dort füllte niemand aus.
 */
export function startFormAppliesTo(businessObject: ModdleElement): boolean {
  if (businessObject?.$type !== 'bpmn:StartEvent') return false;
  if ((businessObject.$parent as ModdleElement | undefined)?.$type !== 'bpmn:Process') return false;

  const definitions = (businessObject.eventDefinitions as ModdleElement[] | undefined) ?? [];
  return !definitions.some((definition) => AUTOMATIC_START_DEFINITIONS.includes(definition.$type));
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
  const flowzerAssignment = extension(businessObject, 'flowzer:TaskAssignment');
  const assignmentMode = assignmentModeOf(flowzerAssignment);
  const schedule = extension(businessObject, 'zeebe:TaskSchedule');
  const taskDefinition = extension(businessObject, 'zeebe:TaskDefinition');
  const aiTask = extension(businessObject, 'flowzer:AiTask');
  const formKey = text(formDefinition, 'formKey') || text(formDefinition, 'formId');
  const type = businessObject?.$type ?? element.type;

  return {
    id: element.id,
    type,
    kind: kindOf(element),
    name: text(businessObject, 'name'),

    formKey: formKey.length > 0 ? formKey : null,
    startFormApplies: startFormAppliesTo(businessObject),
    externalFormReference: text(formDefinition, 'externalReference'),
    assignee: text(assignment, 'assignee'),
    candidateGroups: text(assignment, 'candidateGroups'),
    candidateUsers: text(assignment, 'candidateUsers'),
    assignmentMode,
    directoryAssignment: {
      assigneeId: text(flowzerAssignment, 'assigneeId'),
      candidateUserIds: commaSeparated(text(flowzerAssignment, 'candidateUserIds')),
      candidateGroupIds: commaSeparated(text(flowzerAssignment, 'candidateGroupIds')),
    },
    assignmentContractWarning:
      assignmentMode === 'invalid'
        ? `Der Zuweisungsmodus „${text(flowzerAssignment, 'mode') || '(leer)'}“ wird nicht unterstützt.`
        : null,
    dueDate: text(schedule, 'dueDate'),
    followUpDate: text(schedule, 'followUpDate'),

    jobType: text(taskDefinition, 'type'),
    retries: text(taskDefinition, 'retries'),
    serviceTaskMode:
      type === 'bpmn:ServiceTask' && (aiTask || text(taskDefinition, 'type') === AI_WORKER_TYPE)
        ? 'ai'
        : 'worker',
    aiTask: aiTask
      ? {
          contractVersion: text(aiTask, 'contractVersion'),
          connectionId: text(aiTask, 'connectionId'),
          model: text(aiTask, 'model'),
          instructionVersion: text(aiTask, 'instructionVersion'),
          instruction: text(aiTask.instruction as ModdleElement | undefined, 'body'),
          resultSchema: text(aiTask.resultSchema as ModdleElement | undefined, 'body'),
          maxInputTokens: text(aiTask, 'maxInputTokens'),
          maxOutputTokens: text(aiTask, 'maxOutputTokens'),
          timeoutSeconds: text(aiTask, 'timeoutSeconds'),
          tools: ((aiTask.tools as ModdleElement[] | undefined) ?? []).map((tool) => ({
            toolId: text(tool, 'id'),
            toolVersion: text(tool, 'version'),
            approval: text(tool, 'approval') as AiTaskConfiguration['tools'][number]['approval'],
          })),
        }
      : null,
    needsJobType: needsJobType(businessObject),
    jobTypeOptional: sendsMessage(businessObject),

    inputs: ioMappings(element, 'inputParameters'),
    outputs: ioMappings(element, 'outputParameters'),
    // Ein sendendes Ereignis ist keine Aktivität, liest aber seinen Eingang: Er bestimmt, was
    // mit der Nachricht mitgeht. Ein empfangendes Ereignis schreibt umgekehrt seinen Ausgang.
    supportsInputMappings: INPUT_MAPPING_TYPES.includes(type) || sendsMessage(businessObject),
    supportsOutputMappings:
      OUTPUT_MAPPING_TYPES.includes(type)
      || (type === 'bpmn:IntermediateCatchEvent'
        && Boolean(eventDefinition(businessObject, 'bpmn:MessageEventDefinition'))),

    condition: conditionOf(element),
    isDefaultFlow: element.source?.businessObject.default === businessObject,
    conditionApplies:
      kindOf(element) === 'sequenceFlow' &&
      CONDITIONAL_SOURCES.some((candidate) => isTypeOrSubtype(element.source, candidate)),

    outgoing: outgoingFlows(element),

    timer: timerOf(businessObject),
    message: messageOf(businessObject),
    sendsMessage: sendsMessage(businessObject),
    signalName: signalOf(businessObject),
    error: errorOf(businessObject),
    calledProcess: calledProcessOf(businessObject),
    script: scriptOf(businessObject),
    isScriptTask: type === 'bpmn:ScriptTask',
    multiInstance: multiInstanceOf(businessObject),
  };
}

function assignmentModeOf(assignment: ModdleElement | undefined): AssignmentMode {
  if (!assignment) return 'text';
  const mode = text(assignment, 'mode');
  if (mode === 'text' || mode === 'directory') return mode;
  return 'invalid';
}

function commaSeparated(value: string): string[] {
  return value
    .split(',')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}
