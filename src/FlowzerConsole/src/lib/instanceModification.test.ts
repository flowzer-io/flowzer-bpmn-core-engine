import { describe, expect, it } from 'vitest';

import { ApiError } from '@/lib/api/client';
import type { ModificationFlowNodeDto } from '@/lib/api/types';

import {
  changedVariables,
  flowNodeLabel,
  modificationErrorProblems,
  modificationFindingText,
  modificationMoves,
  modificationTargetGroups,
  parseVariableDraft,
  stepLabel,
} from './instanceModification';

describe('Befunde als deutsche Sätze', () => {
  const codes = [
    'InstanceNotRunning',
    'TokenMissing',
    'TokenNotMovable',
    'DuplicateMove',
    'TargetMissing',
    'TargetNotAllowed',
    'VariableNameInvalid',
    'ModificationFailed',
    'NothingToDo',
    'RequestTooLarge',
    'UserTaskCancelled',
    'ServiceTaskJobCancelled',
    'ServiceTaskJobInProgress',
    'UserTaskDraftDiscarded',
    'TimerRecalculated',
    'VariableNotFound',
  ];

  // Testzweck: Der Betrieb entscheidet über einen nicht rücknehmbaren Eingriff. Bliebe auch
  // nur ein Code unübersetzt, stünde an dieser Stelle eine englische Entwicklermeldung.
  it('übersetzt jeden Code des Vertrags', () => {
    for (const code of codes) {
      const text = modificationFindingText({ code, flowNodeId: 'Pruefung', message: 'raw message' });
      expect(text, code).not.toBe('raw message');
      expect(text, code).toMatch(/\.$/);
    }
  });

  // Testzweck: Wer ein Hindernis liest, muss den betroffenen Schritt wiederfinden — sonst
  // sucht er in einem Modell mit dreißig Knoten nach der Stelle, die gemeint ist.
  it('nennt den betroffenen Schritt', () => {
    expect(
      modificationFindingText({
        code: 'UserTaskCancelled',
        flowNodeId: 'Pruefung',
        message: 'User task cancelled.',
      }),
    ).toMatch(/„Pruefung“/);
  });

  // Testzweck: Ohne Schritt darf kein leeres Anführungspaar entstehen; der Satz muss auch
  // dann gelesen werden können, wenn der Befund die ganze Anfrage meint.
  it('kommt ohne benannten Schritt aus', () => {
    const text = modificationFindingText({
      code: 'TokenNotMovable',
      flowNodeId: null,
      message: 'Token not movable.',
    });
    expect(text).not.toMatch(/„“/);
    expect(text).toMatch(/Instanz/);
  });

  // Testzweck: Die Engine darf Gründe ergänzen. Ein unbekannter Code muss die technische
  // Meldung zeigen, statt die Oberfläche zu einem stummen Befund zu machen.
  it('fällt bei unbekanntem Code auf die Meldung der API zurück', () => {
    expect(
      modificationFindingText({ code: 'SomethingNew', flowNodeId: null, message: 'Anything new.' }),
    ).toBe('Anything new.');
  });
});

describe('Abgelehnte Ausführung', () => {
  function rejection(body: unknown): ApiError {
    return new ApiError('Die Anfrage konnte nicht verarbeitet werden.', {
      status: 422,
      url: '/instance/x/modification',
      body,
    });
  }

  // Testzweck: Die Ablehnung trägt dieselben Befunde wie der Trockenlauf. Blieben sie im
  // Rumpf liegen, stünde im Dialog nur eine technische Zeile statt des eigentlichen Grundes.
  it('liest die Hindernisse aus dem Rumpf der 422', () => {
    const problems = modificationErrorProblems(
      rejection({
        status: 422,
        successful: false,
        errorMessage: 'The modification request could not be processed',
        problems: [
          { code: 'TargetNotAllowed', tokenId: null, flowNodeId: 'Start', message: 'Target not allowed.' },
        ],
      }),
    );

    expect(problems).toHaveLength(1);
    expect(modificationFindingText(problems[0]!)).toMatch(/Start- oder ein angeheftetes Ereignis/);
  });

  // Testzweck: Nicht jeder Fehler trägt Befunde — ein Netzfehler oder eine fremde Antwort
  // darf nicht als Liste von Hindernissen erscheinen, die es gar nicht gibt.
  it('bleibt ohne Befunde leer', () => {
    expect(modificationErrorProblems(rejection({ detail: 'Nur eine Meldung.' }))).toEqual([]);
    expect(modificationErrorProblems(rejection({ problems: ['kaputt'] }))).toEqual([]);
    expect(modificationErrorProblems(new Error('Netz weg.'))).toEqual([]);
    expect(modificationErrorProblems(undefined)).toEqual([]);
  });
});

describe('Gruppierung der Ziele', () => {
  const targets: ModificationFlowNodeDto[] = [
    { id: 'Ende', name: null, type: 'EndEvent' },
    { id: 'Weiche', name: 'Entscheidung', type: 'ExclusiveGateway' },
    { id: 'Buchen', name: 'Buchen', type: 'ServiceTask' },
    { id: 'Pruefung', name: 'Prüfung', type: 'UserTask' },
    { id: 'Antrag', name: 'Antrag erfassen', type: 'UserTask' },
    { id: 'Abbruch', name: 'Abbruch', type: 'FlowzerTerminateEvent' },
    { id: 'Skript', name: 'Skript', type: 'ScriptTask' },
  ];

  // Testzweck: In einer flachen Liste aus dreißig Knoten findet niemand die richtige Stelle.
  // Die Gruppe sagt vorab, was für ein Schritt gewählt wird — auch für die Flowzer-Endereignisse.
  it('fasst verwandte Elementarten unter einem deutschen Namen zusammen', () => {
    const groups = modificationTargetGroups(targets);

    expect(groups.map((group) => group.label)).toEqual([
      'Benutzeraufgaben',
      'Service-Tasks',
      'Gateways',
      'Endereignisse',
      'ScriptTask',
    ]);
    const endereignisse = groups.find((group) => group.label === 'Endereignisse');
    expect(endereignisse?.targets.map((target) => target.id)).toEqual(['Abbruch', 'Ende']);
  });

  // Testzweck: Innerhalb einer Gruppe entscheidet die Beschriftung, nicht die Reihenfolge der
  // API — sonst steht dieselbe Auswahl beim nächsten Öffnen anders da.
  it('sortiert innerhalb einer Gruppe nach der Beschriftung', () => {
    const benutzeraufgaben = modificationTargetGroups(targets).find(
      (group) => group.label === 'Benutzeraufgaben',
    );

    expect(benutzeraufgaben?.targets.map((target) => flowNodeLabel(target))).toEqual([
      'Antrag erfassen',
      'Prüfung',
    ]);
  });

  // Testzweck: Für eine unbekannte Elementart darf keine Bezeichnung erfunden werden. Die
  // rohe Art ist ehrlicher als eine Gruppe, die etwas anderes behauptet.
  it('behält unbekannte Elementarten als Gruppennamen', () => {
    const groups = modificationTargetGroups([{ id: 'X', name: null, type: 'ReceiveTask' }]);

    expect(groups).toEqual([{ label: 'ReceiveTask', targets: [{ id: 'X', name: null, type: 'ReceiveTask' }] }]);
  });
});

describe('Beschriftungen', () => {
  // Testzweck: Ein unbenannter Knoten darf nicht als leere Zeile erscheinen — dann wäre die
  // Auswahl nicht mehr bedienbar. Die Id ist die schlechtere, aber vorhandene Auskunft.
  it('fällt ohne Namen auf die Kennung zurück', () => {
    expect(stepLabel({ tokenId: 't1', flowNodeId: 'Task_1', name: null, type: 'UserTask' })).toBe('Task_1');
    expect(stepLabel({ tokenId: 't1', flowNodeId: 'Task_1', name: '  ', type: 'UserTask' })).toBe('Task_1');
    expect(stepLabel({ tokenId: 't1', flowNodeId: 'Task_1', name: 'Prüfung', type: 'UserTask' })).toBe('Prüfung');
    expect(flowNodeLabel({ id: 'Node_1', name: null, type: 'EndEvent' })).toBe('Node_1');
  });
});

describe('Verschiebungen aus dem Stand des Dialogs', () => {
  // Testzweck: „belassen“ ist keine Verschiebung. Ginge der leere Wert mit, verschöbe der
  // Eingriff einen Schritt auf einen Knoten ohne Kennung — die API lehnte ab.
  it('lässt Schritte ohne Ziel aus der Anfrage', () => {
    expect(modificationMoves({ 'token-1': '', 'token-2': 'Freigabe' })).toEqual([
      { tokenId: 'token-2', targetFlowNodeId: 'Freigabe' },
    ]);
  });

  // Testzweck: Dieselbe Wahl muss dieselbe Anfrage ergeben, sonst prüft der Trockenlauf
  // etwas anderes, als danach ausgeführt wird.
  it('ordnet die Verschiebungen stabil nach der Token-Kennung', () => {
    expect(modificationMoves({ 'token-b': 'Ende', 'token-a': 'Freigabe' })).toEqual([
      { tokenId: 'token-a', targetFlowNodeId: 'Freigabe' },
      { tokenId: 'token-b', targetFlowNodeId: 'Ende' },
    ]);
  });

  // Testzweck: Ohne Wahl darf keine leere Verschiebungsliste entstehen, die die API als
  // Anfrage missdeutet.
  it('ergibt ohne Wahl gar nichts', () => {
    expect(modificationMoves({})).toEqual([]);
    expect(modificationMoves({ 'token-1': '' })).toEqual([]);
  });
});

describe('Geänderte Variablen', () => {
  // Testzweck: Das Feld ist mit allen Variablen vorbelegt. Unverändert abgeschickt schriebe
  // der Eingriff jede einzelne neu und behauptete eine Korrektur, die niemand vorgenommen hat.
  it('schickt unverändertes nicht mit', () => {
    expect(changedVariables({ betrag: 120, kunde: 'Meier' }, { betrag: 120, kunde: 'Meier' })).toBeUndefined();
  });

  // Testzweck: Geänderte und neue Variablen sind genau der Zweck des Eingriffs; beide müssen
  // durchkommen, während der unveränderte Rest stehen bleibt.
  it('nennt geänderte und neue Variablen', () => {
    expect(changedVariables({ betrag: 120, kunde: 'Meier' }, { betrag: 130, kunde: 'Meier', iban: 'DE02' })).toEqual({
      betrag: 130,
      iban: 'DE02',
    });
  });

  // Testzweck: Gleiche Objekte sind keine Änderung — sonst gälte jede Formatierung des
  // Feldes als Korrektur eines verschachtelten Werts.
  it('vergleicht verschachtelte Werte über ihren Inhalt', () => {
    expect(changedVariables({ adresse: { ort: 'Kiel' } }, { adresse: { ort: 'Kiel' } })).toBeUndefined();
    expect(changedVariables({ adresse: { ort: 'Kiel' } }, { adresse: { ort: 'Lübeck' } })).toEqual({
      adresse: { ort: 'Lübeck' },
    });
  });
});

describe('Variablenfeld lesen', () => {
  // Testzweck: Ein leeres Feld heißt „nichts setzen“. Würde daraus ein leeres Objekt, trüge
  // die Anfrage eine Variablenänderung, die es nicht gibt.
  it('nimmt ein leeres Feld als „nichts setzen“', () => {
    expect(parseVariableDraft('   ')).toEqual({});
  });

  // Testzweck: Wer im JSON ein Komma vergisst, soll das im Dialog lesen und nicht an einer
  // 400 der API bemerken.
  it('nennt fehlerhaftes JSON als Grund', () => {
    expect(parseVariableDraft('{ "a": }').error).toMatch(/JSON/);
  });

  // Testzweck: Ein Array oder ein blanker Wert ist kein Variablenname-zu-Wert-Paar und
  // bewirkte still nichts. Abgelehnt wird er vor dem Absenden.
  it('lehnt Arrays, blanke Werte und null ab', () => {
    expect(parseVariableDraft('[1, 2]').error).toBeTruthy();
    expect(parseVariableDraft('42').error).toBeTruthy();
    expect(parseVariableDraft('null').error).toBeTruthy();
  });

  // Testzweck: Der Normalfall muss durchkommen, sonst wäre die Prüfung nur eine Sperre.
  it('liefert das Objekt der Eingabe', () => {
    expect(parseVariableDraft('{ "betrag": 120 }')).toEqual({ variables: { betrag: 120 } });
  });
});
