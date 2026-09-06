import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

import { updateStep } from './edit';
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

const URLAUBSANTRAG = repositoryFile('examples/urlaubsantrag/urlaubsantrag.bpmn');

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
});

// Testzweck: Der Urlaubsantrag ist der Testfall aus der Praxis — parallele Bloecke,
// drei Tore hintereinander, ein gemeinsamer Ablehnungsweg. Er muss vollstaendig
// lesbar sein, sonst taugt die Gliederung nicht.
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

  it('bildet den Hauptablauf als Folge ab', () => {
    expect(document!.blocks.map((block) => block.kind)).toEqual(['step', 'parallel', 'choice', 'step', 'end']);
  });

  it('zeigt die drei gleichzeitigen Prüfungen als einen Block', () => {
    const parallel = document!.blocks[1]! as OutlineParallel;
    expect(parallel.id).toBe('Gw_Fork_Pruefung');
    expect(parallel.joinId).toBe('Gw_Join_Pruefung');
    expect(parallel.branches.map((branch) => (branch.blocks[0] as OutlineStep | undefined)?.name)).toEqual([
      'Urlaubstage prüfen',
      'Urlaub fachlich entscheiden',
      'Vertretung prüfen',
    ]);
  });

  it('schachtelt die drei Tore und führt leere Zweige auf den gemeinsamen Abschluss', () => {
    const tage = document!.blocks[2]! as OutlineChoice;
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
    const shortened = { ...document!, blocks: document!.blocks.slice(2) };
    const { issues } = writeOutlineXml(shortened);

    expect(issues.some((issue) => issue.message.includes('neu berechnet'))).toBe(true);
  });
});
