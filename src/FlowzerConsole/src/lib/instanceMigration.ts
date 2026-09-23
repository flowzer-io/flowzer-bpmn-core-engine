import { instanceBucket } from '@/lib/api/normalize';
import type {
  InstanceMigrationFindingDto,
  MigrationFlowNodeDto,
  ProcessInstanceInfoDto,
} from '@/lib/api/types';

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

/** „3 Instanzen“, „1 Instanz“ — die Einzahl gehört zum deutschen Text dazu. */
export function instanceCountLabel(count: number): string {
  return `${count} ${count === 1 ? 'Instanz' : 'Instanzen'}`;
}

/** Eine Zeile des Zuordnungsblocks: Quellknoten und das gewählte Ziel (leer = keines). */
export interface MigrationMappingChoice {
  source: MigrationFlowNodeDto;
  targetId: string;
}

/** Der Name des Knotens, sonst seine Id — nicht jedes Modell benennt jeden Knoten. */
export function flowNodeLabel(node: MigrationFlowNodeDto): string {
  return node.name?.trim() ? node.name.trim() : node.id;
}

/**
 * Die Zeilen des Zuordnungsblocks aus offenen Forderungen und bereits getroffener Wahl.
 *
 * Die Vorschau nennt nur noch die Knoten **ohne** Ziel. Ein zugeordneter Knoten fiele damit
 * aus der Liste und ließe sich nicht mehr ändern — deshalb stehen die getroffenen Wahlen
 * gleichberechtigt daneben. Beide Quellen können denselben Knoten nennen; er bleibt eine
 * Zeile, und die Reihenfolge hängt am Namen, damit keine Zeile beim Wählen springt.
 */
export function migrationMappingRows(
  required: MigrationFlowNodeDto[],
  choices: MigrationMappingChoice[],
): MigrationMappingChoice[] {
  const rows = new Map<string, MigrationMappingChoice>();
  for (const source of required) rows.set(source.id, { source, targetId: '' });
  for (const choice of choices) rows.set(choice.source.id, choice);

  return [...rows.values()].sort((left, right) =>
    flowNodeLabel(left.source).localeCompare(flowNodeLabel(right.source), 'de'),
  );
}

/**
 * Die wählbaren Ziele einer Zeile: nur Knoten derselben Elementart.
 *
 * Eine Aufgabe auf ein Gateway zu schieben ergäbe einen Zustand, den das Zielmodell nicht
 * kennt. Ein bereits gewähltes Ziel bleibt in der Liste, auch wenn die Zielversion es
 * inzwischen nicht mehr kennt — sonst zeigte die Auswahl „nicht zuordnen“ an, während die
 * Anfrage weiter das fehlende Ziel trägt.
 */
export function migrationTargetOptions(
  targets: MigrationFlowNodeDto[],
  choice: MigrationMappingChoice,
): MigrationFlowNodeDto[] {
  const options = targets.filter((target) => target.type === choice.source.type);
  if (!choice.targetId || options.some((target) => target.id === choice.targetId)) return options;

  return [...options, { id: choice.targetId, name: null, type: choice.source.type }];
}

/** Die Zuordnung für die API — leere Wahlen sind keine Zuordnung und bleiben draußen. */
export function migrationFlowNodeMapping(
  choices: MigrationMappingChoice[],
): Record<string, string> {
  return Object.fromEntries(
    choices.filter((choice) => choice.targetId).map((choice) => [choice.source.id, choice.targetId]),
  );
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
  CallActivityWaiting: (node) =>
    `Der Schritt ${step(node, 'der laufenden Instanz')} wartet auf einen aufgerufenen Vorgang; solange der läuft, lässt sich diese Instanz nicht migrieren. Der aufgerufene Vorgang selbst ist migrierbar.`,
  BoundaryEventAlreadyTriggered: (node) =>
    `An ${step(node, 'einem Schritt')} hat bereits ein angeheftetes Ereignis (z. B. ein Timer) ausgelöst; die Migration würde es erneut scharf schalten.`,
  AlreadyOnTargetVersion: () => 'Die Instanz läuft bereits auf der Zielversion.',
  // Der Befund nennt den wartenden Schritt, nicht das fehlende Ziel: Nur so findet der
  // Betrieb die Zeile wieder, in der er zuordnen muss.
  MappingTargetMissing: (node) =>
    `Das Ziel, das dem Schritt ${step(node, 'dieser Instanz')} zugeordnet wurde, gibt es in der deployten Version nicht. Ordne ihn erneut zu.`,
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
