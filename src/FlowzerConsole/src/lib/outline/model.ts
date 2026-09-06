/**
 * Datenmodell der Gliederungsansicht.
 *
 * Die Gliederung ist eine zweite Oberflaeche neben dem Diagramm: ein Workflow als
 * senkrechte Folge von Bloecken. Welchen Ausschnitt von BPMN sie abbildet und was
 * mit allem anderen passiert, steht in `docs/GLIEDERUNG-TEILMENGE.md`.
 */

/** Ein Schritt ist entweder eine Aufgabe fuer Menschen oder ein Aufruf an einen Dienst. */
export type TaskKind = 'user' | 'service';

/** Eine Zeile aus `zeebe:ioMapping` — Quelle im Prozess, Name im Schritt. */
export interface IoMapping {
  readonly source: string;
  readonly target: string;
}

/** Ein einzelner Prozessschritt. */
export interface OutlineStep {
  readonly kind: 'step';
  readonly id: string;
  readonly name: string;
  readonly task: TaskKind;
  /** Nur `user`: Formularschluessel aus `zeebe:formDefinition`. */
  readonly formKey?: string;
  /** Nur `user`: Formularkennung, falls das Modell `formId` statt `formKey` nutzt. */
  readonly formId?: string;
  readonly assignee?: string;
  readonly candidateGroups?: string;
  readonly candidateUsers?: string;
  readonly dueDate?: string;
  readonly followUpDate?: string;
  /** Nur `service`: Typ aus `zeebe:taskDefinition`. */
  readonly workerType?: string;
  readonly retries?: string;
  readonly inputs: readonly IoMapping[];
  readonly outputs: readonly IoMapping[];
}

/** Ein Zweig eines Blocks: die Bloecke darin und die Kennung des hinfuehrenden Flusses. */
export interface OutlineBranch {
  readonly flowId?: string;
  readonly blocks: readonly OutlineBlock[];
}

/** Ein Zweig einer Verzweigung, zusaetzlich mit Beschriftung und Bedingung. */
export interface OutlineChoiceBranch extends OutlineBranch {
  readonly label?: string;
  readonly condition?: string;
  /** Der Standardweg des Tores (`default`) — er traegt nie eine Bedingung. */
  readonly isDefault: boolean;
}

/** Mehrere Zweige laufen gleichzeitig und treffen sich wieder (paralleles Tor). */
export interface OutlineParallel {
  readonly kind: 'parallel';
  readonly id: string;
  readonly joinId: string;
  readonly name?: string;
  readonly joinName?: string;
  readonly branches: readonly OutlineBranch[];
}

/**
 * Genau ein Zweig laeuft weiter (exklusives Tor).
 *
 * Ein Zweig endet entweder mit einem eigenen Ende oder laeuft dort weiter, wo die
 * Verzweigung in der uebergeordneten Folge zu Ende ist. `joinId` steht nur, wenn
 * die Zweige sich an einem eigenen Zusammenfuehrungs-Tor treffen.
 */
export interface OutlineChoice {
  readonly kind: 'choice';
  readonly id: string;
  readonly name?: string;
  readonly joinId?: string;
  readonly joinName?: string;
  readonly branches: readonly OutlineChoiceBranch[];
}

/** Ein Ende-Ereignis. */
export interface OutlineEnd {
  readonly kind: 'end';
  readonly id: string;
  readonly name: string;
}

export type OutlineBlock = OutlineStep | OutlineParallel | OutlineChoice | OutlineEnd;

/** Ein vollstaendig gelesener Workflow als Gliederung. */
export interface OutlineDocument {
  readonly definitionsId: string;
  readonly targetNamespace?: string;
  /** Wer die Datei zuletzt geschrieben hat; bleibt beim Speichern stehen. */
  readonly exporter?: string;
  readonly exporterVersion?: string;
  readonly processId: string;
  readonly processName?: string;
  readonly startId: string;
  readonly startName?: string;
  readonly blocks: readonly OutlineBlock[];
  /**
   * Kennungen der vorhandenen Sequenzfluesse, abgelegt unter „Quelle->Ziel".
   * Ohne sie bekaeme jeder Speichervorgang neue Flusskennungen, und das
   * vorhandene Diagramm liesse sich nicht wiederverwenden.
   */
  readonly flowIds: Readonly<Record<string, string>>;
  /** Das Diagramm der Vorlage, unveraendert — solange die Struktur gleich bleibt, wird es weitergereicht. */
  readonly sourceDiagram?: string;
  /**
   * Fingerabdruck der Ausgangsstruktur ohne Namen, Formulare, Zuweisungen und
   * Fristen. Er entscheidet, ob `sourceDiagram` noch passt: Wer nur eine Frist
   * aendert, soll die Anordnung im Diagramm nicht verlieren.
   */
  readonly sourceStructure?: string;
}

/**
 * `blocker` sperrt das Speichern: Die Gliederung bildet das Modell nicht
 * vollstaendig ab. `hinweis` ist eine Nebenwirkung, ueber die der Nutzer
 * Bescheid wissen soll, die das Speichern aber nicht verhindert.
 */
export type OutlineIssueLevel = 'blocker' | 'hinweis';

export interface OutlineIssue {
  readonly level: OutlineIssueLevel;
  readonly message: string;
  readonly elementId?: string;
}

export interface OutlineReadResult {
  readonly document?: OutlineDocument;
  readonly issues: readonly OutlineIssue[];
}

export function hasBlocker(issues: readonly OutlineIssue[]): boolean {
  return issues.some((issue) => issue.level === 'blocker');
}

/** Lesbare Ueberschrift eines Blocks — auch fuer Bloecke ohne eigenen Namen. */
export function blockLabel(block: OutlineBlock): string {
  switch (block.kind) {
    case 'step':
      return block.name.trim() || block.id;
    case 'end':
      return block.name.trim() || 'Ende';
    case 'parallel':
      return block.name?.trim() || 'Gleichzeitig';
    case 'choice':
      return block.name?.trim() || 'Verzweigung';
  }
}

/** Beschriftung eines Verzweigungszweigs: Name des Flusses, sonst Bedingung, sonst „sonst". */
export function branchLabel(branch: OutlineChoiceBranch): string {
  const label = branch.label?.trim();
  if (label) return label;
  if (branch.isDefault || !branch.condition) return 'sonst';
  return branch.condition;
}

/** Alle Bloecke eines Dokuments in Lesereihenfolge, inklusive der Bloecke in Zweigen. */
export function allBlocks(blocks: readonly OutlineBlock[]): OutlineBlock[] {
  const collected: OutlineBlock[] = [];

  const walk = (list: readonly OutlineBlock[]) => {
    for (const block of list) {
      collected.push(block);
      if (block.kind === 'parallel' || block.kind === 'choice') {
        for (const branch of block.branches) walk(branch.blocks);
      }
    }
  };

  walk(blocks);
  return collected;
}

export function findBlock(blocks: readonly OutlineBlock[], id: string): OutlineBlock | undefined {
  return allBlocks(blocks).find((block) => block.id === id);
}
