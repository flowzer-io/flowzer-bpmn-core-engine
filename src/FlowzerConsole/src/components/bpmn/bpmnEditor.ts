/**
 * Schmale Schicht zwischen bpmn-js und dem Eigenschaften-Panel der Konsole.
 *
 * Nur diese Datei kennt Moddle-Elemente, `extensionElements` und die Befehle von bpmn-js.
 * Das Panel arbeitet ausschließlich mit den flachen Werten aus {@link ElementProperties} —
 * so bleibt die Oberfläche von der Modellbibliothek getrennt und die Begriffe der Konsole
 * (Formular, Zuweisung, Frist) haben genau eine Stelle, an der sie ins BPMN übersetzt werden.
 */

/** Ein Moddle-Element (BPMN-Objektmodell von bpmn-js). Die Felder sind bewusst offen. */
interface ModdleElement {
  $type: string;
  $parent?: ModdleElement;
  [key: string]: unknown;
}

/** Ein Element auf der Zeichenfläche. */
export interface DiagramElement {
  id: string;
  type: string;
  businessObject: ModdleElement;
  source?: DiagramElement;
  target?: DiagramElement;
  outgoing?: DiagramElement[];
}

interface ElementRegistryLike {
  get: (id: string) => DiagramElement | undefined;
  filter: (predicate: (element: DiagramElement) => boolean) => DiagramElement[];
}

interface ModelingLike {
  updateProperties: (element: DiagramElement, properties: Record<string, unknown>) => void;
  updateModdleProperties: (
    element: DiagramElement,
    moddleElement: ModdleElement,
    properties: Record<string, unknown>,
  ) => void;
}

interface BpmnFactoryLike {
  create: (type: string, properties?: Record<string, unknown>) => ModdleElement;
}

interface ModelerLike {
  get: <T>(name: string) => T;
}

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

/** Ein Prozess samt dem Diagrammelement, über das bpmn-js die Änderung verbucht. */
interface ProcessScope {
  element: DiagramElement;
  businessObject: ModdleElement;
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

  /** Auftrag an einen externen Worker (Service-Task). */
  jobType: string;
  retries: string;

  /** Zuordnungen zwischen Prozess- und Aufgabendaten. */
  inputs: IoMapping[];
  outputs: IoMapping[];

  /** Sequenzfluss. */
  condition: string;
  isDefaultFlow: boolean;
  /** Nur an Toren und Aktivitäten wertet die Engine eine Bedingung aus. */
  conditionApplies: boolean;

  /** Tor: die ausgehenden Flüsse mit ihren Bedingungen. */
  outgoing: OutgoingFlow[];

  /**
   * Eigenschaften, die die Engine an diesem Element auswertet, die das Panel aber nicht
   * bearbeitet. Sie zu verschweigen wäre der Fehler: Das Element sähe fertig aus.
   */
  uncovered: string[];
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

const CONDITIONAL_SOURCES = ['bpmn:ExclusiveGateway', 'bpmn:InclusiveGateway', 'bpmn:Activity'];
const GATEWAY_TYPES = ['bpmn:ExclusiveGateway', 'bpmn:InclusiveGateway'];

/**
 * Was die Engine liest, das Panel aber nicht anbietet. Die Liste ist bewusst hier und nicht
 * in der Oberfläche: Sie gehört zu dem, was der Parser auswertet, und muss mit ihm wachsen.
 */
const UNCOVERED_PROPERTIES: { applies: (businessObject: ModdleElement) => boolean; label: string }[] = [
  {
    applies: (businessObject) => hasEventDefinition(businessObject, 'bpmn:TimerEventDefinition'),
    label: 'Zeitangabe des Timers (Dauer, Zeitpunkt oder Zyklus)',
  },
  {
    applies: (businessObject) =>
      hasEventDefinition(businessObject, 'bpmn:MessageEventDefinition') ||
      Boolean(businessObject.messageRef),
    label: 'Nachricht und Korrelationsschlüssel',
  },
  {
    applies: (businessObject) => hasEventDefinition(businessObject, 'bpmn:SignalEventDefinition'),
    label: 'Signal',
  },
  {
    applies: (businessObject) => businessObject.$type === 'bpmn:CallActivity',
    label: 'aufgerufener Prozess',
  },
  {
    applies: (businessObject) => businessObject.$type === 'bpmn:ScriptTask',
    label: 'Ausdruck des Skripts',
  },
  {
    applies: (businessObject) => Boolean(businessObject.loopCharacteristics),
    label: 'Mehrfachausführung',
  },
];

function hasEventDefinition(businessObject: ModdleElement, type: string): boolean {
  const definitions = (businessObject.eventDefinitions as ModdleElement[] | undefined) ?? [];
  return definitions.some((definition) => definition.$type === type);
}

function uncoveredProperties(businessObject: ModdleElement | undefined): string[] {
  if (!businessObject) return [];
  return UNCOVERED_PROPERTIES.filter((entry) => entry.applies(businessObject)).map((entry) => entry.label);
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

    let candidate: ModdleElement | undefined = element.businessObject;
    while (candidate && candidate.$type !== 'bpmn:Process') {
      candidate = candidate.$parent;
    }

    return candidate ? { element, businessObject: candidate } : null;
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

  function extensionValues(businessObject: ModdleElement | undefined): ModdleElement[] {
    const container = businessObject?.extensionElements as ModdleElement | undefined;
    return (container?.values as ModdleElement[] | undefined) ?? [];
  }

  function extension(businessObject: ModdleElement | undefined, type: string): ModdleElement | undefined {
    return extensionValues(businessObject).find((value) => value.$type === type);
  }

  function text(moddleElement: ModdleElement | undefined, attribute: string): string {
    const value = moddleElement?.[attribute];
    return typeof value === 'string' ? value : '';
  }

  /** Legt `bpmn:extensionElements` an, falls das Element noch keine hat. */
  function ensureExtensionElements(element: DiagramElement): ModdleElement {
    const businessObject = element.businessObject;
    const existing = businessObject.extensionElements as ModdleElement | undefined;
    if (existing) return existing;

    const created = factory().create('bpmn:ExtensionElements', { values: [] });
    created.$parent = businessObject;
    modeling().updateModdleProperties(element, businessObject, { extensionElements: created });
    return created;
  }

  /**
   * Setzt genau eine Erweiterung eines Typs. `null` entfernt sie — leere Attribute stehen zu
   * lassen wäre kein Nichts, sondern ein leerer Wert, den die Engine wieder auswerten müsste.
   */
  function writeExtension(
    element: DiagramElement,
    type: string,
    properties: Record<string, unknown> | null,
  ): void {
    const existing = extension(element.businessObject, type);

    if (properties === null) {
      if (!existing) return;
      const container = element.businessObject.extensionElements as ModdleElement;
      const values = (container.values as ModdleElement[]).filter((value) => value !== existing);
      modeling().updateModdleProperties(element, container, { values });

      // Ein leeres `<bpmn:extensionElements />` bleibt sonst als Rest im Diagramm stehen.
      if (values.length === 0) {
        modeling().updateModdleProperties(element, element.businessObject, { extensionElements: undefined });
      }
      return;
    }

    if (existing) {
      modeling().updateModdleProperties(element, existing, properties);
      return;
    }

    const container = ensureExtensionElements(element);
    const created = factory().create(type, properties);
    created.$parent = container;
    modeling().updateModdleProperties(element, container, {
      values: [...((container.values as ModdleElement[] | undefined) ?? []), created],
    });
  }

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
    const expression = element.businessObject?.conditionExpression as ModdleElement | undefined;
    return text(expression, 'body');
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

  function ensureProcessExtensionElements(scope: ProcessScope): ModdleElement {
    const existing = scope.businessObject.extensionElements as ModdleElement | undefined;
    if (existing) return existing;

    const created = factory().create('bpmn:ExtensionElements', { values: [] });
    created.$parent = scope.businessObject;
    modeling().updateModdleProperties(scope.element, scope.businessObject, { extensionElements: created });
    return created;
  }

  return {
    /** Liest alle Werte, die das Panel für ein Element anzeigt. */
    read(elementId: string): ElementProperties | null {
      const element = registry().get(elementId);
      if (!element) return null;

      const businessObject = element.businessObject;
      const formDefinition = extension(businessObject, 'zeebe:FormDefinition');
      const assignment = extension(businessObject, 'zeebe:AssignmentDefinition');
      const schedule = extension(businessObject, 'zeebe:TaskSchedule');
      const taskDefinition = extension(businessObject, 'zeebe:TaskDefinition');
      const formKey = text(formDefinition, 'formKey') || text(formDefinition, 'formId');

      return {
        id: element.id,
        type: businessObject?.$type ?? element.type,
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

        inputs: ioMappings(element, 'inputParameters'),
        outputs: ioMappings(element, 'outputParameters'),

        condition: conditionOf(element),
        isDefaultFlow: element.source?.businessObject.default === businessObject,
        conditionApplies:
          kindOf(element) === 'sequenceFlow' &&
          CONDITIONAL_SOURCES.some((type) => isTypeOrSubtype(element.source, type)),

        outgoing: outgoingFlows(element),
        uncovered: uncoveredProperties(businessObject),
      };
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
        writeExtension(element, 'zeebe:FormDefinition', null);
        return;
      }

      // `formId` und `formKey` bedeuten dasselbe Ziel; stünden beide da, entschiede die
      // Lesereihenfolge der Engine, welches Formular gilt. Deshalb wird die Gegenseite
      // ausdrücklich geleert.
      writeExtension(element, 'zeebe:FormDefinition', {
        formKey: trimmed,
        formId: undefined,
        externalReference: undefined,
      });
    },

    setAssignment(elementId: string, assignment: Assignment): void {
      const element = registry().get(elementId);
      if (!element) return;

      const values = {
        assignee: blankToUndefined(assignment.assignee),
        candidateGroups: blankToUndefined(assignment.candidateGroups),
        candidateUsers: blankToUndefined(assignment.candidateUsers),
      };
      const isEmpty = Object.values(values).every((value) => value === undefined);
      writeExtension(element, 'zeebe:AssignmentDefinition', isEmpty ? null : values);
    },

    setSchedule(elementId: string, schedule: Schedule): void {
      const element = registry().get(elementId);
      if (!element) return;

      const values = {
        dueDate: blankToUndefined(schedule.dueDate),
        followUpDate: blankToUndefined(schedule.followUpDate),
      };
      const isEmpty = Object.values(values).every((value) => value === undefined);
      writeExtension(element, 'zeebe:TaskSchedule', isEmpty ? null : values);
    },

    setJob(elementId: string, jobType: string, retries: string): void {
      const element = registry().get(elementId);
      if (!element) return;

      const type = blankToUndefined(jobType);
      const attempts = blankToUndefined(retries);

      // Ein `type=""` waere kein fehlender Auftragstyp, sondern ein leerer: Der Workflow liesse
      // sich speichern, und zur Laufzeit fände kein Worker den Schritt. Ohne Angabe wird das
      // Attribut deshalb weggelassen — dann meldet schon das Speichern den fehlenden Typ.
      if (type === undefined && attempts === undefined) {
        writeExtension(element, 'zeebe:TaskDefinition', null);
        return;
      }

      writeExtension(element, 'zeebe:TaskDefinition', { type, retries: attempts });
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
        writeExtension(element, 'zeebe:IoMapping', null);
        return;
      }

      const container = ensureExtensionElements(element);
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

      const expression = factory().create('bpmn:FormalExpression', { body });
      expression.$parent = flow.businessObject;
      modeling().updateProperties(flow, { conditionExpression: expression });
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

      const container = ensureProcessExtensionElements(scope);
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

function blankToUndefined(value: string): string | undefined {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

/**
 * Prüft den Typ eines Elements einschließlich seiner Oberklassen. `bpmn:Activity` ist keine
 * eigene Elementart, sondern die Oberklasse von Aufgaben und Teilprozessen — ohne diese
 * Prüfung bekäme ein Fluss aus einer Aufgabe heraus kein Bedingungsfeld.
 */
function isTypeOrSubtype(element: DiagramElement | undefined, type: string): boolean {
  if (!element) return false;
  const descriptor = element.businessObject?.$instanceOf;
  if (typeof descriptor === 'function') {
    return (descriptor as (type: string) => boolean).call(element.businessObject, type);
  }
  return element.businessObject?.$type === type;
}
