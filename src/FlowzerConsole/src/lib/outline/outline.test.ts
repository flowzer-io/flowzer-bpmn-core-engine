import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { setStartFormKey, updateStep } from './edit';
import { readOutline } from './read';
import { writeOutlineXml } from './write';
import { hasBlocker, type OutlineChoice, type OutlineParallel, type OutlineStep } from './model';

/** Das Beispiel liegt ausserhalb der Konsole; der Pfad wird vom Arbeitsverzeichnis aus gesucht. */
function repositoryFile(relative: string): string {
  let directory = process.cwd();
  while (!existsSync(resolve(directory, relative))) {
    const parent = dirname(directory);
    if (parent === directory) throw new Error(`${relative} nicht gefunden`);
    directory = parent;
  }
  return readFileSync(resolve(directory, relative), 'utf8');
}

/**
 * Der Urlaubsantrag als Gliederungsvorlage — eine Kopie des Beispiels aus der Zeit vor
 * dem abbrechenden Endereignis. Das echte Beispiel liegt seitdem ausserhalb der
 * Teilmenge (siehe docs/GLIEDERUNG-TEILMENGE.md und den Test weiter unten); die
 * Gliederung wird dafuer nicht erweitert. Die Kopie haelt den Stand fest, an dem sich
 * Lesen, Zerlegen und Rueckuebersetzen an einem echten, mehrzweigigen Modell messen.
 */
const URLAUBSANTRAG = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), 'fixtures/urlaubsantrag-gliederung.bpmn'),
  'utf8',
);

/** Das echte Beispiel — es muss abgelehnt werden, nicht gelesen. */
const URLAUBSANTRAG_ECHT = repositoryFile('examples/urlaubsantrag/urlaubsantrag.bpmn');

const MINIMAL = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="Definitions_1" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_1" name="Freigabe" isExecutable="true">
    <bpmn:startEvent id="Start_1" name="Start" />
    <bpmn:userTask id="Task_1" name="Antrag prüfen">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="Prüfung" />
        <zeebe:assignmentDefinition candidateGroups="Vorgesetzte" />
        <zeebe:taskSchedule dueDate="PT48H" />
      </bpmn:extensionElements>
    </bpmn:userTask>
    <bpmn:endEvent id="End_1" name="Fertig" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />
  </bpmn:process>
</bpmn:definitions>`;

function withProcessBody(body: string): string {
  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_1">
  <bpmn:process id="Process_1" isExecutable="true">${body}</bpmn:process>
</bpmn:definitions>`;
}

// Testzweck: Die Gliederung liest einen einfachen Ablauf mit allen Angaben, die
// am Schritt bearbeitbar sein sollen — Formular, Zuweisung und Frist.
describe('readOutline — einfacher Ablauf', () => {
  it('liest Schritt, Formular, Zuweisung und Frist', () => {
    const { document, issues } = readOutline(MINIMAL);

    expect(hasBlocker(issues)).toBe(false);
    expect(document?.processName).toBe('Freigabe');
    expect(document?.blocks.map((block) => block.kind)).toEqual(['step', 'end']);

    const step = document!.blocks[0]! as OutlineStep;
    expect(step).toMatchObject({
      id: 'Task_1',
      name: 'Antrag prüfen',
      task: 'user',
      formKey: 'Prüfung',
      candidateGroups: 'Vorgesetzte',
      dueDate: 'PT48H',
    });
  });

  it('legt für einen noch leeren Workflow Start und Ende an', () => {
    const empty = withProcessBody('');
    const { document, issues } = readOutline(empty);

    expect(hasBlocker(issues)).toBe(false);
    expect(document?.blocks.map((block) => block.kind)).toEqual(['end']);
    expect(writeOutlineXml(document!).xml).toContain('bpmn:startEvent');
  });

  it('liest und schreibt eine stabile Directory-Zuweisung ohne Informationsverlust', () => {
    const directoryXml = MINIMAL
      .replace(
        'xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"',
        'xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"',
      )
      .replace(
        '<zeebe:assignmentDefinition candidateGroups="Vorgesetzte" />',
        '<flowzer:taskAssignment mode="directory" assigneeId="10000000-0000-0000-0000-000000000001" candidateGroupIds="20000000-0000-0000-0000-000000000001" />',
      );

    const read = readOutline(directoryXml);
    const step = read.document?.blocks[0] as OutlineStep;

    expect(hasBlocker(read.issues)).toBe(false);
    expect(step.assignmentMode).toBe('directory');
    expect(step.directoryAssigneeId).toBe('10000000-0000-0000-0000-000000000001');
    expect(step.directoryCandidateGroupIds).toEqual(['20000000-0000-0000-0000-000000000001']);

    const written = writeOutlineXml(read.document!);
    expect(hasBlocker(written.issues)).toBe(false);
    expect(written.xml).toContain('xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"');
    expect(written.xml).toContain('<flowzer:taskAssignment mode="directory"');
    expect(written.xml).not.toContain('zeebe:assignmentDefinition');
  });
});

// Testzweck: Die Gliederung erhält KI-Aufgaben als eigene Dienstvariante vollständig und
// schreibt den versionierten Vertrag ohne Informationsverlust zurück ins BPMN.
describe('readOutline — KI-Aufgabe', () => {
  it('liest und schreibt den KI-Vertrag vollständig', () => {
    const xml = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                  id="Definitions_Ai">
  <bpmn:process id="Process_Ai" isExecutable="true">
    <bpmn:startEvent id="Start_1" />
    <bpmn:serviceTask id="Ai_1" name="Klassifizieren">
      <bpmn:extensionElements>
        <zeebe:taskDefinition type="flowzer.ai.v1" retries="2" />
        <flowzer:aiTask contractVersion="1" connectionId="118adeb6-65a4-4e57-a03b-d3b0a3300ac9"
          model="model-a" instructionVersion="2" maxInputTokens="4096" maxOutputTokens="1024" timeoutSeconds="60">
          <flowzer:instruction>Classify the request.</flowzer:instruction>
          <flowzer:resultSchema>{"type":"object"}</flowzer:resultSchema>
        </flowzer:aiTask>
        <zeebe:ioMapping>
          <zeebe:input source="=request" target="request" />
          <zeebe:output source="=result" target="classification" />
        </zeebe:ioMapping>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:endEvent id="End_1" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Ai_1" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Ai_1" targetRef="End_1" />
  </bpmn:process>
</bpmn:definitions>`;

    const read = readOutline(xml);
    expect(hasBlocker(read.issues)).toBe(false);
    const step = read.document!.blocks[0] as OutlineStep;
    expect(step.serviceTaskMode).toBe('ai');
    expect(step.aiTask).toEqual({
      contractVersion: '1',
      connectionId: '118adeb6-65a4-4e57-a03b-d3b0a3300ac9',
      model: 'model-a',
      instructionVersion: '2',
      instruction: 'Classify the request.',
      resultSchema: '{"type":"object"}',
      maxInputTokens: '4096',
      maxOutputTokens: '1024',
      timeoutSeconds: '60',
    });

    const written = writeOutlineXml(read.document!);
    expect(hasBlocker(written.issues)).toBe(false);
    expect(written.xml).toContain('<flowzer:aiTask contractVersion="1"');
    expect(written.xml).toContain('<flowzer:instruction>Classify the request.</flowzer:instruction>');
    expect(written.xml).toContain('<flowzer:resultSchema>{&quot;type&quot;:&quot;object&quot;}</flowzer:resultSchema>');
  });
});

// Testzweck: Das echte Beispiel bricht seit dem abbrechenden Endereignis den ganzen
// Vorgang ab und liegt damit ausserhalb der Teilmenge. Die Gliederung muss es mit
// einer Meldung ablehnen, die das Element benennt — und dabei nicht abstuerzen.
describe('readOutline — echtes Beispiel ausserhalb der Teilmenge', () => {
  it('lehnt den Urlaubsantrag mit einer klaren Meldung ab', () => {
    const { document, issues } = readOutline(URLAUBSANTRAG_ECHT);

    expect(hasBlocker(issues)).toBe(true);
    // Ohne Zerlegung gibt es keine Gliederung — die Seite zeigt nur die Meldung.
    expect(document).toBeUndefined();

    const blocker = issues.find((issue) => issue.level === 'blocker');
    expect(blocker?.message).toContain('terminateEventDefinition');
    expect(blocker?.message).toContain('dem Diagramm vorbehalten');
  });
});

// Testzweck: Der Urlaubsantrag ist der Testfall aus der Praxis — parallele Bloecke,
// drei Tore hintereinander, ein gemeinsamer Ablehnungsweg. Er muss vollstaendig
// lesbar sein, sonst taugt die Gliederung nicht. Geprueft wird die Vorlage unter
// fixtures/, nicht das echte Beispiel — siehe die Erklaerung oben.
describe('readOutline — Urlaubsantrag', () => {
  const { document, issues } = readOutline(URLAUBSANTRAG);

  it('liest das Modell ohne Blocker', () => {
    expect(issues.filter((issue) => issue.level === 'blocker')).toEqual([]);
    expect(document).toBeDefined();
  });

  it('sagt an, dass die erklärenden Kommentare beim Speichern verloren gehen', () => {
    // Das Beispiel erklaert im XML, warum es so modelliert ist. Die Gliederung
    // fuehrt diese Kommentare nicht mit — das ist die auffaelligste offene Kante
    // des Prototyps und muss deshalb dastehen, nicht still passieren.
    const hint = issues.find((issue) => issue.message.includes('Kommentare'));
    expect(hint?.level).toBe('hinweis');
  });

  it('liest das freiwillige Startformular am Startereignis', () => {
    // Der Antrag wird beim Starten ausgefuellt; ohne diese Angabe stuende der
    // Vorgang ohne seine ersten Variablen da.
    expect(document!.startFormKey).toBe('Urlaubsantrag');
    expect(document!.startFormId).toBeUndefined();
  });

  it('bildet den Hauptablauf als Folge ab', () => {
    // Der Antrag selbst ist kein Schritt mehr: Er wird beim Starten ueber das
    // Startformular ausgefuellt, und der Ablauf beginnt mit den Pruefungen.
    expect(document!.blocks.map((block) => block.kind)).toEqual(['parallel', 'choice', 'step', 'end']);
  });

  it('zeigt die drei gleichzeitigen Prüfungen als einen Block', () => {
    const parallel = document!.blocks[0]! as OutlineParallel;
    expect(parallel.id).toBe('Gw_Fork_Pruefung');
    expect(parallel.joinId).toBe('Gw_Join_Pruefung');
    expect(parallel.branches.map((branch) => (branch.blocks[0] as OutlineStep | undefined)?.name)).toEqual([
      'Urlaubstage prüfen',
      'Urlaub fachlich entscheiden',
      'Vertretung prüfen',
    ]);
  });

  it('schachtelt die drei Tore und führt leere Zweige auf den gemeinsamen Abschluss', () => {
    const tage = document!.blocks[1]! as OutlineChoice;
    expect(tage.id).toBe('Gw_Tage');
    expect(tage.branches.map((branch) => branch.label)).toEqual(['ja', 'nicht genug Tage']);
    expect(tage.branches[0]!.condition).toBe('=tageAusreichend = "ja"');
    expect(tage.branches[1]!.isDefault).toBe(true);
    // Der Standardweg fuehrt ohne eigenen Schritt weiter unten auf „Ablehnung mitteilen".
    expect(tage.branches[1]!.blocks).toEqual([]);

    const fachlich = tage.branches[0]!.blocks[0]! as OutlineChoice;
    expect(fachlich.id).toBe('Gw_Fachlich');
    const vertretung = fachlich.branches[0]!.blocks[0]! as OutlineChoice;
    expect(vertretung.id).toBe('Gw_Vertretung');
    expect((vertretung.branches[0]!.blocks[0] as OutlineParallel).id).toBe('Gw_Fork_Genehmigt');
  });
});

// Testzweck: Lesen und Schreiben muessen denselben Prozess ergeben. Das ist die
// Zusage, auf die sich das Speichern stuetzt.
describe('Rückübersetzung', () => {
  it('schreibt den Urlaubsantrag verlustfrei zurück', () => {
    const first = readOutline(URLAUBSANTRAG);
    const { xml, issues } = writeOutlineXml(first.document!);

    expect(issues.filter((issue) => issue.level === 'blocker')).toEqual([]);
    expect(xml).toBeDefined();

    const second = readOutline(xml!);
    expect(second.issues.filter((issue) => issue.level === 'blocker')).toEqual([]);
    expect(JSON.stringify(second.document!.blocks)).toBe(JSON.stringify(first.document!.blocks));
  });

  it('behält das Startformular am Startereignis', () => {
    const { document } = readOutline(URLAUBSANTRAG);
    const { xml } = writeOutlineXml(document!);

    expect(xml).toContain('<zeebe:formDefinition formKey="Urlaubsantrag" />');
    expect(readOutline(xml!).document!.startFormKey).toBe('Urlaubsantrag');
  });

  it('schreibt ohne Startformular keine extensionElements an den Start', () => {
    // Das Startformular ist freiwillig. Ein leeres `extensionElements` waere ein
    // Unterschied zum gelesenen Modell — und damit ein Blocker beim naechsten Lesen.
    const { document } = readOutline(MINIMAL);
    expect(document!.startFormKey).toBeUndefined();

    const { xml } = writeOutlineXml(document!);
    const startEvent = xml!.slice(xml!.indexOf('<bpmn:startEvent'), xml!.indexOf('</bpmn:startEvent>'));
    expect(startEvent).not.toContain('extensionElements');
    expect(hasBlocker(readOutline(xml!).issues)).toBe(false);
  });

  it('behält das Diagramm, wenn nur das Startformular gewechselt wird', () => {
    const { document } = readOutline(URLAUBSANTRAG);
    const { xml, issues } = writeOutlineXml(setStartFormKey(document!, 'Urlaubsantrag kurz'));

    expect(xml).toContain('formKey="Urlaubsantrag kurz"');
    expect(xml).toContain('x="1550" y="302"');
    expect(issues.some((issue) => issue.message.includes('neu berechnet'))).toBe(false);
  });

  it('behält das vorhandene Diagramm, solange sich die Struktur nicht ändert', () => {
    const { document } = readOutline(URLAUBSANTRAG);
    const { xml, issues } = writeOutlineXml(document!);

    expect(xml).toContain('x="1550" y="302"');
    expect(issues.some((issue) => issue.message.includes('neu berechnet'))).toBe(false);
  });

  it('behält das Diagramm auch, wenn nur eine Frist geändert wird', () => {
    const { document } = readOutline(URLAUBSANTRAG);
    const changed = updateStep(document!, 'Task_Urlaubstage', { dueDate: 'P2D' });
    const { xml, issues } = writeOutlineXml(changed);

    expect(xml).toContain('dueDate="P2D"');
    expect(xml).toContain('x="1550" y="302"');
    expect(issues.some((issue) => issue.message.includes('neu berechnet'))).toBe(false);
  });

  it('berechnet die Anordnung neu, sobald sich die Struktur ändert', () => {
    const { document } = readOutline(URLAUBSANTRAG);
    const shortened = { ...document!, blocks: document!.blocks.slice(1) };
    const { issues } = writeOutlineXml(shortened);

    expect(issues.some((issue) => issue.message.includes('neu berechnet'))).toBe(true);
  });
});
