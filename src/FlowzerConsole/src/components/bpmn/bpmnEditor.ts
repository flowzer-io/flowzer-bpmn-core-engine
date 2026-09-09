/**
 * Schmale Schicht zwischen bpmn-js und dem Eigenschaften-Panel der Konsole.
 *
 * Nur diese Datei schreibt ins BPMN-Objektmodell. Das Panel arbeitet ausschließlich mit den
 * flachen Werten aus {@link ElementProperties} — so bleibt die Oberfläche von der
 * Modellbibliothek getrennt und die Begriffe der Konsole (Formular, Zuweisung, Frist) haben
 * genau eine Stelle, an der sie ins BPMN übersetzt werden.
 *
 * Der Umfang folgt dem, was `src/core-engine/ModelParser.cs` liest. Kommt dort eine
 * Eigenschaft dazu, gehört sie auch hierher — sonst lässt sich modellieren, was nicht läuft.
 *
 * Die Schreiber nehmen **Teiländerungen** und mischen sie mit dem aktuellen Modellstand. Das
 * ist kein Komfort, sondern nötig: Ein Textfeld schreibt erst beim Verlassen. Klickt jemand
 * aus einem Feld heraus direkt auf einen Schalter derselben Gruppe, laufen beide Schreiber
 * nacheinander — der zweite mit den Werten aus dem Bild *vor* dem ersten. Gäbe er die ganze
 * Gruppe mit, machte er die eben getippte Eingabe wieder zunichte.
 */

import {
  calledProcessOf,
  messageHolder,
  multiInstanceOf,
  readElementProperties,
  startFormAppliesTo,
  timerOf,
  type Assignment,
  type AssignmentMode,
  type DirectoryAssignment,
  type EmbeddedForm,
  type CalledProcess,
  type ElementProperties,
  type IoMapping,
  type MultiInstance,
  type Schedule,
  type ScriptDefinition,
  type TimerDefinition,
  type MessageReference,
  type FormOwner,
} from './elementProperties';
import {
  AI_WORKER_TYPE,
  DEFAULT_AI_TASK,
  type AiTaskConfiguration,
  type ServiceTaskMode,
} from '@/lib/aiTaskContract';
import {
  enclosing,
  eventDefinition,
  expressionBody,
  extension,
  extensionValues,
  text,
  type BpmnFactoryLike,
  type DiagramElement,
  type ElementRegistryLike,
  type ModdleElement,
  type ModelingLike,
} from './moddle';

export type { DiagramElement } from './moddle';
export * from './elementProperties';

interface ModelerLike {
  get: <T>(name: string) => T;
}

/** Ein Prozess samt dem Diagrammelement, über das bpmn-js die Änderung verbucht. */
interface ProcessScope {
  element: DiagramElement;
  businessObject: ModdleElement;
}

/**
 * Erzeugt den Zugriff auf ein laufendes bpmn-js-Modell.
 *
 * Bewusst eine Funktion über der Modeler-Instanz statt einer Klasse: Der Modeler lebt in
 * einer Ref, und jede Panel-Aktion holt sich die Dienste frisch — so bleibt nichts über
 * einen Neuaufbau des Modelers hinweg stehen.
 */
export function createBpmnEditor(modeler: ModelerLike) {
  const registry = () => modeler.get<ElementRegistryLike>('elementRegistry');
  const modeling = () => modeler.get<ModelingLike>('modeling');
  const factory = () => modeler.get<BpmnFactoryLike>('bpmnFactory');

  /**
   * Alle Prozesse des Diagramms. In einer Kollaboration steckt jeder hinter seinem Teilnehmer;
   * ohne diesen Umweg blieben die Formulare der übrigen Pools unsichtbar.
   *
   * Bewusst über die Elementliste statt über `canvas.getRootElement()`: Letzteres legt eine
   * Wurzel an, wenn noch keine da ist. Das Panel liest während des Zeichnens — ein Aufruf,
   * der dabei das Modell verändert, löst mitten im Rendern Ereignisse und damit
   * Zustandsänderungen aus.
   */
  function processes(): ProcessScope[] {
    const direct = registry()
      .filter((element) => element.businessObject?.$type === 'bpmn:Process')
      .map((element) => ({ element, businessObject: element.businessObject }));

    if (direct.length > 0) return direct;

    return registry()
      .filter((element) => element.businessObject?.$type === 'bpmn:Participant')
      .filter((element) => Boolean(element.businessObject.processRef))
      .map((element) => ({
        element,
        businessObject: element.businessObject.processRef as ModdleElement,
      }));
  }

  /**
   * Der Prozess, zu dem ein Element gehört. Ein Formular gehört zu dem Prozess, in dem die
   * Aufgabe liegt — in einer Kollaboration mit zwei Pools wäre der erstbeste Prozess der
   * falsche, und ein Export nur dieses Pools verlöre das Formular.
   */
  function processOf(elementId: string): ProcessScope | null {
    const element = registry().get(elementId);
    if (!element) return null;

    const businessObject = enclosing(element.businessObject, 'bpmn:Process');
    return businessObject ? { element, businessObject } : null;
  }

  /** Der Prozess, in dem ein bestimmtes eingebettetes Formular schon liegt. */
  function processHoldingForm(formId: string): ProcessScope | null {
    return (
      processes().find((scope) =>
        extensionValues(scope.businessObject).some(
          (value) => value.$type === 'zeebe:UserTaskForm' && value.id === formId,
        ),
      ) ?? null
    );
  }

  /** Legt `bpmn:extensionElements` an, falls der Träger noch keine hat. */
  function ensureExtensionElements(element: DiagramElement, owner: ModdleElement): ModdleElement {
    const existing = owner.extensionElements as ModdleElement | undefined;
    if (existing) return existing;

    const created = factory().create('bpmn:ExtensionElements', { values: [] });
    created.$parent = owner;
    modeling().updateModdleProperties(element, owner, { extensionElements: created });
    return created;
  }

  /**
   * Setzt genau eine Erweiterung eines Typs an einem Träger. `null` entfernt sie — leere
   * Attribute stehen zu lassen wäre kein Nichts, sondern ein leerer Wert, den die Engine
   * wieder auswerten müsste.
   */
  function writeExtension(
    element: DiagramElement,
    owner: ModdleElement,
    type: string,
    properties: Record<string, unknown> | null,
  ): void {
    const existing = extension(owner, type);

    if (properties === null) {
      if (!existing) return;
      const container = owner.extensionElements as ModdleElement;
      const values = (container.values as ModdleElement[]).filter((value) => value !== existing);
      modeling().updateModdleProperties(element, container, { values });

      // Ein leeres `<bpmn:extensionElements />` bleibt sonst als Rest im Diagramm stehen.
      if (values.length === 0) {
        modeling().updateModdleProperties(element, owner, { extensionElements: undefined });
      }
      return;
    }

    if (existing) {
      modeling().updateModdleProperties(element, existing, properties);
      return;
    }

    const container = ensureExtensionElements(element, owner);
    const created = factory().create(type, properties);
    created.$parent = container;
    modeling().updateModdleProperties(element, container, {
      values: [...((container.values as ModdleElement[] | undefined) ?? []), created],
    });
  }

  function conditionOf(element: DiagramElement): string {
    return expressionBody(element.businessObject, 'conditionExpression');
  }

  /** Ein `bpmn:FormalExpression` unterhalb eines Trägers. */
  function formalExpression(body: string, owner: ModdleElement): ModdleElement {
    const created = factory().create('bpmn:FormalExpression', { body });
    created.$parent = owner;
    return created;
  }

  /**
   * Legt ein Wurzelelement an, auf das ein Ereignis verweist — eine Nachricht oder ein Signal.
   * Beide leben nicht am Element, sondern neben den Prozessen im Dokument.
   */
  function createRootReference(
    element: DiagramElement,
    holder: ModdleElement,
    referenceProperty: 'messageRef' | 'signalRef',
    type: string,
    name: string,
  ): ModdleElement | null {
    const definitions = enclosing(element.businessObject, 'bpmn:Definitions');
    if (!definitions) return null;

    const rootElements = (definitions.rootElements as ModdleElement[] | undefined) ?? [];
    // Die Kennung vergibt die Factory: Sie zieht sie aus dem Kennungsregister des Dokuments
    // und belegt sie dort. Eine selbst gewuerfelte koennte mit einer anderen kollidieren.
    const created = factory().create(type, { name });
    created.$parent = definitions;

    modeling().updateModdleProperties(element, definitions, { rootElements: [...rootElements, created] });
    modeling().updateModdleProperties(element, holder, { [referenceProperty]: created });
    return created;
  }

  /** Legt die verschachtelte Flowzer-Erweiterung mit korrekten Moddle-Elternbeziehungen an. */
  function ensureAiTaskExtension(element: DiagramElement): ModdleElement {
    const existing = extension(element.businessObject, 'flowzer:AiTask');
    if (existing) return existing;

    const container = ensureExtensionElements(element, element.businessObject);
    const contract = factory().create('flowzer:AiTask', {
      ...DEFAULT_AI_TASK,
      connectionId: undefined,
      model: undefined,
      instruction: undefined,
      resultSchema: undefined,
      tools: undefined,
    });
    const instruction = factory().create('flowzer:Instruction', { body: DEFAULT_AI_TASK.instruction });
    const resultSchema = factory().create('flowzer:ResultSchema', { body: DEFAULT_AI_TASK.resultSchema });
    contract.$parent = container;
    instruction.$parent = contract;
    resultSchema.$parent = contract;
    contract.instruction = instruction;
    contract.resultSchema = resultSchema;
    modeling().updateModdleProperties(element, container, {
      values: [...((container.values as ModdleElement[] | undefined) ?? []), contract],
    });
    return contract;
  }

  function setServiceTaskMode(element: DiagramElement, mode: ServiceTaskMode): void {
    if (mode === 'worker') {
      writeExtension(element, element.businessObject, 'flowzer:AiTask', null);
      const taskDefinition = extension(element.businessObject, 'zeebe:TaskDefinition');
      if (text(taskDefinition, 'type') === AI_WORKER_TYPE) {
        const retries = text(taskDefinition, 'retries');
        writeExtension(
          element,
          element.businessObject,
          'zeebe:TaskDefinition',
          retries ? { retries } : null,
        );
      }
      return;
    }

    const taskDefinition = extension(element.businessObject, 'zeebe:TaskDefinition');
    writeExtension(element, element.businessObject, 'zeebe:TaskDefinition', {
      type: AI_WORKER_TYPE,
      retries: merge(undefined, text(taskDefinition, 'retries')),
    });
    ensureAiTaskExtension(element);
  }

  return {
    /** Liest alle Werte, die das Panel für ein Element anzeigt. */
    read(elementId: string): ElementProperties | null {
      const element = registry().get(elementId);
      return element ? readElementProperties(element) : null;
    },

    setName(elementId: string, name: string): void {
      const element = registry().get(elementId);
      if (!element) return;
      modeling().updateProperties(element, { name: name.trim() });
    },

    /** Setzt den Formularverweis einer Aufgabe oder des Startereignisses; `null` entfernt ihn. */
    setFormKey(elementId: string, formKey: string | null): void {
      const element = registry().get(elementId);
      if (!element) return;

      const trimmed = formKey?.trim();
      if (!trimmed) {
        writeExtension(element, element.businessObject, 'zeebe:FormDefinition', null);
        return;
      }

      // `formId`, `externalReference` und `formKey` bedeuten dasselbe Ziel; stünden mehrere da,
      // entschiede die Lesereihenfolge der Engine, welches Formular gilt.
      writeExtension(element, element.businessObject, 'zeebe:FormDefinition', {
        formKey: trimmed,
        formId: undefined,
        externalReference: undefined,
      });
    },

    setAssignment(elementId: string, patch: Partial<Assignment>): void {
      const element = registry().get(elementId);
      if (!element) return;

      const current = extension(element.businessObject, 'zeebe:AssignmentDefinition');
      const values = {
        assignee: merge(patch.assignee, text(current, 'assignee')),
        candidateGroups: merge(patch.candidateGroups, text(current, 'candidateGroups')),
        candidateUsers: merge(patch.candidateUsers, text(current, 'candidateUsers')),
      };
      const isEmpty = Object.values(values).every((value) => value === undefined);
      writeExtension(element, element.businessObject, 'zeebe:AssignmentDefinition', isEmpty ? null : values);
    },

    /**
     * Wechselt den Vertrag ohne eine nicht deploybare leere Directory-Zuweisung zu erzeugen.
     * Der Wechsel in den Directory-Modus wird erst mit der ersten konkreten Auswahl persistiert.
     */
    setAssignmentMode(elementId: string, mode: Exclude<AssignmentMode, 'invalid'>): void {
      const element = registry().get(elementId);
      if (!element) return;

      if (mode === 'directory') {
        const current = extension(element.businessObject, 'flowzer:TaskAssignment');
        if (text(current, 'mode') !== 'directory' || !hasDirectoryReference(current)) return;
        writeExtension(element, element.businessObject, 'zeebe:AssignmentDefinition', null);
        return;
      }

      writeExtension(element, element.businessObject, 'flowzer:TaskAssignment', {
        mode: 'text',
        assigneeId: undefined,
        candidateUserIds: undefined,
        candidateGroupIds: undefined,
      });
    },

    /** Schreibt ausschließlich stabile IDs; Anzeigenamen gehören nie in den BPMN-Vertrag. */
    setDirectoryAssignment(elementId: string, patch: Partial<DirectoryAssignment>): void {
      const element = registry().get(elementId);
      if (!element) return;

      const current = extension(element.businessObject, 'flowzer:TaskAssignment');
      const currentDirectory = text(current, 'mode') === 'directory' ? current : undefined;
      const assigneeId = normalizeId(patch.assigneeId ?? text(currentDirectory, 'assigneeId'));
      const candidateUserIds = normalizeIds(
        patch.candidateUserIds ?? commaSeparatedIds(text(currentDirectory, 'candidateUserIds')),
      );
      const candidateGroupIds = normalizeIds(
        patch.candidateGroupIds ?? commaSeparatedIds(text(currentDirectory, 'candidateGroupIds')),
      );

      writeExtension(element, element.businessObject, 'zeebe:AssignmentDefinition', null);
      if (!assigneeId && candidateUserIds.length === 0 && candidateGroupIds.length === 0) {
        writeExtension(element, element.businessObject, 'flowzer:TaskAssignment', null);
        return;
      }

      writeExtension(element, element.businessObject, 'flowzer:TaskAssignment', {
        mode: 'directory',
        assigneeId: assigneeId || undefined,
        candidateUserIds: candidateUserIds.length > 0 ? candidateUserIds.join(',') : undefined,
        candidateGroupIds: candidateGroupIds.length > 0 ? candidateGroupIds.join(',') : undefined,
      });
    },

    setSchedule(elementId: string, patch: Partial<Schedule>): void {
      const element = registry().get(elementId);
      if (!element) return;

      const current = extension(element.businessObject, 'zeebe:TaskSchedule');
      const values = {
        dueDate: merge(patch.dueDate, text(current, 'dueDate')),
        followUpDate: merge(patch.followUpDate, text(current, 'followUpDate')),
      };
      const isEmpty = Object.values(values).every((value) => value === undefined);
      writeExtension(element, element.businessObject, 'zeebe:TaskSchedule', isEmpty ? null : values);
    },

    setJob(elementId: string, patch: { type?: string; retries?: string }): void {
      const element = registry().get(elementId);
      if (!element) return;

      const current = extension(element.businessObject, 'zeebe:TaskDefinition');
      const type = merge(patch.type, text(current, 'type'));
      const attempts = merge(patch.retries, text(current, 'retries'));

      // Ein `type=""` waere kein fehlender Auftragstyp, sondern ein leerer: Der Workflow liesse
      // sich speichern, und zur Laufzeit fände kein Worker den Schritt. Ohne Angabe wird das
      // Attribut deshalb weggelassen — dann meldet schon das Speichern den fehlenden Typ.
      if (type === undefined && attempts === undefined) {
        writeExtension(element, element.businessObject, 'zeebe:TaskDefinition', null);
        return;
      }

      writeExtension(element, element.businessObject, 'zeebe:TaskDefinition', { type, retries: attempts });
    },

    /** Wechselt einen Standard-Service-Task zwischen freiem Worker und Flowzer-KI-Vertrag. */
    setServiceTaskMode(elementId: string, mode: ServiceTaskMode): void {
      const element = registry().get(elementId);
      if (!element || element.businessObject.$type !== 'bpmn:ServiceTask') return;
      setServiceTaskMode(element, mode);
    },

    /** Schreibt Teiländerungen des KI-Vertrags, ohne Prompt oder Schema beim Fokuswechsel zu verlieren. */
    setAiTask(elementId: string, patch: Partial<AiTaskConfiguration>): void {
      const element = registry().get(elementId);
      if (!element || element.businessObject.$type !== 'bpmn:ServiceTask') return;
      setServiceTaskMode(element, 'ai');
      const current = ensureAiTaskExtension(element);

      const attributeNames = [
        'contractVersion',
        'connectionId',
        'model',
        'instructionVersion',
        'maxInputTokens',
        'maxOutputTokens',
        'timeoutSeconds',
      ] as const;
      const attributes = Object.fromEntries(
        attributeNames.map((name) => [name, merge(patch[name], text(current, name))]),
      );
      modeling().updateModdleProperties(element, current, attributes);

      for (const [property, type] of [
        ['instruction', 'flowzer:Instruction'],
        ['resultSchema', 'flowzer:ResultSchema'],
      ] as const) {
        let child = current[property] as ModdleElement | undefined;
        if (!child) {
          child = factory().create(type, { body: patch[property] ?? '' });
          child.$parent = current;
          modeling().updateModdleProperties(element, current, { [property]: child });
        } else if (patch[property] !== undefined) {
          modeling().updateModdleProperties(element, child, { body: patch[property].trim() });
        }
      }

      if (patch.tools !== undefined) {
        const tools = patch.tools.map((tool) => {
          const child = factory().create('flowzer:Tool', {
            id: tool.toolId,
            version: tool.toolVersion,
            approval: tool.approval,
          });
          child.$parent = current;
          return child;
        });
        modeling().updateModdleProperties(element, current, { tools });
      }
    },

    /** Schreibt Ein- und Ausgangszuordnungen als ein `zeebe:ioMapping`. */
    setIoMappings(elementId: string, inputs: IoMapping[], outputs: IoMapping[]): void {
      const element = registry().get(elementId);
      if (!element) return;

      // Ein halbes Paar hat keine Bedeutung: Ohne Ziel wuesste die Engine nicht, wohin der
      // Wert soll, ohne Quelle nicht, woher er kommt.
      const usable = (mapping: IoMapping) => mapping.source.trim().length > 0 && mapping.target.trim().length > 0;
      const keptInputs = inputs.filter(usable);
      const keptOutputs = outputs.filter(usable);

      if (keptInputs.length === 0 && keptOutputs.length === 0) {
        writeExtension(element, element.businessObject, 'zeebe:IoMapping', null);
        return;
      }

      const container = ensureExtensionElements(element, element.businessObject);
      let mapping = extension(element.businessObject, 'zeebe:IoMapping');

      if (!mapping) {
        mapping = factory().create('zeebe:IoMapping', {});
        mapping.$parent = container;
        modeling().updateModdleProperties(element, container, {
          values: [...((container.values as ModdleElement[] | undefined) ?? []), mapping],
        });
      }

      const build = (type: string, entries: IoMapping[]) =>
        entries.map((entry) => {
          const created = factory().create(type, {
            source: entry.source.trim(),
            target: entry.target.trim(),
          });
          created.$parent = mapping!;
          return created;
        });

      modeling().updateModdleProperties(element, mapping, {
        inputParameters: build('zeebe:Input', keptInputs),
        outputParameters: build('zeebe:Output', keptOutputs),
      });
    },

    /**
     * Setzt die Bedingung eines Sequenzflusses. Ein Fluss mit Bedingung kann nicht zugleich
     * der Standardfluss sein — sonst stünde im Diagramm eine Regel, die nie zur Anwendung käme.
     */
    setCondition(flowId: string, condition: string): void {
      const flow = registry().get(flowId);
      if (!flow) return;

      const body = condition.trim();
      const source = flow.source;

      if (body.length > 0 && source && source.businessObject.default === flow.businessObject) {
        modeling().updateProperties(source, { default: undefined });
      }

      if (body.length === 0) {
        modeling().updateProperties(flow, { conditionExpression: undefined });
        return;
      }

      modeling().updateProperties(flow, {
        conditionExpression: formalExpression(body, flow.businessObject),
      });
    },

    /** Legt fest, welcher ausgehende Fluss greift, wenn keine Bedingung zutrifft. */
    setDefaultFlow(elementId: string, flowId: string | null): void {
      const element = registry().get(elementId);
      if (!element) return;

      if (flowId === null) {
        modeling().updateProperties(element, { default: undefined });
        return;
      }

      const flow = registry().get(flowId);
      if (!flow) return;

      // Der Standardfluss ist der Weg ohne Bedingung; eine vorhandene würde ihn widerlegen.
      if (conditionOf(flow).length > 0) {
        modeling().updateProperties(flow, { conditionExpression: undefined });
      }

      modeling().updateProperties(element, { default: flow.businessObject });
    },

    /**
     * Setzt die Zeitangabe eines Timers. Die beiden anderen Arten werden dabei entfernt: Stehen
     * mehrere im Diagramm, entscheidet die Lesereihenfolge der Engine, welche gilt.
     */
    setTimer(elementId: string, patch: Partial<TimerDefinition>): void {
      const element = registry().get(elementId);
      if (!element) return;

      const definition = eventDefinition(element.businessObject, 'bpmn:TimerEventDefinition');
      if (!definition) return;

      const current = timerOf(element.businessObject);
      const kind = patch.kind ?? current?.kind ?? 'duration';
      const body = (patch.expression ?? current?.expression ?? '').trim();
      const value = body.length > 0 ? formalExpression(body, definition) : undefined;

      modeling().updateModdleProperties(element, definition, {
        timeDuration: kind === 'duration' ? value : undefined,
        timeDate: kind === 'date' ? value : undefined,
        timeCycle: kind === 'cycle' ? value : undefined,
      });
    },

    /**
     * Setzt Name und Korrelationsschlüssel der Nachricht. Die Nachricht selbst ist ein
     * Wurzelelement des Dokuments; fehlt sie noch, entsteht sie hier.
     */
    setMessage(elementId: string, patch: Partial<MessageReference>): void {
      const element = registry().get(elementId);
      if (!element) return;

      const holder = messageHolder(element.businessObject);
      if (!holder) return;

      let message = holder.messageRef as ModdleElement | undefined;
      const name = (patch.name ?? text(message, 'name')).trim();

      if (message) {
        modeling().updateModdleProperties(element, message, { name });
      } else {
        message = createRootReference(element, holder, 'messageRef', 'bpmn:Message', name) ?? undefined;
        if (!message) return;
      }

      const key = merge(patch.correlationKey, text(extension(message, 'zeebe:Subscription'), 'correlationKey'));
      writeExtension(element, message, 'zeebe:Subscription', key === undefined ? null : { correlationKey: key });
    },

    /** Setzt den Namen des Signals. Auch das Signal ist ein Wurzelelement des Dokuments. */
    setSignal(elementId: string, name: string): void {
      const element = registry().get(elementId);
      if (!element) return;

      const definition = eventDefinition(element.businessObject, 'bpmn:SignalEventDefinition');
      if (!definition) return;

      const trimmedName = name.trim();
      const signal = definition.signalRef as ModdleElement | undefined;

      if (signal) {
        modeling().updateModdleProperties(element, signal, { name: trimmedName });
        return;
      }

      createRootReference(element, definition, 'signalRef', 'bpmn:Signal', trimmedName);
    },

    setCalledProcess(elementId: string, patch: Partial<CalledProcess>): void {
      const element = registry().get(elementId);
      if (!element) return;

      const current = calledProcessOf(element.businessObject);
      const processId = merge(patch.processId, current?.processId ?? '');

      // Der Parser liest `processId` als Pflichtangabe. Eine Erweiterung ohne sie liesse den
      // Aufruf am Server in einen Nullverweis laufen — ein 500 statt einer Meldung. Ohne
      // Kennung steht die Erweiterung deshalb gar nicht erst da.
      if (processId === undefined) {
        writeExtension(element, element.businessObject, 'zeebe:CalledElement', null);
        return;
      }

      writeExtension(element, element.businessObject, 'zeebe:CalledElement', {
        processId,
        propagateAllChildVariables:
          patch.propagateAllChildVariables ?? current?.propagateAllChildVariables ?? true,
        propagateAllParentVariables:
          patch.propagateAllParentVariables ?? current?.propagateAllParentVariables ?? true,
      });
    },

    /**
     * Legt fest, ob eine Skript-Aufgabe als FEEL-Ausdruck in der Engine läuft oder als Auftrag
     * an einen Worker geht. Beides zugleich gäbe es im Modell nicht: Die Engine nimmt das
     * Skript, sobald eines da ist.
     */
    setScriptMode(elementId: string, mode: 'script' | 'job'): void {
      const element = registry().get(elementId);
      if (!element) return;

      if (mode === 'script') {
        writeExtension(element, element.businessObject, 'zeebe:TaskDefinition', null);
        if (!extension(element.businessObject, 'zeebe:Script')) {
          writeExtension(element, element.businessObject, 'zeebe:Script', {});
        }
        return;
      }

      writeExtension(element, element.businessObject, 'zeebe:Script', null);
    },

    setScript(elementId: string, patch: Partial<ScriptDefinition>): void {
      const element = registry().get(elementId);
      if (!element) return;

      const current = extension(element.businessObject, 'zeebe:Script');
      writeExtension(element, element.businessObject, 'zeebe:Script', {
        expression: merge(patch.expression, text(current, 'expression')),
        resultVariable: merge(patch.resultVariable, text(current, 'resultVariable')),
      });
    },

    /**
     * Schreibt die Angaben zur Mehrfachausführung. Ob sie sequenziell oder parallel läuft,
     * bestimmt die Elementart im Diagramm und nicht dieses Panel.
     */
    setMultiInstance(elementId: string, patch: Partial<Omit<MultiInstance, 'isSequential'>>): void {
      const element = registry().get(elementId);
      if (!element) return;

      const loop = element.businessObject.loopCharacteristics as ModdleElement | undefined;
      if (loop?.$type !== 'bpmn:MultiInstanceLoopCharacteristics') return;

      const current = multiInstanceOf(element.businessObject)!;
      const condition = (patch.completionCondition ?? current.completionCondition).trim();
      modeling().updateModdleProperties(element, loop, {
        completionCondition: condition.length > 0 ? formalExpression(condition, loop) : undefined,
      });

      const zeebeValues = {
        inputCollection: merge(patch.inputCollection, current.inputCollection),
        inputElement: merge(patch.inputElement, current.inputElement),
        outputCollection: merge(patch.outputCollection, current.outputCollection),
        outputElement: merge(patch.outputElement, current.outputElement),
      };
      const isEmpty = Object.values(zeebeValues).every((value) => value === undefined);
      writeExtension(element, loop, 'zeebe:LoopCharacteristics', isEmpty ? null : zeebeValues);
    },

    /**
     * Alle Elemente des Diagramms, die auf ein Formular zeigen — für die Übersicht und die
     * Markierung im Diagramm.
     *
     * Menschliche Aufgaben stehen immer darin, auch ohne Formular: Dort ist ein fehlender
     * Verweis ein Mangel. Ein Startereignis steht nur darin, wenn es ein Startformular trägt —
     * ohne eines startet der Workflow direkt, und das ist der Normalfall. Startereignisse mit
     * Zeit-, Nachrichten- oder Signaldefinition bleiben aussen vor: Dort liest die Engine
     * keinen Form-Key.
     */
    listFormOwners(): FormOwner[] {
      return registry()
        .filter((element) => {
          // bpmn-js fuehrt die Beschriftung eines benannten Ereignisses als eigenes Element mit
          // demselben Objekt. Ohne diesen Ausschluss stuende jedes benannte Startereignis
          // zweimal in der Liste — und traege zwei Markierungen im Diagramm.
          if (element.labelTarget) return false;

          const type = element.businessObject?.$type;
          if (type === 'bpmn:UserTask') return true;
          return startFormAppliesTo(element.businessObject) && formKeyOf(element.businessObject) !== null;
        })
        .map((element) => ({
          id: element.id,
          name: text(element.businessObject, 'name').trim() || element.id,
          kind: element.businessObject.$type === 'bpmn:UserTask' ? ('userTask' as const) : ('startEvent' as const),
          formKey: formKeyOf(element.businessObject),
        }));
    },

    /** Die Formulare, die der Workflow selbst mitbringt — über alle Prozesse des Diagramms. */
    listEmbeddedForms(): EmbeddedForm[] {
      return processes()
        .flatMap((scope) => extensionValues(scope.businessObject))
        .filter((value) => value.$type === 'zeebe:UserTaskForm')
        .map((value) => ({ id: text(value, 'id'), schema: text(value, 'body') }))
        .filter((form) => form.id.length > 0);
    },

    /**
     * Legt ein Formular im Workflow an oder ersetzt sein Schema.
     *
     * `contextElementId` benennt die Aufgabe, zu der das Formular gehört: Ein neues Formular
     * entsteht in deren Prozess. Ein vorhandenes wird dort geändert, wo es schon liegt.
     */
    saveEmbeddedForm(formId: string, schema: string, contextElementId?: string): void {
      const existingScope = processHoldingForm(formId);

      if (existingScope) {
        const existing = extensionValues(existingScope.businessObject).find(
          (value) => value.$type === 'zeebe:UserTaskForm' && value.id === formId,
        )!;
        modeling().updateModdleProperties(existingScope.element, existing, { body: schema });
        return;
      }

      const scope = (contextElementId ? processOf(contextElementId) : null) ?? processes()[0];
      if (!scope) return;

      const container = ensureExtensionElements(scope.element, scope.businessObject);
      const created = factory().create('zeebe:UserTaskForm', { id: formId, body: schema });
      created.$parent = container;
      modeling().updateModdleProperties(scope.element, container, {
        values: [...((container.values as ModdleElement[] | undefined) ?? []), created],
      });
    },

    removeEmbeddedForm(formId: string): void {
      const scope = processHoldingForm(formId);
      if (!scope) return;

      const container = scope.businessObject.extensionElements as ModdleElement;
      const values = (container.values as ModdleElement[]).filter(
        (value) => !(value.$type === 'zeebe:UserTaskForm' && value.id === formId),
      );
      modeling().updateModdleProperties(scope.element, container, { values });
    },
  };
}

export type BpmnEditor = ReturnType<typeof createBpmnEditor>;

/** Der Formularverweis eines Elements — dieselbe Lesereihenfolge wie im Parser. */
function formKeyOf(businessObject: ModdleElement): string | null {
  const formDefinition = extension(businessObject, 'zeebe:FormDefinition');
  const formKey = text(formDefinition, 'formKey') || text(formDefinition, 'formId');
  return formKey.length > 0 ? formKey : null;
}

/**
 * Der Wert, der geschrieben wird: der neue, wenn einer kam, sonst der bisherige. Leer heisst
 * „Attribut weglassen" — ein leerer Text waere im BPMN eine Angabe und kein Nichts.
 */
function merge(patched: string | undefined, current: string): string | undefined {
  const value = (patched ?? current).trim();
  return value.length > 0 ? value : undefined;
}

function commaSeparatedIds(value: string): string[] {
  return value
    .split(',')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

function normalizeId(value: string): string {
  return value.trim().toLowerCase();
}

/** UUIDs werden kanonisch, eindeutig und stabil sortiert geschrieben. */
function normalizeIds(values: string[]): string[] {
  return [...new Set(values.map(normalizeId).filter((value) => value.length > 0))].sort();
}

function hasDirectoryReference(assignment: ModdleElement | undefined): boolean {
  return Boolean(
    normalizeId(text(assignment, 'assigneeId'))
      || commaSeparatedIds(text(assignment, 'candidateUserIds')).length > 0
      || commaSeparatedIds(text(assignment, 'candidateGroupIds')).length > 0,
  );
}
