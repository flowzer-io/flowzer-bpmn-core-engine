import { BpmnModdle } from 'bpmn-moddle';
import zeebeModdle from 'zeebe-bpmn-moddle/resources/zeebe.json';
import { describe, expect, it } from 'vitest';

import { convertCamunda7, detectCamunda7, translateExpression, type Finding, type ImportReport } from './camunda7Import';
import { FLOWZER_MODDLE } from '@/components/bpmn/flowzerModdle';
import { readElementProperties, type DiagramElement } from '@/components/bpmn/bpmnEditor';
import type { ModdleElement } from '@/components/bpmn/moddle';

/**
 * Ein Bestellprozess, wie ihn Camunda 7 speichert: External Task und Java-Delegate,
 * menschliche Aufgabe mit Zuweisung, Frist und Formularverweis, Ein-/Ausgabezuordnungen,
 * JUEL-Bedingungen, Mehrfachausführung, Listener und ein Skript-Task.
 */
const CAMUNDA7_XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xmlns:camunda="http://camunda.org/schema/1.0/bpmn"
                  id="Definitions_Bestellung" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:message id="Message_Zahlung" name="Zahlungseingang" />
  <bpmn:process id="bestellprozess" name="Bestellprozess" isExecutable="true"
                camunda:historyTimeToLive="180" camunda:versionTag="1.4"
                camunda:candidateStarterGroups="vertrieb">
    <bpmn:startEvent id="Start_1" name="Bestellung eingegangen"
                     camunda:formKey="embedded:app:forms/bestellung.html" />
    <bpmn:serviceTask id="Task_Bonitaet" name="Bonität prüfen"
                      camunda:type="external" camunda:topic="bonitaet-pruefen" camunda:asyncBefore="true">
      <bpmn:extensionElements>
        <camunda:inputOutput>
          <camunda:inputParameter name="kundennummer">\${bestellung.kundennummer}</camunda:inputParameter>
          <camunda:inputParameter name="waehrung">EUR</camunda:inputParameter>
          <camunda:inputParameter name="positionen">
            <camunda:list>
              <camunda:value>erste</camunda:value>
            </camunda:list>
          </camunda:inputParameter>
          <camunda:outputParameter name="bonitaetScore">\${score}</camunda:outputParameter>
        </camunda:inputOutput>
        <camunda:executionListener event="start" class="de.example.Protokoll" />
        <camunda:properties>
          <camunda:property name="kostenstelle" value="4711" />
        </camunda:properties>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:serviceTask id="Task_Lager" name="Lager buchen"
                      camunda:class="de.example.bestellung.LagerBuchenDelegate" camunda:jobPriority="10">
      <bpmn:extensionElements>
        <camunda:failedJobRetryTimeCycle>R3/PT5M</camunda:failedJobRetryTimeCycle>
        <camunda:connector>
          <camunda:connectorId>http-connector</camunda:connectorId>
        </camunda:connector>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:serviceTask id="Task_Rechnung" name="Rechnung senden"
                      camunda:delegateExpression="\${rechnungVersandDelegate}" />
    <bpmn:exclusiveGateway id="Gateway_Bonitaet" name="Bonität ausreichend?" />
    <bpmn:userTask id="Task_Freigabe" name="Bestellung freigeben"
                   camunda:assignee="\${antragsteller}" camunda:candidateGroups="einkauf,leitung"
                   camunda:candidateUsers="anna" camunda:dueDate="\${faelligAm}" camunda:followUpDate="P2D"
                   camunda:formKey="camunda-forms:deployment:freigabe.form" camunda:priority="50"
                   camunda:asyncAfter="true">
      <bpmn:extensionElements>
        <camunda:taskListener event="create" delegateExpression="\${erinnerung}" />
      </bpmn:extensionElements>
      <bpmn:multiInstanceLoopCharacteristics camunda:collection="\${positionen}"
                                             camunda:elementVariable="position">
        <bpmn:completionCondition xsi:type="bpmn:tFormalExpression">\${abgeschlossen &amp;&amp; !gesperrt}</bpmn:completionCondition>
      </bpmn:multiInstanceLoopCharacteristics>
    </bpmn:userTask>
    <bpmn:scriptTask id="Task_Rabatt" name="Rabatt berechnen" scriptFormat="groovy">
      <bpmn:script>rabatt = 5</bpmn:script>
    </bpmn:scriptTask>
    <bpmn:intermediateCatchEvent id="Catch_Zahlung" name="Zahlungseingang">
      <bpmn:messageEventDefinition id="MessageDefinition_1" messageRef="Message_Zahlung" />
    </bpmn:intermediateCatchEvent>
    <bpmn:endEvent id="End_1" name="Bestellung abgeschlossen" />
    <bpmn:sequenceFlow id="Flow_Freigabe" sourceRef="Gateway_Bonitaet" targetRef="Task_Freigabe">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">\${bonitaetScore &gt;= 60 &amp;&amp; !gesperrt}</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Flow_Ablehnung" sourceRef="Gateway_Bonitaet" targetRef="End_1">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">\${execution.getVariable("bonitaetScore") == 0 || abgelehnt}</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Flow_Storno" sourceRef="Gateway_Bonitaet" targetRef="Task_Rabatt">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">\${status != 'offen'}</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Flow_Eilig" sourceRef="Gateway_Bonitaet" targetRef="Task_Lager">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">\${bestellung.istEilig()}</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="bestellprozess">
      <bpmndi:BPMNShape id="Start_1_di" bpmnElement="Start_1">
        <dc:Bounds x="152" y="102" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Task_Bonitaet_di" bpmnElement="Task_Bonitaet">
        <dc:Bounds x="240" y="80" width="100" height="80" />
      </bpmndi:BPMNShape>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;

const PLAIN_XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="Definitions_Plain" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="plain" name="Ohne Camunda" isExecutable="true">
    <bpmn:startEvent id="Start_1" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="plain" />
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;

const converted = convertCamunda7(CAMUNDA7_XML);

function findings(report: ImportReport, group: keyof ImportReport, elementId: string): Finding[] {
  return report[group].filter((finding) => finding.elementId === elementId);
}

function texts(report: ImportReport, group: keyof ImportReport, elementId: string): string {
  return findings(report, group, elementId)
    .map((finding) => `${finding.what} ${finding.detail ?? ''}`)
    .join(' | ');
}

/** Das Ergebnis mit denselben Moddle-Erweiterungen lesen, die auch die Konsole lädt. */
async function readBack(xml: string): Promise<Map<string, DiagramElement>> {
  const moddle = new BpmnModdle({ zeebe: zeebeModdle, flowzer: FLOWZER_MODDLE });
  const { rootElement, warnings } = await moddle.fromXML(xml);
  expect(warnings).toEqual([]);

  const elements = new Map<string, DiagramElement>();
  const collect = (node: ModdleElement) => {
    const id = typeof node.id === 'string' ? node.id : null;
    if (id) elements.set(id, { id, type: node.$type, businessObject: node });
    for (const child of (node.flowElements as ModdleElement[] | undefined) ?? []) collect(child);
  };

  for (const root of (rootElement as unknown as { rootElements?: ModdleElement[] }).rootElements ?? []) {
    collect(root);
  }

  return elements;
}

describe('detectCamunda7', () => {
  // Testzweck: Der deklarierte Camunda-7-Namensraum ist das Erkennungsmerkmal für ein Modell,
  // das übersetzt werden muss.
  it('erkennt ein Camunda-7-Modell am Namensraum', () => {
    expect(detectCamunda7(CAMUNDA7_XML)).toBe(true);
  });

  // Testzweck: Eine gewöhnliche BPMN-Datei darf nicht fälschlich als Camunda 7 gelten,
  // sonst zeigte die Konsole einen Bericht ohne Anlass.
  it('erkennt eine Datei ohne camunda-Namensraum nicht als Camunda 7', () => {
    expect(detectCamunda7(PLAIN_XML)).toBe(false);
    expect(detectCamunda7(converted.xml)).toBe(false);
  });

  // Testzweck: Unlesbare Eingaben sind kein Camunda 7 und dürfen die Erkennung nicht werfen lassen.
  it('meldet für kaputte Eingaben kein Camunda 7', () => {
    expect(detectCamunda7('kein xml')).toBe(false);
    expect(detectCamunda7('')).toBe(false);
  });
});

describe('convertCamunda7 — Service-Tasks', () => {
  // Testzweck: Ein External Task trägt sein Thema schon am Modell; es wird verlustfrei
  // zu Flowzers Auftragstyp.
  it('macht aus camunda:topic einen zeebe:taskDefinition-Typ', () => {
    expect(converted.xml).toContain('<zeebe:taskDefinition type="bonitaet-pruefen"/>');
    expect(texts(converted.report, 'converted', 'Task_Bonitaet')).toContain('bonitaet-pruefen');
  });

  // Testzweck: Ein Java-Delegate läuft in Flowzer nicht; aus dem letzten Segment des
  // Klassennamens wird ein Auftragstyp, und der Bericht verlangt einen Worker dafür.
  it('leitet aus camunda:class einen Auftragstyp ab und meldet den Worker-Bedarf', () => {
    expect(converted.xml).toContain('<zeebe:taskDefinition type="LagerBuchenDelegate"/>');
    expect(texts(converted.report, 'attention', 'Task_Lager')).toContain('Java-Delegate wird zum Worker-Auftrag');
  });

  // Testzweck: Auch ein Delegate-Ausdruck ergibt einen Auftragstyp — ohne die JUEL-Hülle.
  it('leitet aus camunda:delegateExpression einen Auftragstyp ab', () => {
    expect(converted.xml).toContain('<zeebe:taskDefinition type="rechnungVersandDelegate"/>');
    expect(texts(converted.report, 'attention', 'Task_Rechnung')).toContain('Worker');
  });
});

describe('convertCamunda7 — menschliche Aufgaben', () => {
  // Testzweck: Zuweisung und Frist haben in Flowzer eine Entsprechung und werden übernommen.
  it('übersetzt Zuweisung und Frist nach zeebe:assignmentDefinition und zeebe:taskSchedule', () => {
    expect(converted.xml).toContain(
      '<zeebe:assignmentDefinition assignee="=antragsteller" candidateUsers="anna" candidateGroups="einkauf,leitung"/>',
    );
    expect(converted.xml).toContain('<zeebe:taskSchedule dueDate="=faelligAm" followUpDate="P2D"/>');
  });

  // Testzweck: Ein Camunda-7-Formularverweis läuft in Flowzer ins Leere; er wird entfernt
  // und der Bericht sagt, dass das Formular neu entsteht.
  it('entfernt den Formularverweis und meldet ihn zur Nacharbeit', () => {
    expect(converted.xml).not.toContain('freigabe.form');
    expect(texts(converted.report, 'attention', 'Task_Freigabe')).toContain(
      'Formulare werden in Flowzer neu erstellt',
    );
  });

  // Testzweck: Auch das Startformular aus Camunda 7 verweist auf ein fremdes Formular.
  it('entfernt den Formularverweis am Startereignis', () => {
    expect(converted.xml).not.toContain('bestellung.html');
    expect(texts(converted.report, 'attention', 'Start_1')).toContain('Formularverweis entfernt');
  });

  // Testzweck: Die Aufgabenpriorität aus Camunda 7 hat keine Entsprechung und zählt als Verlust.
  it('meldet camunda:priority als entfernt', () => {
    expect(texts(converted.report, 'dropped', 'Task_Freigabe')).toContain('camunda:priority');
  });
});

describe('convertCamunda7 — Zuordnungen', () => {
  // Testzweck: camunda:inputOutput wird zu zeebe:ioMapping; der Ausdruck bekommt das
  // führende Gleichheitszeichen, ein Literal bleibt Literal.
  it('übersetzt inputParameter und outputParameter', () => {
    expect(converted.xml).toContain('<zeebe:input source="=bestellung.kundennummer" target="kundennummer"/>');
    expect(converted.xml).toContain('<zeebe:input source="EUR" target="waehrung"/>');
    expect(converted.xml).toContain('<zeebe:output source="=score" target="bonitaetScore"/>');
  });

  // Testzweck: Ein Parameter mit Liste, Abbildung oder Skript hat in Flowzer keine
  // Entsprechung; er darf nicht auf einen halben Ausdruck eingedampft werden.
  it('entfernt strukturierte Parameter und meldet sie', () => {
    expect(converted.xml).not.toContain('target="positionen"');
    expect(converted.xml).not.toContain('erste');
    expect(texts(converted.report, 'attention', 'Task_Bonitaet')).toContain('camunda:list');
  });
});

describe('convertCamunda7 — Ausdrücke', () => {
  // Testzweck: Die JUEL-Operatoren haben in FEEL andere Namen; die Bedingung muss
  // vollständig übersetzt werden, sonst nimmt das Tor stillschweigend den falschen Weg.
  it('übersetzt UND, Vergleich und Verneinung nach FEEL', () => {
    expect(converted.xml).toContain('>=bonitaetScore &gt;= 60 and not(gesperrt)<');
  });

  // Testzweck: execution.getVariable ist in Flowzer schlicht der Variablenname,
  // ODER und Gleichheit werden mit übersetzt.
  it('löst execution.getVariable auf und übersetzt ODER', () => {
    expect(converted.xml).toContain('>=bonitaetScore = 0 or abgelehnt<');
  });

  // Testzweck: FEEL kennt `!=`; die Ungleichheit darf nicht von der Verneinungsregel
  // zerlegt werden.
  it('lässt die Ungleichheit stehen', () => {
    expect(converted.xml).toContain(`>=status != 'offen'<`);
  });

  // Testzweck: Ein Methodenaufruf hat in FEEL keine Entsprechung; er wird gemeldet,
  // statt in einen Ausdruck übersetzt zu werden, der etwas anderes bedeutet.
  it('meldet Methodenaufrufe zur Nacharbeit', () => {
    expect(texts(converted.report, 'attention', 'Flow_Eilig')).toContain('istEilig()');
  });

  // Testzweck: Ein reiner Literaltext ist kein Ausdruck und bleibt unverändert.
  it('lässt Literaltext unverändert', () => {
    expect(translateExpression('P2D')).toEqual({ value: 'P2D', notes: [] });
  });

  // Testzweck: Gemischter Text ist weder Literal noch Ausdruck; er bleibt stehen und wird gemeldet.
  it('meldet gemischten Text statt ihn zu raten', () => {
    const translated = translateExpression('Hallo ${name}');
    expect(translated.value).toBe('Hallo ${name}');
    expect(translated.notes).toHaveLength(1);
  });

  // Testzweck: Eine Verneinung vor einer Klammer ist mehrdeutig und wird nicht geraten.
  it('meldet eine nicht eindeutige Verneinung', () => {
    expect(translateExpression('${!(a && b)}').notes.join(' ')).toContain('Verneinung');
  });
});

describe('convertCamunda7 — Mehrfachausführung', () => {
  // Testzweck: Sammlung und Elementvariable sind Flowzers inputCollection/inputElement;
  // die Sammlung ist in FEEL immer ein Ausdruck.
  it('übersetzt collection und elementVariable', () => {
    expect(converted.xml).toContain('<zeebe:loopCharacteristics inputCollection="=positionen" inputElement="position"/>');
  });

  // Testzweck: Auch die Abbruchbedingung der Mehrfachausführung ist JUEL.
  it('übersetzt die Abbruchbedingung', () => {
    expect(converted.xml).toContain('>=abgeschlossen and not(gesperrt)<');
  });
});

describe('convertCamunda7 — Verluste', () => {
  // Testzweck: Angaben zur Ausführungssteuerung sind in Flowzer gegenstandslos; sie
  // verschwinden, aber nicht stillschweigend.
  it('meldet asyncBefore, jobPriority und die Prozessangaben als entfernt', () => {
    const dropped = converted.report.dropped.map((finding) => finding.what).join(' | ');
    expect(dropped).toContain('camunda:asyncBefore');
    expect(dropped).toContain('camunda:asyncAfter');
    expect(dropped).toContain('camunda:jobPriority');
    expect(dropped).toContain('camunda:historyTimeToLive');
    expect(dropped).toContain('camunda:versionTag');
    expect(dropped).toContain('camunda:candidateStarterGroups');
    expect(converted.xml).not.toContain('historyTimeToLive');
  });

  // Testzweck: Listener, Eigenschaften, Wiederholungszyklus und Konnektoren tragen Verhalten;
  // ihr Wegfall gehört in beide Gruppen, damit niemand ihn übersieht.
  it('meldet Listener und Konnektoren als Verlust und zur Nacharbeit', () => {
    for (const extension of ['executionListener', 'taskListener', 'properties', 'failedJobRetryTimeCycle', 'connector']) {
      expect(converted.report.dropped.some((finding) => finding.what.includes(extension))).toBe(true);
      expect(converted.report.attention.some((finding) => finding.what.includes(extension))).toBe(true);
    }
    expect(converted.xml).not.toContain('http-connector');
  });
});

describe('convertCamunda7 — Fähigkeitsvertrag', () => {
  // Testzweck: Was Flowzer nicht ausführt, soll der Bericht vorab sagen — nicht erst die
  // abgelehnte Veröffentlichung.
  it('meldet den Skript-Task als nicht ausführbar', () => {
    expect(texts(converted.report, 'attention', 'Task_Rabatt')).toContain('Veröffentlichung lehnt diesen Schritt ab');
  });

  // Testzweck: In Camunda 7 korreliert die Laufzeit-API; in Flowzer gehört der Schlüssel
  // ins Modell, sonst wartet das Ereignis auf nichts Bestimmtes.
  it('verlangt einen Korrelationsschlüssel am wartenden Nachrichtenereignis', () => {
    expect(texts(converted.report, 'attention', 'Catch_Zahlung')).toContain('zeebe:subscription');
  });
});

describe('convertCamunda7 — Dokument', () => {
  // Testzweck: Ohne die Namensraumdeklaration wären alle erzeugten zeebe-Angaben unlesbar.
  it('deklariert den zeebe-Namensraum', () => {
    expect(converted.xml).toContain('xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"');
  });

  // Testzweck: Bleibt kein camunda-Rest, verschwindet auch die Deklaration — sonst stünde
  // im Modell ein Namensraum, der nichts mehr bedeutet.
  it('entfernt den camunda-Namensraum, wenn nichts übrig bleibt', () => {
    expect(converted.xml).not.toContain('http://camunda.org/schema/1.0/bpmn');
    expect(converted.xml).not.toContain('camunda:');
  });

  // Testzweck: Ein nicht übersetzter camunda-Rest darf nicht verschwinden; er bleibt
  // stehen und wird gemeldet, damit niemand ihn für übernommen hält.
  it('lässt unbekannte camunda-Angaben stehen und meldet sie', () => {
    const withUnknown = CAMUNDA7_XML.replace('camunda:asyncBefore="true"', 'camunda:unbekannt="wert"');
    const result = convertCamunda7(withUnknown);

    expect(result.xml).toContain('camunda:unbekannt="wert"');
    expect(result.report.attention.some((finding) => finding.what.includes('camunda:unbekannt'))).toBe(true);
  });

  // Testzweck: Die Zeichnung ist der halbe Wert eines Bestandsmodells; sie darf die
  // Übersetzung unverändert überstehen.
  it('erhält den Diagrammteil', () => {
    expect(converted.xml).toContain('<bpmndi:BPMNShape id="Task_Bonitaet_di" bpmnElement="Task_Bonitaet">');
    expect(converted.xml).toContain('<dc:Bounds x="152" y="102" width="36" height="36"/>');
  });

  // Testzweck: isExecutable entscheidet, ob Flowzer den Prozess überhaupt startet.
  it('lässt isExecutable stehen', () => {
    expect(converted.xml).toContain('isExecutable="true"');
  });

  // Testzweck: Ein zweiter Lauf über ein bereits übersetztes Modell darf nichts mehr
  // ändern; sonst wäre jeder erneute Import ein stiller Umbau.
  it('ist idempotent', () => {
    const again = convertCamunda7(converted.xml);

    expect(again.xml).toBe(converted.xml);
    expect(again.report.converted).toEqual([]);
    expect(again.report.dropped).toEqual([]);
    // Was Flowzer nicht ausführt, bleibt wahr und wird deshalb weiterhin gemeldet.
    expect(again.report.attention.some((finding) => finding.elementId === 'Task_Rabatt')).toBe(true);
  });

  // Testzweck: Eine gewöhnliche BPMN-Datei läuft unverändert durch; derselbe Aufruf darf
  // deshalb auch für sie benutzt werden.
  it('reicht eine Datei ohne camunda-Angaben durch', () => {
    const result = convertCamunda7(PLAIN_XML);

    expect(result.report.converted).toEqual([]);
    expect(result.report.dropped).toEqual([]);
    expect(result.report.attention).toEqual([]);
    expect(result.xml).toContain('<bpmn:startEvent id="Start_1"/>');
  });

  // Testzweck: Eine unlesbare Datei soll klar scheitern, statt ein halbes Modell zu liefern.
  it('wirft bei unlesbarem XML', () => {
    expect(() => convertCamunda7('<bpmn:definitions>')).toThrow();
  });
});

describe('convertCamunda7 — Gegenprobe mit Flowzers Leser', () => {
  // Testzweck: Die erzeugten zeebe-Namen sind nur dann richtig, wenn genau der Leser der
  // Konsole sie findet — dieselben Moddle-Erweiterungen, dieselbe Lesefunktion.
  it('liest das Ergebnis mit readElementProperties zurück', async () => {
    const elements = await readBack(converted.xml);

    const service = readElementProperties(elements.get('Task_Bonitaet')!);
    expect(service.jobType).toBe('bonitaet-pruefen');
    expect(service.inputs).toEqual([
      { source: '=bestellung.kundennummer', target: 'kundennummer' },
      { source: 'EUR', target: 'waehrung' },
    ]);
    expect(service.outputs).toEqual([{ source: '=score', target: 'bonitaetScore' }]);

    const userTask = readElementProperties(elements.get('Task_Freigabe')!);
    expect(userTask.assignee).toBe('=antragsteller');
    expect(userTask.candidateGroups).toBe('einkauf,leitung');
    expect(userTask.candidateUsers).toBe('anna');
    expect(userTask.dueDate).toBe('=faelligAm');
    expect(userTask.followUpDate).toBe('P2D');
    expect(userTask.formKey).toBeNull();
    expect(userTask.multiInstance).toMatchObject({ inputCollection: '=positionen', inputElement: 'position' });

    const flow = readElementProperties(elements.get('Flow_Freigabe')!);
    expect(flow.condition).toBe('=bonitaetScore >= 60 and not(gesperrt)');
  });
});
