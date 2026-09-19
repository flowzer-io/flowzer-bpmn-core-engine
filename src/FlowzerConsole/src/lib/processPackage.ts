import type {
  ProcessPackageImportedFormDto,
  ProcessPackageMappingDto,
  ProcessPackagePreviewDto,
  ProcessPackageReferenceKind,
  ProcessPackageReferenceOptionsDto,
} from '@/lib/api/types';

/** Die Zielentscheidung, die der Dialog führt, bevor daraus ein `mapping` wird. */
export interface ImportTarget {
  mode: 'new' | 'newVersionOf';
  name: string;
  folderId: string | null;
}

/** Die Zuordnungen des Dialogs: je Bezugskennung die gewählte Kennung im Zielsystem. */
export type ReferenceChoices = Record<string, string>;

const KIND_LABELS: Record<ProcessPackageReferenceKind, string> = {
  directoryUser: 'Person',
  directoryGroup: 'Gruppe',
  aiConnection: 'KI-Verbindung',
  jobType: 'Auftragstyp',
  secret: 'Secret-Name',
  calledProcess: 'Aufgerufener Prozess',
  calledDecision: 'Aufgerufene Entscheidung',
};

/** Die Beschriftung einer Bezugsart in der Oberfläche. */
export function referenceKindLabel(kind: ProcessPackageReferenceKind): string {
  return KIND_LABELS[kind] ?? kind;
}

/**
 * Was mit einem Formular beim Import geschehen ist, in einem Satz.
 *
 * „Wiederverwendet“ heißt ausdrücklich, dass der vorhandene Stand unverändert bleibt —
 * ein Import darf kein fremdes Formular überschreiben.
 */
export function formOutcomeLabel(outcome: ProcessPackageImportedFormDto['outcome']): string {
  if (outcome === 'created') return 'neu angelegt';
  if (outcome === 'reused') return 'wiederverwendet';
  if (outcome === 'revised') return 'neue Fassung';
  return 'im Diagramm enthalten';
}

/** Nur die Bezüge, für die es überhaupt etwas zu entscheiden gibt. */
export function mappableReferences(
  preview: ProcessPackagePreviewDto,
): ProcessPackageReferenceOptionsDto[] {
  return preview.references.filter((option) => option.reference.requiresMapping);
}

/** Die Bezüge, die der Import nur nennt — sie brauchen keine Auswahl, wohl aber Beachtung. */
export function informationalReferences(
  preview: ProcessPackagePreviewDto,
): ProcessPackageReferenceOptionsDto[] {
  return preview.references.filter((option) => !option.reference.requiresMapping);
}

/**
 * Belegt die Zuordnungstabelle mit den Vorschlägen vor — dem gleichnamigen Eintrag dieser
 * Installation. Ein Bezug ohne Vorschlag bleibt bewusst leer: Lieber eine Lücke, die
 * auffällt, als eine stille Zuordnung auf irgendeinen Eintrag.
 */
export function suggestedChoices(preview: ProcessPackagePreviewDto): ReferenceChoices {
  const choices: ReferenceChoices = {};
  for (const option of mappableReferences(preview)) {
    if (option.suggestedId) choices[option.reference.id] = option.suggestedId;
  }
  return choices;
}

/**
 * Die Zielentscheidung, mit der der Dialog aufmacht.
 *
 * Steht die Kennung des Pakets hier schon und darf die Person dort bearbeiten, ist „neuer
 * Stand“ das Naheliegende — sonst entstünde ein zweiter Workflow mit demselben Namen.
 */
export function defaultTarget(
  preview: ProcessPackagePreviewDto,
  folderId: string | null,
): ImportTarget {
  const asNewVersion = preview.conflict?.mayCreateNewVersion === true;
  return {
    mode: asNewVersion ? 'newVersionOf' : 'new',
    name: preview.manifest.workflow.name,
    folderId,
  };
}

/** Bezüge, die noch auf eine Auswahl warten. Der Import verlangt sie ausdrücklich. */
export function unresolvedReferences(
  preview: ProcessPackagePreviewDto,
  choices: ReferenceChoices,
): ProcessPackageReferenceOptionsDto[] {
  return mappableReferences(preview).filter(
    (option) => (choices[option.reference.id] ?? '').length === 0,
  );
}

/**
 * Ob importiert werden darf. Ein Modell, das diese Installation nicht ausführen kann, hält
 * den Import <em>nicht</em> auf: Es kommt als Entwurf an und lässt sich hier zu Ende bringen.
 * Eine fehlende Zuordnung hält ihn dagegen auf — die kann niemand erraten.
 */
export function canImport(
  preview: ProcessPackagePreviewDto,
  choices: ReferenceChoices,
  target: ImportTarget,
): boolean {
  if (unresolvedReferences(preview, choices).length > 0) return false;
  if (target.mode === 'newVersionOf') return preview.conflict?.mayCreateNewVersion === true;
  return target.name.trim().length > 0;
}

/** Baut aus Zielentscheidung und Auswahl das, was die API als `mapping` erwartet. */
export function buildMapping(
  preview: ProcessPackagePreviewDto,
  choices: ReferenceChoices,
  target: ImportTarget,
): ProcessPackageMappingDto {
  if (target.mode === 'newVersionOf') {
    return {
      mode: 'newVersionOf',
      definitionId: preview.conflict?.definitionId ?? preview.manifest.workflow.definitionId,
      references: choices,
    };
  }

  return {
    mode: 'new',
    // Die Kennung aus dem Paket nur dann, wenn sie hier frei ist; sonst vergibt die API eine
    // neue. Eine belegte Kennung mitzuschicken hiesse, den Import sehenden Auges abzulehnen.
    definitionId: preview.conflict ? null : preview.manifest.workflow.definitionId,
    folderId: target.folderId,
    name: target.name.trim(),
    references: choices,
  };
}
