import { describe, expect, it } from 'vitest';

import { readElementProperties } from './elementProperties';
import type { DiagramElement, ModdleElement } from './moddle';

/** Baut ein Diagrammelement aus einem Moddle-Objekt, wie bpmn-js es liefert. */
function element(businessObject: Partial<ModdleElement> & { $type: string }, id = 'Element_1'): DiagramElement {
  return { id, type: businessObject.$type, businessObject: businessObject as ModdleElement };
}

function extensions(...values: ModdleElement[]): ModdleElement {
  return { $type: 'bpmn:ExtensionElements', values } as ModdleElement;
}

function expression(body: string): ModdleElement {
  return { $type: 'bpmn:FormalExpression', body } as ModdleElement;
}

// Testzweck: Das Panel zeigt die Werte einer menschlichen Aufgabe so, wie die Engine sie liest.
// Wird hier anders gelesen als im ModelParser, zeigt der Modeler etwas anderes an, als läuft.
describe('readElementProperties für eine menschliche Aufgabe', () => {
  const userTask = element({
    $type: 'bpmn:UserTask',
    name: 'Antrag prüfen',
    extensionElements: extensions(
      { $type: 'zeebe:FormDefinition', formKey: 'Urlaubsantrag:1.0' } as ModdleElement,
      {
        $type: 'zeebe:AssignmentDefinition',
        assignee: 'anna',
        candidateGroups: 'personal',
      } as ModdleElement,
      { $type: 'zeebe:TaskSchedule', dueDate: 'PT48H' } as ModdleElement,
      {
        $type: 'zeebe:IoMapping',
        inputParameters: [{ $type: 'zeebe:Input', source: '=antrag.betrag', target: 'betrag' }],
      } as ModdleElement,
    ),
  });

  it('liest Formular, Zuweisung, Frist und Zuordnungen', () => {
    const properties = readElementProperties(userTask);

    expect(properties.kind).toBe('userTask');
    expect(properties.formKey).toBe('Urlaubsantrag:1.0');
    expect(properties.assignee).toBe('anna');
    expect(properties.candidateGroups).toBe('personal');
    expect(properties.candidateUsers).toBe('');
    expect(properties.dueDate).toBe('PT48H');
    expect(properties.inputs).toEqual([{ source: '=antrag.betrag', target: 'betrag' }]);
    expect(properties.outputs).toEqual([]);
  });

  it('bietet Ein- und Ausgangszuordnungen an, aber keinen Auftragstyp', () => {
    const properties = readElementProperties(userTask);

    expect(properties.supportsInputMappings).toBe(true);
    expect(properties.supportsOutputMappings).toBe(true);
    expect(properties.needsJobType).toBe(false);
  });

  it('meldet einen Formularverweis, den die Engine nicht liest', () => {
    const withExternalReference = element({
      $type: 'bpmn:UserTask',
      extensionElements: extensions({
        $type: 'zeebe:FormDefinition',
        externalReference: 'Urlaubsantrag',
      } as ModdleElement),
    });

    const properties = readElementProperties(withExternalReference);

    expect(properties.formKey).toBeNull();
    expect(properties.externalFormReference).toBe('Urlaubsantrag');
  });
});

// Testzweck: Ein Timer gilt der Engine über genau eine Zeitangabe. Stehen mehrere im Diagramm,
// muss das Panel dieselbe nehmen wie der Parser — sonst zeigt es eine Angabe, die nicht wirkt.
describe('readElementProperties für Timer', () => {
  function timerEvent(definition: Partial<ModdleElement>) {
    return element({
      $type: 'bpmn:IntermediateCatchEvent',
      eventDefinitions: [{ $type: 'bpmn:TimerEventDefinition', ...definition } as ModdleElement],
    });
  }

  it('liest eine Dauer', () => {
    expect(readElementProperties(timerEvent({ timeDuration: expression('PT1H') })).timer).toEqual({
      kind: 'duration',
      expression: 'PT1H',
    });
  });

  it('liest einen Zeitpunkt', () => {
    expect(readElementProperties(timerEvent({ timeDate: expression('2026-10-01T10:00:00Z') })).timer).toEqual({
      kind: 'date',
      expression: '2026-10-01T10:00:00Z',
    });
  });

  it('liest einen Zyklus', () => {
    expect(readElementProperties(timerEvent({ timeCycle: expression('R3/PT1H') })).timer).toEqual({
      kind: 'cycle',
      expression: 'R3/PT1H',
    });
  });

  it('nimmt bei mehreren Angaben dieselbe wie der Parser', () => {
    const properties = readElementProperties(
      timerEvent({ timeCycle: expression('R3/PT1H'), timeDuration: expression('PT1H') }),
    );

    expect(properties.timer).toEqual({ kind: 'duration', expression: 'PT1H' });
  });

  it('meldet einen Timer ohne Angabe als leere Dauer', () => {
    expect(readElementProperties(timerEvent({})).timer).toEqual({ kind: 'duration', expression: '' });
  });

  it('liefert für ein Element ohne Timer nichts', () => {
    expect(readElementProperties(element({ $type: 'bpmn:UserTask' })).timer).toBeNull();
  });
});

// Testzweck: Eine wartende Nachricht löst die Engine über `messageRef` auf; eine gesendete
// verschickt sie über einen Worker-Auftrag. Das Panel darf deshalb nicht überall dasselbe
// Feld zeigen — sonst stünde am sendenden Ereignis ein Name ohne Wirkung.
describe('readElementProperties für Nachrichten', () => {
  const message = {
    $type: 'bpmn:Message',
    name: 'Antrag eingegangen',
    extensionElements: extensions({ $type: 'zeebe:Subscription', correlationKey: '=antragsnummer' } as ModdleElement),
  } as ModdleElement;

  it('liest Name und Korrelationsschlüssel eines wartenden Ereignisses', () => {
    const properties = readElementProperties(
      element({
        $type: 'bpmn:StartEvent',
        eventDefinitions: [{ $type: 'bpmn:MessageEventDefinition', messageRef: message } as ModdleElement],
      }),
    );

    expect(properties.message).toEqual({ name: 'Antrag eingegangen', correlationKey: '=antragsnummer' });
    expect(properties.needsJobType).toBe(false);
  });

  it('liest die Nachricht auch an einer Empfangsaufgabe', () => {
    const properties = readElementProperties(element({ $type: 'bpmn:ReceiveTask', messageRef: message }));

    expect(properties.message?.name).toBe('Antrag eingegangen');
  });

  it('zeigt am sendenden Ereignis keinen Nachrichtennamen, sondern einen Auftrag', () => {
    const properties = readElementProperties(
      element({
        $type: 'bpmn:EndEvent',
        eventDefinitions: [{ $type: 'bpmn:MessageEventDefinition', messageRef: message } as ModdleElement],
        extensionElements: extensions({ $type: 'zeebe:TaskDefinition', type: 'antrag-melden' } as ModdleElement),
      }),
    );

    expect(properties.message).toBeNull();
    expect(properties.needsJobType).toBe(true);
    expect(properties.jobType).toBe('antrag-melden');
  });
});

// Testzweck: Signal, aufgerufener Prozess und Mehrfachausführung wertet die Engine aus; ohne
// sie im Panel liesse sich ein Element zeichnen, das nirgends vollständig einzustellen wäre.
describe('readElementProperties für weitere Elemente', () => {
  it('liest den Signalnamen', () => {
    const properties = readElementProperties(
      element({
        $type: 'bpmn:IntermediateThrowEvent',
        eventDefinitions: [
          {
            $type: 'bpmn:SignalEventDefinition',
            signalRef: { $type: 'bpmn:Signal', name: 'Freigabe erteilt' } as ModdleElement,
          } as ModdleElement,
        ],
      }),
    );

    expect(properties.signalName).toBe('Freigabe erteilt');
  });

  it('liest den aufgerufenen Prozess samt Vorgabe für die Datenweitergabe', () => {
    const properties = readElementProperties(
      element({
        $type: 'bpmn:CallActivity',
        extensionElements: extensions({
          $type: 'zeebe:CalledElement',
          processId: 'Process_Urlaub',
          propagateAllChildVariables: false,
        } as ModdleElement),
      }),
    );

    expect(properties.calledProcess).toEqual({
      processId: 'Process_Urlaub',
      propagateAllChildVariables: false,
      // Ohne Angabe reicht die Engine alles durch.
      propagateAllParentVariables: true,
    });
  });

  it('unterscheidet eine Skript-Aufgabe mit Ausdruck von einer als Auftrag', () => {
    const asScript = readElementProperties(
      element({
        $type: 'bpmn:ScriptTask',
        extensionElements: extensions({
          $type: 'zeebe:Script',
          expression: '=betrag * 1.19',
          resultVariable: 'brutto',
        } as ModdleElement),
      }),
    );
    const asJob = readElementProperties(element({ $type: 'bpmn:ScriptTask' }));

    expect(asScript.script).toEqual({ expression: '=betrag * 1.19', resultVariable: 'brutto' });
    expect(asScript.needsJobType).toBe(false);
    expect(asJob.script).toBeNull();
    expect(asJob.isScriptTask).toBe(true);
    expect(asJob.needsJobType).toBe(true);
  });

  it('liest die Mehrfachausführung samt Abbruchbedingung', () => {
    const properties = readElementProperties(
      element({
        $type: 'bpmn:ServiceTask',
        loopCharacteristics: {
          $type: 'bpmn:MultiInstanceLoopCharacteristics',
          isSequential: true,
          completionCondition: expression('=anzahl > 3'),
          extensionElements: extensions({
            $type: 'zeebe:LoopCharacteristics',
            inputCollection: '=positionen',
            inputElement: 'position',
          } as ModdleElement),
        } as ModdleElement,
      }),
    );

    expect(properties.multiInstance).toEqual({
      isSequential: true,
      inputCollection: '=positionen',
      inputElement: 'position',
      outputCollection: '',
      outputElement: '',
      completionCondition: '=anzahl > 3',
    });
  });

  it('bietet am Start-Ereignis nur Ausgangszuordnungen an', () => {
    const properties = readElementProperties(element({ $type: 'bpmn:StartEvent' }));

    expect(properties.supportsInputMappings).toBe(false);
    expect(properties.supportsOutputMappings).toBe(true);
  });
});
