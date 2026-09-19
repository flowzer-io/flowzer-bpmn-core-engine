import { ApiError } from '@/lib/api/client';
import type {
  InstanceModificationFindingDto,
  InstanceModificationStepDto,
  ModificationFlowNodeDto,
  ProcessVariables,
} from '@/lib/api/types';

/**
 * Regeln des Eingriffsdialogs, die ohne React auskommen.
 *
 * Wie beim Migrationsassistenten stehen sie neben der Komponente, damit die Oberfläche
 * einen Grund nennen kann, statt eine Schaltfläche nur stumm zu sperren — und damit sich
 * die Übersetzung der API-Codes an einer Stelle prüfen lässt.
 */

/**
 * Nur das, was sich gegenüber den vorbelegten Variablen geändert hat.
 *
 * Es ist dieselbe Frage wie bei der Korrektur eines Auftrags: Das Feld ist mit dem
 * aktuellen Stand vorbelegt, und unverändert abgeschickt wäre jeder Schlüssel eine
 * Änderung. Deshalb wird die dortige Regel benutzt und nicht abgeschrieben.
 */
export { changedCorrections as changedVariables } from '@/lib/jobRetry';

/** Eine getroffene Wahl: welcher wartende Schritt auf welchen Knoten soll. */
export interface ModificationChoice {
  tokenId: string;
  targetFlowNodeId: string;
}

/** Der Name des Schritts, sonst seine Knoten-Id — nicht jedes Modell benennt jeden Knoten. */
export function stepLabel(step: InstanceModificationStepDto): string {
  return step.name?.trim() ? step.name.trim() : step.flowNodeId;
}

/** Der Name des Knotens, sonst seine Id — gleiche Regel wie im Migrationsassistenten. */
export function flowNodeLabel(node: ModificationFlowNodeDto): string {
  return node.name?.trim() ? node.name.trim() : node.id;
}

/**
 * Die Verschiebungen für die Anfrage aus dem Stand des Dialogs.
 *
 * Ein Schritt ohne Ziel steht auf „belassen“ und ist keine Verschiebung: Er bleibt aus der
 * Anfrage heraus, damit der Trockenlauf dieselbe Anfrage sieht wie vor der ersten Wahl.
 * Die Reihenfolge hängt an der Token-Kennung, damit dieselbe Wahl dieselbe Anfrage ergibt.
 */
export function modificationMoves(selection: Record<string, string>): ModificationChoice[] {
  return Object.entries(selection)
    .filter(([, targetFlowNodeId]) => targetFlowNodeId.trim().length > 0)
    .map(([tokenId, targetFlowNodeId]) => ({ tokenId, targetFlowNodeId }))
    .sort((left, right) => left.tokenId.localeCompare(right.tokenId));
}

/** Eine Gruppe der Zielauswahl — wird im Dialog zu einer `<optgroup>`. */
export interface ModificationTargetGroup {
  label: string;
  targets: ModificationFlowNodeDto[];
}

/**
 * Deutsche Gruppennamen zu den Elementarten, die die API als `type` nennt.
 *
 * Die Namen stammen aus den Klassennamen der Engine (`UserTask`, `ExclusiveGateway`,
 * `FlowzerErrorEndEvent` …). Übersetzt wird nur, was hier wirklich steht; alles andere
 * behält seine Elementart als Gruppennamen, statt unter einer erfundenen Bezeichnung zu
 * verschwinden.
 */
const TARGET_GROUP_LABEL: Record<string, string> = {
  UserTask: 'Benutzeraufgaben',
  ServiceTask: 'Service-Tasks',
  ExclusiveGateway: 'Gateways',
  ParallelGateway: 'Gateways',
  InclusiveGateway: 'Gateways',
  EndEvent: 'Endereignisse',
  FlowzerErrorEndEvent: 'Endereignisse',
  FlowzerMessageEndEvent: 'Endereignisse',
  FlowzerSignalEndEvent: 'Endereignisse',
  FlowzerTerminateEvent: 'Endereignisse',
};

/** Die Reihenfolge der bekannten Gruppen — das Wahrscheinliche zuerst. */
const GROUP_ORDER = ['Benutzeraufgaben', 'Service-Tasks', 'Gateways', 'Endereignisse'];

/**
 * Die Zielknoten nach Elementart gruppiert.
 *
 * Eine flache Liste aus dreißig Knoten ist nicht zu überblicken; die Gruppe sagt vorab,
 * was für ein Schritt da gewählt wird. Bekannte Gruppen stehen in fester Reihenfolge
 * vorn, unbekannte Elementarten danach alphabetisch — innerhalb einer Gruppe nach ihrer
 * Beschriftung.
 */
export function modificationTargetGroups(
  targets: ModificationFlowNodeDto[],
): ModificationTargetGroup[] {
  const groups = new Map<string, ModificationFlowNodeDto[]>();
  for (const target of targets) {
    const label = TARGET_GROUP_LABEL[target.type] ?? target.type;
    const group = groups.get(label);
    if (group) group.push(target);
    else groups.set(label, [target]);
  }

  return [...groups.entries()]
    .map(([label, entries]) => ({
      label,
      targets: [...entries].sort((left, right) =>
        flowNodeLabel(left).localeCompare(flowNodeLabel(right), 'de'),
      ),
    }))
    .sort((left, right) => {
      const leftIndex = GROUP_ORDER.indexOf(left.label);
      const rightIndex = GROUP_ORDER.indexOf(right.label);
      if (leftIndex !== -1 || rightIndex !== -1) {
        return (leftIndex === -1 ? GROUP_ORDER.length : leftIndex)
          - (rightIndex === -1 ? GROUP_ORDER.length : rightIndex);
      }
      return left.label.localeCompare(right.label, 'de');
    });
}

export interface VariableDraftResult {
  variables?: ProcessVariables;
  error?: string;
}

/**
 * Liest das Variablenfeld des Eingriffsdialogs.
 *
 * Leer heißt „nichts setzen“, nicht „alles löschen“: Entfernt wird nur über die
 * Auswahlliste daneben. Ein Array oder ein blanker Wert wäre kein Variablenname-zu-Wert-Paar
 * und würde still nichts bewirken — deshalb wird er hier abgelehnt statt abgeschickt.
 */
export function parseVariableDraft(text: string): VariableDraftResult {
  if (text.trim().length === 0) return {};

  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { error: 'Das ist kein gültiges JSON. Bitte den Text prüfen.' };
  }

  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return {
      error: 'Erwartet wird ein JSON-Objekt aus Variablennamen und Werten, etwa { "betrag": 120 }.',
    };
  }

  return { variables: parsed as ProcessVariables };
}

/** Ein benannter Schritt in Anführungszeichen, sonst eine neutrale Umschreibung. */
function step(flowNodeId: string | null | undefined, fallback: string): string {
  return flowNodeId ? `„${flowNodeId}“` : fallback;
}

/**
 * Deutsche Sätze zu den Codes der API — an einer Stelle, damit Hindernis und Hinweis im
 * Trockenlauf und im Ergebnis gleich lauten. Die Schlüssel folgen dem API-Vertrag; was
 * dort neu hinzukommt, fällt in `modificationFindingText` auf die technische Meldung zurück.
 */
const FINDING_TEXT: Record<string, (flowNodeId: string | null | undefined) => string> = {
  InstanceNotRunning: () =>
    'Die Instanz läuft nicht mehr. An einem beendeten Vorgang lässt sich nichts mehr verschieben.',
  TokenMissing: () =>
    'Der genannte Schritt gehört nicht zu dieser Instanz. Öffne den Dialog erneut, damit die Liste wieder stimmt.',
  TokenNotMovable: (node) =>
    `Der Schritt ${step(node, 'dieser Instanz')} wartet nicht oder liegt in einem Unterprozess bzw. in einer mehrfach ausgeführten Aufgabe. Solche Schritte lassen sich noch nicht verschieben.`,
  DuplicateMove: (node) =>
    `Für den Schritt ${step(node, 'dieser Instanz')} wurden zwei Ziele genannt. Es geht nur eines.`,
  TargetMissing: (node) =>
    `Das Ziel ${step(node, 'des Eingriffs')} gibt es auf der obersten Ebene dieses Modells nicht.`,
  TargetNotAllowed: (node) =>
    `Das Ziel ${step(node, 'des Eingriffs')} ist ein Start- oder ein angeheftetes Ereignis. Solche Knoten werden nie über einen Sequenzfluss erreicht und lassen sich deshalb nicht betreten.`,
  VariableNameInvalid: () =>
    'Ein Variablenname ist leer oder benennt einen Pfad (etwa „a.b“ oder „a[0]“). Geändert werden können nur ganze Variablen der obersten Ebene.',
  ModificationFailed: () =>
    'Der Eingriff ist an einem unerwarteten Fehler gescheitert. Die Instanz ist unverändert; Einzelheiten stehen im Protokoll der API.',
  NothingToDo: () =>
    'Die Anfrage enthält weder eine Verschiebung noch eine Variablenänderung. Es gibt nichts zu tun.',
  RequestTooLarge: () =>
    'Die Anfrage nennt zu viele Verschiebungen oder Variablen auf einmal. Teile den Eingriff auf.',

  UserTaskCancelled: (node) =>
    `Die Aufgabe an ${step(node, 'dem verschobenen Schritt')} verschwindet samt Kennung, Übernahme und Fristen. Am Ziel beginnt gegebenenfalls eine neue Aufgabe mit neuer Kennung.`,
  ServiceTaskJobCancelled: (node) =>
    `Der Worker-Auftrag des Service-Tasks ${step(node, 'am verschobenen Schritt')} wird abgebrochen.`,
  // Die Ablage kennt zusätzlich den Auftrag, an dem gerade jemand arbeitet. Sein Ergebnis
  // wird nach dem Eingriff abgewiesen — das muss der Betrieb vorher wissen.
  ServiceTaskJobInProgress: (node) =>
    `An dem Service-Task ${step(node, 'des verschobenen Schritts')} arbeitet gerade ein Worker. Sein Ergebnis wird nach dem Eingriff nicht mehr angenommen.`,
  UserTaskDraftDiscarded: (node) =>
    `Ein privater Entwurf zur Aufgabe ${step(node, 'am verschobenen Schritt')} geht verloren.`,
  TimerRecalculated: (node) =>
    `Ein Timer an ${step(node, 'dem Ziel')} beginnt mit dem Eingriff von vorn und rechnet nicht ab dem ursprünglichen Beginn des Wartens.`,
  VariableNotFound: () =>
    'Eine Variable, die entfernt werden soll, gibt es nicht. Für sie geschieht nichts.',
};

/** Übersetzt einen Befund der API in einen deutschen Satz. */
export function modificationFindingText(finding: InstanceModificationFindingDto): string {
  const text = FINDING_TEXT[finding.code];
  return text ? text(finding.flowNodeId) : finding.message;
}

/**
 * Die Hindernisse aus der Ablehnung eines Eingriffs (422).
 *
 * Die Ausführung antwortet nicht mit einer nackten Meldung, sondern mit denselben Befunden
 * wie der Trockenlauf. Sie werden herausgelesen, damit die Ablehnung im Dialog dieselben
 * deutschen Sätze zeigt wie die Prüfung — und nicht bloß eine technische Zeile. Fehlen sie,
 * bleibt die Meldung der API (`detail` bzw. `errorMessage`) die Auskunft.
 */
export function modificationErrorProblems(error: unknown): InstanceModificationFindingDto[] {
  const body = error instanceof ApiError ? error.body : undefined;
  if (!body || typeof body !== 'object') return [];

  const problems = (body as { problems?: unknown }).problems;
  if (!Array.isArray(problems)) return [];

  return problems.filter(
    (entry): entry is InstanceModificationFindingDto =>
      typeof entry === 'object'
      && entry !== null
      && typeof (entry as InstanceModificationFindingDto).code === 'string'
      && typeof (entry as InstanceModificationFindingDto).message === 'string',
  );
}
