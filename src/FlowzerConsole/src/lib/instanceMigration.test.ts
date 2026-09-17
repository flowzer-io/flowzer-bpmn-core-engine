import { describe, expect, it } from 'vitest';

import type { MigrationFlowNodeDto } from '@/lib/api/types';

import {
  flowNodeLabel,
  instanceCountLabel,
  isMigrationCandidate,
  migrationFindingText,
  migrationFlowNodeMapping,
  migrationMappingRows,
  migrationSelectionProblem,
  migrationTargetOptions,
} from './instanceMigration';

const candidate = {
  relatedDefinitionId: 'urlaub',
  definitionId: 'definition-1',
};

describe('Auswahlregel für die Migration', () => {
  // Testzweck: Ohne Auswahl gibt es nichts zu migrieren. Die Regel muss das als Grund
  // nennen können, damit die Schaltfläche nicht kommentarlos tot wirkt.
  it('verlangt mindestens eine Instanz', () => {
    expect(migrationSelectionProblem([])).toMatch(/mindestens eine/i);
  });

  // Testzweck: Die Vorschau prüft genau einen Quell-Workflow in genau einer Version.
  // Eine saubere Auswahl muss deshalb ohne Grund durchgehen.
  it('lässt mehrere Instanzen derselben Version zu', () => {
    expect(migrationSelectionProblem([candidate, { ...candidate }])).toBeNull();
  });

  // Testzweck: Instanzen verschiedener Workflows haben kein gemeinsames Ziel; die API
  // lehnt das mit 400 ab. Der Grund muss vorher in der Oberfläche stehen.
  it('lehnt Instanzen verschiedener Workflows mit Begründung ab', () => {
    const reason = migrationSelectionProblem([candidate, { ...candidate, relatedDefinitionId: 'reise' }]);
    expect(reason).toMatch(/Workflows/);
  });

  // Testzweck: Auch innerhalb eines Workflows laufen Instanzen auf verschiedenen Versionen.
  // Sie haben verschiedene Ausgangsstände und lassen sich nicht in einem Zug migrieren.
  it('lehnt Instanzen verschiedener Versionen mit Begründung ab', () => {
    const reason = migrationSelectionProblem([candidate, { ...candidate, definitionId: 'definition-2' }]);
    expect(reason).toMatch(/Version/);
  });
});

describe('Migrierbare Instanzen der Liste', () => {
  const instance = {
    instanceId: 'instance-1', definitionId: 'definition-1', relatedDefinitionId: 'urlaub',
    relatedDefinitionName: 'Urlaubsantrag', state: 'Waiting' as const, canInspect: true,
    userTaskSubscriptionCount: 0, messageSubscriptionCount: 0, signalSubscriptionCount: 0,
    serviceSubscriptionCount: 0, tokens: [],
  };

  // Testzweck: Migrieren darf nur, wer die Instanz auch einsehen darf — dieselbe
  // Betriebspolitik wie beim Abbruch. Ohne Freigabe lehnt die API ohnehin ab.
  it('erkennt eine laufende Instanz mit Betriebsrecht', () => {
    expect(isMigrationCandidate(instance)).toBe(true);
    expect(isMigrationCandidate({ ...instance, canInspect: false })).toBe(false);
  });

  // Testzweck: Beendete Instanzen haben keinen Token mehr, der umgehängt werden könnte.
  it('schließt beendete Instanzen aus', () => {
    expect(isMigrationCandidate({ ...instance, state: 'Completed' })).toBe(false);
    expect(isMigrationCandidate({ ...instance, state: 'Failed' })).toBe(false);
  });

  // Testzweck: Abgebrochene Instanzen zählen fachlich zu den fertigen. Genau deshalb darf
  // die Umbuchung sie nicht plötzlich als Migrationsquelle anbieten — die API lehnt sie
  // mit `InstanceNotRunning` ab, und der Abbruch ist nicht rückholbar.
  it('schließt abgebrochene Instanzen aus', () => {
    expect(isMigrationCandidate({ ...instance, state: 'Terminated' })).toBe(false);
    expect(isMigrationCandidate({ ...instance, state: 'Terminating' })).toBe(false);
  });
});

describe('Befunde als deutsche Sätze', () => {
  // Testzweck: Die API begründet technisch auf Englisch. Der Betrieb liest in der Konsole
  // deutsch — und braucht den betroffenen Schritt im Satz, um ihn im Modell zu finden.
  it('nennt den betroffenen Schritt im deutschen Satz', () => {
    const text = migrationFindingText({
      code: 'FlowNodeMissing', flowNodeId: 'Review', message: 'Flow node Review is missing.',
    });
    expect(text).toContain('Review');
    expect(text).not.toContain('missing');
  });

  // Testzweck: Ein verworfener Entwurf ist die Folge, die der Betrieb vor der Migration
  // kennen muss — sie steht als eigener Hinweis, nicht als Fehler.
  it('erklärt einen verworfenen Entwurf', () => {
    const text = migrationFindingText({
      code: 'UserTaskDraftDiscarded', flowNodeId: 'Antrag', message: 'Draft discarded.',
    });
    expect(text).toMatch(/Entwurf/);
  });

  // Testzweck: Ein bereits ausgelöstes Boundary-Event würde nach der Migration erneut
  // scharf stehen. Das ist keine Kleinigkeit und muss als Grund im Klartext dastehen.
  it('nennt ein bereits ausgelöstes angeheftetes Ereignis als Grund', () => {
    const text = migrationFindingText({
      code: 'BoundaryEventAlreadyTriggered', flowNodeId: 'Frist',
      message: 'Boundary event already triggered.',
    });
    expect(text).toContain('Frist');
    expect(text).toMatch(/scharf schalten/);
  });

  // Testzweck: Die API darf neue Codes ergänzen, ohne dass die Konsole leere Zeilen zeigt.
  // Dann steht dort die technische Meldung statt gar nichts.
  it('fällt bei unbekanntem Code auf die API-Meldung zurück', () => {
    expect(
      migrationFindingText({ code: 'SomethingNew', flowNodeId: null, message: 'Unmapped detail.' }),
    ).toBe('Unmapped detail.');
  });

  // Testzweck: Deployt jemand während des Stapels eine andere Version, bleibt die Instanz
  // unverändert. Der Satz muss das sagen und zur erneuten Auswahl auffordern, sonst hält der
  // Betrieb den leeren Ausgang für einen Fehler.
  it('erklärt eine zwischenzeitlich deployte andere Version', () => {
    const text = migrationFindingText({ code: 'TargetVersionChanged', flowNodeId: null, message: 'x' });

    expect(text).toMatch(/andere Version deployt/);
    expect(text).toMatch(/unverändert/);
  });

  // Testzweck: Kann die Ablage keine Entwürfe mitnehmen, hilft dem Bearbeiter kein
  // Wiederholen — das ist eine Sache des Betriebs und muss als solche dastehen.
  it('verweist bei fehlender Entwurfsablage an den Betrieb', () => {
    const text = migrationFindingText({ code: 'DraftStorageNotSupported', flowNodeId: null, message: 'x' });

    expect(text).toMatch(/Entwürfe/);
    expect(text).toMatch(/Betrieb/);
  });

  // Testzweck: Ein Timer rechnet nach der Migration mit der Dauer der Zielversion ab dem
  // ursprünglichen Beginn des Wartens. Wer das nicht weiß, wundert sich über eine Eskalation
  // unmittelbar nach dem Umzug.
  it('warnt vor sofort fälligen Timern am betroffenen Schritt', () => {
    const text = migrationFindingText({ code: 'TimerRecalculated', flowNodeId: 'Warten', message: 'x' });

    expect(text).toContain('„Warten“');
    expect(text).toMatch(/Zielversion/);
    expect(text).toMatch(/sofort fällig/);
  });

  // Testzweck: Ein Worker, der gerade arbeitet, verliert seine Arbeit durch die Migration
  // nicht. Der Hinweis darf deshalb nicht das Gegenteil behaupten und zum Abwarten drängen.
  it('sagt bei einem laufenden Worker-Auftrag, dass dessen Ergebnis übernommen wird', () => {
    const text = migrationFindingText({ code: 'ServiceTaskJobInProgress', flowNodeId: 'Buchen', message: 'x' });

    expect(text).toContain('„Buchen“');
    expect(text).toContain('übernommen');
    expect(text).not.toContain('verworfen');
  });
});

describe('Anzahl der Instanzen im Text', () => {
  // Testzweck: Der Assistent nennt die Anzahl in Schaltfläche, Zusammenfassung und Meldung.
  // Stünde dort „1 Instanzen“, wirkte die Auskunft vor einem unumkehrbaren Schritt schludrig.
  it('beugt Instanz und Instanzen richtig', () => {
    expect(instanceCountLabel(1)).toBe('1 Instanz');
    expect(instanceCountLabel(3)).toBe('3 Instanzen');
  });
});

describe('Knoten von Hand zuordnen', () => {
  const review: MigrationFlowNodeDto = { id: 'Review', name: 'Prüfung', type: 'UserTask' };
  const check: MigrationFlowNodeDto = { id: 'Check', name: null, type: 'UserTask' };
  const targets: MigrationFlowNodeDto[] = [
    { id: 'Freigabe', name: 'Freigabe', type: 'UserTask' },
    { id: 'Weiche', name: 'Weiche', type: 'ExclusiveGateway' },
  ];

  // Testzweck: Ein Knoten ohne Namen ist im Modell trotzdem eindeutig. Ohne den Rückfall
  // auf die Id stünde in der Auswahl eine leere Zeile, die niemand zuordnen kann.
  it('nennt den Namen des Knotens, sonst seine Id', () => {
    expect(flowNodeLabel(review)).toBe('Prüfung');
    expect(flowNodeLabel(check)).toBe('Check');
  });

  // Testzweck: Die Vorschau nennt nur noch die offenen Forderungen. Fiele die Zeile eines
  // bereits zugeordneten Knotens damit weg, ließe sich die Wahl nicht mehr ändern.
  it('behält die Zeile eines bereits zugeordneten Knotens', () => {
    const rows = migrationMappingRows([check], [{ source: review, targetId: 'Freigabe' }]);

    expect(rows.map((row) => row.source.id)).toEqual(['Check', 'Review']);
    expect(rows.map((row) => row.targetId)).toEqual(['', 'Freigabe']);
  });

  // Testzweck: Wer eine Zuordnung wieder leert, sieht den Knoten im nächsten Trockenlauf
  // erneut als Forderung. Beide Quellen dürfen daraus keine doppelte Zeile machen.
  it('führt einen Knoten aus Forderung und Wahl nur einmal', () => {
    const rows = migrationMappingRows([review], [{ source: review, targetId: '' }]);

    expect(rows).toHaveLength(1);
    expect(rows.map((row) => row.targetId)).toEqual(['']);
  });

  // Testzweck: Eine Aufgabe auf ein Gateway zu schieben ergäbe einen Zustand, den das
  // Zielmodell nicht kennt. Die Auswahl darf solche Ziele gar nicht erst anbieten.
  it('bietet nur Ziele derselben Elementart an', () => {
    const options = migrationTargetOptions(targets, { source: review, targetId: '' });

    expect(options.map((node) => node.id)).toEqual(['Freigabe']);
  });

  // Testzweck: Wird zwischendurch eine andere Version deployt, fehlt das gewählte Ziel in
  // der Auswahl. Die Zeile muss die Wahl trotzdem zeigen, statt stumm auf „nicht zuordnen“
  // zu springen — die API weist sie sonst erst hinterher mit `MappingTargetMissing` ab.
  it('zeigt auch ein Ziel, das die Zielversion nicht mehr kennt', () => {
    const options = migrationTargetOptions(targets, { source: review, targetId: 'Entfallen' });

    expect(options.map((node) => node.id)).toEqual(['Freigabe', 'Entfallen']);
  });

  // Testzweck: „nicht zuordnen“ ist keine Zuordnung. Ginge die leere Wahl an die API, wiese
  // sie den leeren Zielknoten ab, statt die Instanz schlicht unverändert zu lassen.
  it('sendet nur belegte Zuordnungen an die API', () => {
    const mapping = migrationFlowNodeMapping([
      { source: review, targetId: 'Freigabe' },
      { source: check, targetId: '' },
    ]);

    expect(mapping).toEqual({ Review: 'Freigabe' });
  });

  // Testzweck: Kennt die deployte Version den zugeordneten Knoten nicht, bleibt die Instanz
  // unverändert. Der Grund muss auf die Zuordnung zeigen, sonst sucht der Betrieb im Modell.
  it('erklärt einen fehlenden Zielknoten der Zuordnung', () => {
    const text = migrationFindingText({
      code: 'MappingTargetMissing', flowNodeId: 'Freigabe', message: 'Mapping target missing.',
    });

    expect(text).toContain('„Freigabe“');
    expect(text).toMatch(/zugeordnete/i);
    expect(text).not.toContain('missing');
  });
});
