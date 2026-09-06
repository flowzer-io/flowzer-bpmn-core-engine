/**
 * Bearbeiten der Gliederung: Schritte aendern, hinzufuegen, verschieben, loeschen.
 *
 * Alle Funktionen erzeugen ein neues Dokument; nichts wird an Ort und Stelle
 * geaendert. Bloecke werden ueber ihre BPMN-Kennung angesprochen, weil die in
 * einem Dokument eindeutig ist und die Oberflaeche sie ohnehin fuehrt.
 */
import {
  allBlocks,
  type OutlineBlock,
  type OutlineChoice,
  type OutlineChoiceBranch,
  type OutlineDocument,
  type OutlineParallel,
  type OutlineStep,
  type TaskKind,
} from './model';

type SequenceTransform = (blocks: readonly OutlineBlock[]) => readonly OutlineBlock[];
type BranchTransform = (blocks: readonly OutlineBlock[], branchIndex: number) => readonly OutlineBlock[];

function isContainer(block: OutlineBlock): block is OutlineParallel | OutlineChoice {
  return block.kind === 'parallel' || block.kind === 'choice';
}

/** Ersetzt die Bloecke eines Zweigs, ohne den Typ des umgebenden Blocks zu verlieren. */
function withBranchBlocks(block: OutlineParallel | OutlineChoice, transform: BranchTransform): OutlineBlock {
  if (block.kind === 'parallel') {
    return {
      ...block,
      branches: block.branches.map((branch, index) => ({ ...branch, blocks: transform(branch.blocks, index) })),
    };
  }
  return {
    ...block,
    branches: block.branches.map((branch, index) => ({ ...branch, blocks: transform(branch.blocks, index) })),
  };
}

/** Wendet `transform` auf jede Folge im Baum an — von innen nach aussen. */
function rewriteSequences(blocks: readonly OutlineBlock[], transform: SequenceTransform): readonly OutlineBlock[] {
  const rewritten = blocks.map((block) =>
    isContainer(block) ? withBranchBlocks(block, (inner) => rewriteSequences(inner, transform)) : block,
  );
  return transform(rewritten);
}

function withBlocks(document: OutlineDocument, transform: SequenceTransform): OutlineDocument {
  return { ...document, blocks: rewriteSequences(document.blocks, transform) };
}

/** Wendet `change` auf genau den Block mit dieser Kennung an. */
function mapBlock(
  document: OutlineDocument,
  id: string,
  change: (block: OutlineBlock) => OutlineBlock,
): OutlineDocument {
  return withBlocks(document, (blocks) => blocks.map((block) => (block.id === id ? change(block) : block)));
}

/** Eine im Dokument noch unbenutzte Kennung. `reserved` deckt Kennungen ab, die im selben Zug entstehen. */
export function freeId(document: OutlineDocument, prefix: string, reserved: readonly string[] = []): string {
  const used = new Set<string>([document.startId, ...reserved]);
  for (const block of allBlocks(document.blocks)) {
    used.add(block.id);
    if (block.kind === 'parallel') used.add(block.joinId);
    if (block.kind === 'choice' && block.joinId) used.add(block.joinId);
  }

  let counter = 1;
  while (used.has(`${prefix}_${counter}`)) counter++;
  return `${prefix}_${counter}`;
}

export function newStep(document: OutlineDocument, task: TaskKind): OutlineStep {
  return {
    kind: 'step',
    id: freeId(document, task === 'user' ? 'UserTask' : 'ServiceTask'),
    name: task === 'user' ? 'Neuer Schritt' : 'Neuer Dienstaufruf',
    task,
    inputs: [],
    outputs: [],
  };
}

export function newChoice(document: OutlineDocument): OutlineChoice {
  const id = freeId(document, 'Gateway');
  return {
    kind: 'choice',
    id,
    name: 'Neue Verzweigung',
    branches: [
      { label: 'ja', condition: '=bedingung = "ja"', isDefault: false, blocks: [] },
      { label: 'sonst', isDefault: true, blocks: [] },
    ],
  };
}

export function newParallel(document: OutlineDocument): OutlineParallel {
  const forkId = freeId(document, 'Gateway');
  return {
    kind: 'parallel',
    id: forkId,
    joinId: freeId(document, 'Gateway', [forkId]),
    name: 'Gleichzeitig',
    branches: [{ blocks: [] }, { blocks: [] }],
  };
}

/** Setzt einen Block hinter den Block mit der Kennung `afterId`. */
export function insertAfter(document: OutlineDocument, afterId: string, block: OutlineBlock): OutlineDocument {
  return withBlocks(document, (blocks) => {
    const index = blocks.findIndex((candidate) => candidate.id === afterId);
    if (index < 0) return blocks;
    return [...blocks.slice(0, index + 1), block, ...blocks.slice(index + 1)];
  });
}

/** Setzt einen Block vor den Block mit der Kennung `beforeId`. */
export function insertBefore(document: OutlineDocument, beforeId: string, block: OutlineBlock): OutlineDocument {
  return withBlocks(document, (blocks) => {
    const index = blocks.findIndex((candidate) => candidate.id === beforeId);
    if (index < 0) return blocks;
    return [...blocks.slice(0, index), block, ...blocks.slice(index)];
  });
}

/** Setzt einen Block an den Anfang eines Zweigs. */
export function insertIntoBranch(
  document: OutlineDocument,
  containerId: string,
  branchIndex: number,
  block: OutlineBlock,
): OutlineDocument {
  return mapBlock(document, containerId, (container) =>
    isContainer(container)
      ? withBranchBlocks(container, (blocks, index) => (index === branchIndex ? [block, ...blocks] : blocks))
      : container,
  );
}

export function removeBlock(document: OutlineDocument, id: string): OutlineDocument {
  return withBlocks(document, (blocks) => blocks.filter((block) => block.id !== id));
}

export function moveBlock(document: OutlineDocument, id: string, direction: 'up' | 'down'): OutlineDocument {
  return withBlocks(document, (blocks) => {
    const index = blocks.findIndex((block) => block.id === id);
    const target = direction === 'up' ? index - 1 : index + 1;
    if (index < 0 || target < 0 || target >= blocks.length) return blocks;

    const moved = [...blocks];
    const here = moved[index];
    const there = moved[target];
    if (!here || !there) return blocks;

    moved[index] = there;
    moved[target] = here;
    return moved;
  });
}

/** Kann der Block in dieser Richtung noch verschoben werden? */
export function canMove(document: OutlineDocument, id: string, direction: 'up' | 'down'): boolean {
  for (const sequence of sequencesOf(document.blocks)) {
    const index = sequence.findIndex((block) => block.id === id);
    if (index < 0) continue;
    return direction === 'up' ? index > 0 : index < sequence.length - 1;
  }
  return false;
}

function sequencesOf(blocks: readonly OutlineBlock[]): readonly OutlineBlock[][] {
  const sequences: OutlineBlock[][] = [[...blocks]];
  for (const block of blocks) {
    if (!isContainer(block)) continue;
    for (const branch of block.branches) sequences.push(...sequencesOf(branch.blocks));
  }
  return sequences;
}

export function updateStep(document: OutlineDocument, id: string, patch: Partial<OutlineStep>): OutlineDocument {
  return mapBlock(document, id, (block) => (block.kind === 'step' ? { ...block, ...patch } : block));
}

export function renameBlock(document: OutlineDocument, id: string, name: string): OutlineDocument {
  return mapBlock(document, id, (block) => {
    switch (block.kind) {
      case 'step':
        return { ...block, name };
      case 'end':
        return { ...block, name };
      case 'parallel':
        return { ...block, name };
      case 'choice':
        return { ...block, name };
    }
  });
}

export function updateBranch(
  document: OutlineDocument,
  choiceId: string,
  branchIndex: number,
  patch: Partial<OutlineChoiceBranch>,
): OutlineDocument {
  return mapBlock(document, choiceId, (block) => {
    if (block.kind !== 'choice') return block;
    const branches = block.branches.map((branch, index) => (index === branchIndex ? { ...branch, ...patch } : branch));
    // Genau ein Standardweg: Wird einer gesetzt, verlieren die anderen die Markierung.
    const normalized = patch.isDefault
      ? branches.map((branch, index) => (index === branchIndex ? branch : { ...branch, isDefault: false }))
      : branches;
    return { ...block, branches: normalized };
  });
}

export function addBranch(document: OutlineDocument, containerId: string): OutlineDocument {
  return mapBlock(document, containerId, (container) => {
    if (container.kind === 'parallel') {
      return { ...container, branches: [...container.branches, { blocks: [] }] };
    }
    if (container.kind === 'choice') {
      return {
        ...container,
        branches: [...container.branches, { label: 'weiterer Fall', condition: '', isDefault: false, blocks: [] }],
      };
    }
    return container;
  });
}

export function removeBranch(document: OutlineDocument, containerId: string, branchIndex: number): OutlineDocument {
  return mapBlock(document, containerId, (container) => {
    if (!isContainer(container) || container.branches.length <= 2) return container;
    if (container.kind === 'parallel') {
      return { ...container, branches: container.branches.filter((_, index) => index !== branchIndex) };
    }
    return { ...container, branches: container.branches.filter((_, index) => index !== branchIndex) };
  });
}
