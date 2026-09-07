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
  timerOf,
  type Assignment,
  type EmbeddedForm,
  type CalledProcess,
  type ElementProperties,
  type IoMapping,
  type MultiInstance,
  type Schedule,
  type ScriptDefinition,
  type TimerDefinition,
  type MessageReference,
  type UserTaskReference,
} from './elementProperties';
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

    /** Setzt den Formularverweis der Aufgabe; `null` entfernt ihn. */
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

    /** Alle menschlichen Aufgaben des Diagramms — für Übersicht und Markierung. */
    listUserTasks(): UserTaskReference[] {
      return registry()
        .filter((element) => element.businessObject?.$type === 'bpmn:UserTask')
        .map((element) => {
          const formDefinition = extension(element.businessObject, 'zeebe:FormDefinition');
          const formKey = text(formDefinition, 'formKey') || text(formDefinition, 'formId');
          return {
            id: element.id,
            name: text(element.businessObject, 'name').trim() || element.id,
            formKey: formKey.length > 0 ? formKey : null,
          };
        });
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

/**
 * Der Wert, der geschrieben wird: der neue, wenn einer kam, sonst der bisherige. Leer heisst
 * „Attribut weglassen" — ein leerer Text waere im BPMN eine Angabe und kein Nichts.
 */
function merge(patched: string | undefined, current: string): string | undefined {
  const value = (patched ?? current).trim();
  return value.length > 0 ? value : undefined;
}
