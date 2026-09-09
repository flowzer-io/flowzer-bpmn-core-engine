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

  it('behandelt eine fehlende Flowzer-Erweiterung weiterhin als Legacy-Textmodus', () => {
    const properties = readElementProperties(userTask);

    expect(properties.assignmentMode).toBe('text');
    expect(properties.directoryAssignment).toEqual({
      assigneeId: '',
      candidateUserIds: [],
      candidateGroupIds: [],
    });
    expect(properties.assignmentContractWarning).toBeNull();
  });

  it('liest bekannte Benutzer und Gruppen ausschließlich als stabile Referenzen', () => {
    const directoryTask = element({
      $type: 'bpmn:UserTask',
      extensionElements: extensions({
        $type: 'flowzer:TaskAssignment',
        mode: 'directory',
        assigneeId: '10000000-0000-0000-0000-000000000001',
        candidateUserIds: '10000000-0000-0000-0000-000000000002,10000000-0000-0000-0000-000000000003',
        candidateGroupIds: '20000000-0000-0000-0000-000000000001',
      } as ModdleElement),
    });

    const properties = readElementProperties(directoryTask);

    expect(properties.assignmentMode).toBe('directory');
    expect(properties.directoryAssignment).toEqual({
      assigneeId: '10000000-0000-0000-0000-000000000001',
      candidateUserIds: [
        '10000000-0000-0000-0000-000000000002',
        '10000000-0000-0000-0000-000000000003',
      ],
      candidateGroupIds: ['20000000-0000-0000-0000-000000000001'],
    });
    expect(properties.assignee).toBe('');
  });

  it('weist auf einen unbekannten Modus hin, statt ihn still umzudeuten', () => {
    const invalidTask = element({
      $type: 'bpmn:UserTask',
      extensionElements: extensions({
        $type: 'flowzer:TaskAssignment',
        mode: 'automatic',
        assigneeId: '10000000-0000-0000-0000-000000000001',
      } as ModdleElement),
    });

    const properties = readElementProperties(invalidTask);

    expect(properties.assignmentMode).toBe('invalid');
    expect(properties.assignmentContractWarning).toContain('automatic');
  });
});

// Testzweck: Der Diagrammeditor liest KI-Aufgaben als eigene Service-Task-Variante und zeigt
// exakt den versionierten Vertrag, den die serverseitige Validierung auswertet.
describe('readElementProperties für eine KI-Aufgabe', () => {
  it('liest Verbindung, Anweisung, Ergebnisschema und Ausführungsgrenzen', () => {
    const aiTask = element({
      $type: 'bpmn:ServiceTask',
      extensionElements: extensions(
        { $type: 'zeebe:TaskDefinition', type: 'flowzer.ai.v1', retries: '2' } as ModdleElement,
        {
          $type: 'flowzer:AiTask',
          contractVersion: '1',
          connectionId: '118adeb6-65a4-4e57-a03b-d3b0a3300ac9',
          model: 'model-a',
          instructionVersion: '3',
          maxInputTokens: '4096',
          maxOutputTokens: '512',
          timeoutSeconds: '45',
          instruction: { $type: 'flowzer:Instruction', body: 'Classify the request.' },
          resultSchema: { $type: 'flowzer:ResultSchema', body: '{"type":"object"}' },
        } as ModdleElement,
      ),
    });

    const properties = readElementProperties(aiTask);

    expect(properties.serviceTaskMode).toBe('ai');
    expect(properties.aiTask).toEqual({
      contractVersion: '1',
      connectionId: '118adeb6-65a4-4e57-a03b-d3b0a3300ac9',
      model: 'model-a',
      instructionVersion: '3',
      instruction: 'Classify the request.',
      resultSchema: '{"type":"object"}',
      maxInputTokens: '4096',
      maxOutputTokens: '512',
      timeoutSeconds: '45',
    });
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

// Testzweck: Ein Startformular am reinen Startereignis muss das Panel als solches erkennen.
// Ohne `startFormApplies` böte es das Formular auch dort an, wo die Engine es nicht liest —
// an einem Timer-, Nachrichten- oder Signalstart füllt es niemand aus.
describe('readElementProperties für ein Startereignis', () => {
  const process = { $type: 'bpmn:Process' } as ModdleElement;

  /** Ein Startereignis mit Formular — wahlweise mit Ereignisdefinition und Behälter. */
  function startEvent(eventDefinitions?: ModdleElement[], parent: ModdleElement = process) {
    return element({
      $type: 'bpmn:StartEvent',
      name: 'Antrag stellen',
      $parent: parent,
      ...(eventDefinitions ? { eventDefinitions } : {}),
      extensionElements: extensions({
        $type: 'zeebe:FormDefinition',
        formKey: 'Urlaubsantrag',
      } as ModdleElement),
    });
  }

  it('liest den Form-Key und meldet das Startformular als zutreffend', () => {
    const properties = readElementProperties(startEvent());

    expect(properties.kind).toBe('startEvent');
    expect(properties.formKey).toBe('Urlaubsantrag');
    expect(properties.startFormApplies).toBe(true);
  });

  it.each([
    ['bpmn:TimerEventDefinition'],
    ['bpmn:MessageEventDefinition'],
    ['bpmn:SignalEventDefinition'],
  ])('meldet das Startformular an einem %s-Start als nicht zutreffend', (type) => {
    const properties = readElementProperties(startEvent([{ $type: type } as ModdleElement]));

    expect(properties.kind).toBe('startEvent');
    expect(properties.startFormApplies).toBe(false);
  });

  // Der Parser behandelt nur Zeit, Nachricht und Signal gesondert; alles andere landet in
  // seinem gewöhnlichen Zweig und bekommt sehr wohl einen Form-Key. Wäre das Panel hier
  // strenger, stünde ein Schlüssel im Diagramm, den niemand mehr sehen oder entfernen kann.
  it('meldet das Startformular an einem bedingten Start als zutreffend', () => {
    const properties = readElementProperties(
      startEvent([{ $type: 'bpmn:ConditionalEventDefinition' } as ModdleElement]),
    );

    expect(properties.startFormApplies).toBe(true);
  });

  it('meldet am Startereignis eines Subprozesses kein Startformular', () => {
    const properties = readElementProperties(
      startEvent(undefined, { $type: 'bpmn:SubProcess' } as ModdleElement),
    );

    expect(properties.startFormApplies).toBe(false);
  });

  it('meldet an einer Aufgabe kein Startformular', () => {
    const properties = readElementProperties(element({ $type: 'bpmn:UserTask' }));

    expect(properties.startFormApplies).toBe(false);
  });
});
