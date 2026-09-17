import { instanceBucket } from '@/lib/api/normalize';
import type { InstanceMigrationFindingDto, ProcessInstanceInfoDto } from '@/lib/api/types';

/**
 * Regeln des Migrationsassistenten, die ohne React auskommen.
 *
 * Sie stehen hier und nicht in den Komponenten, weil sowohl die Instanzliste als auch
 * der Dialog dieselbe Auskunft brauchen — und weil die Oberfläche einen Grund nennen
 * soll, statt eine Schaltfläche nur stumm zu sperren.
 */

/** Ob die Instanz überhaupt als Quelle einer Migration in Frage kommt. */
export function isMigrationCandidate(
  instance: Pick<ProcessInstanceInfoDto, 'canInspect' | 'state'>,
): boolean {
  // `canInspect` ist genau die Betriebspolitik: Wer die Instanz nicht einsehen darf,
  // darf sie auch nicht migrieren — die API lehnt es sonst mit 403 ab.
  return instance.canInspect === true && instanceBucket(instance.state) === 'active';
}

export interface MigrationSelectionCandidate {
  relatedDefinitionId: string;
  definitionId: string;
}

/**
 * Prüft die Auswahl der Instanzliste gegen den Vertrag der Vorschau: ein Workflow, eine
 * Quellversion. Gibt `null` zurück, wenn nichts entgegensteht, sonst den Grund als Satz —
 * er landet als `title` an der gesperrten Schaltfläche.
 */
export function migrationSelectionProblem(selection: MigrationSelectionCandidate[]): string | null {
  const [first] = selection;
  if (!first) return 'Wähle mindestens eine laufende Instanz aus.';

  if (selection.some((entry) => entry.relatedDefinitionId !== first.relatedDefinitionId)) {
    return 'Die Auswahl enthält Instanzen verschiedener Workflows. Migrieren lässt sich nur ein Workflow auf einmal.';
  }

  if (selection.some((entry) => entry.definitionId !== first.definitionId)) {
    return 'Die Auswahl enthält Instanzen verschiedener Versionen. Migrieren lassen sich nur Instanzen derselben Ausgangsversion.';
  }

  return null;
}

/** Ein benannter Schritt in Anführungszeichen, sonst eine neutrale Umschreibung. */
function step(flowNodeId: string | null | undefined, fallback: string): string {
  return flowNodeId ? `„${flowNodeId}“` : fallback;
}

/**
 * Deutsche Sätze zu den Codes der API — an einer Stelle, damit Problem und Hinweis in
 * Liste und Ergebnis gleich lauten. Die Schlüssel folgen dem API-Vertrag; was dort neu
 * hinzukommt, fällt in `migrationFindingText` auf die technische Meldung zurück.
 */
const FINDING_TEXT: Record<string, (flowNodeId: string | null | undefined) => string> = {
  InstanceNotRunning: () => 'Die Instanz läuft nicht mehr und lässt sich deshalb nicht migrieren.',
  ProcessChanged: () =>
    'Die Zielversion enthält den Prozess dieser Instanz nicht mehr unter derselben Prozess-ID.',
  TokenNotAtRest: (node) =>
    `Die Instanz arbeitet gerade an ${step(node, 'einem Schritt')} und steht nicht still. Versuche es erneut, sobald sie wartet.`,
  FlowNodeMissing: (node) => `Der Schritt ${step(node, 'der laufenden Instanz')} fehlt in der Zielversion.`,
  FlowNodeTypeChanged: (node) => `Der Schritt ${step(node, 'der laufenden Instanz')} hat in der Zielversion eine andere Art.`,
  SubProcessNotSupported: (node) =>
    `Die Instanz steht in einem Unterprozess (${step(node, 'ohne benannten Schritt')}); Unterprozesse lassen sich noch nicht migrieren.`,
  MultiInstanceNotSupported: (node) =>
    `Der Schritt ${step(node, 'der laufenden Instanz')} läuft mehrfach (Multi-Instance); das lässt sich noch nicht migrieren.`,
  ServiceTaskTypeChanged: (node) =>
    `Der Service-Task ${step(node, 'der laufenden Instanz')} ruft in der Zielversion einen anderen Worker-Typ auf.`,
  AiTaskNotSupported: (node) => `KI-Aufgaben wie ${step(node, 'in dieser Instanz')} lassen sich noch nicht migrieren.`,
  BoundaryEventAlreadyTriggered: (node) =>
    `An ${step(node, 'einem Schritt')} hat bereits ein angeheftetes Ereignis (z. B. ein Timer) ausgelöst; die Migration würde es erneut scharf schalten.`,
  AlreadyOnTargetVersion: () => 'Die Instanz läuft bereits auf der Zielversion.',
  MigrationFailed: () =>
    'Die Migration ist an einem unerwarteten Fehler gescheitert. Die Instanz ist unverändert; Einzelheiten stehen im Protokoll der API.',
  TargetVersionChanged: () =>
    'Inzwischen wurde eine andere Version deployt; diese Instanz blieb unverändert. Prüfe die Auswahl erneut.',
  DraftStorageNotSupported: () =>
    'Die Ablage dieser Installation kann Entwürfe zu Aufgaben nicht mitnehmen; die Instanz blieb unverändert. Wende dich an den Betrieb.',

  UserTaskFormChanged: (node) =>
    `Die offene Aufgabe ${step(node, 'dieser Instanz')} wird künftig mit dem Formular der Zielversion bearbeitet.`,
  UserTaskDraftDiscarded: (node) =>
    `Der gespeicherte Entwurf zur Aufgabe ${step(node, 'dieser Instanz')} geht verloren, weil sich das Formular geändert hat.`,
  ServiceTaskJobInProgress: (node) =>
    `Ein Worker arbeitet gerade an ${step(node, 'einem Service-Task')}. Sein Ergebnis wird auch nach der Migration übernommen; der weitere Weg folgt dann der Zielversion.`,
  TimerRecalculated: (node) =>
    `Timer an ${step(node, 'einem Schritt')} rechnen nach der Migration mit der Dauer der Zielversion ab dem ursprünglichen Beginn des Wartens und können sofort fällig werden.`,
};

/** Übersetzt einen Befund der API in einen deutschen Satz. */
export function migrationFindingText(finding: InstanceMigrationFindingDto): string {
  const text = FINDING_TEXT[finding.code];
  return text ? text(finding.flowNodeId) : finding.message;
}
