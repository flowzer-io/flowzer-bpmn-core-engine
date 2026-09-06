/**
 * Der Form-Key verbindet eine menschliche Aufgabe mit ihrem Formular. Er steht im BPMN
 * unter `zeebe:formDefinition/@formKey` und kennt zwei Fälle:
 *
 * - **Aus dem Bestand:** `Urlaubsantrag` (neueste Version) oder `Urlaubsantrag:1.0`
 *   (genau diese Version).
 * - **In diesem Workflow:** `camunda-forms:bpmn:Kennung` — das Formular liegt als
 *   `zeebe:userTaskForm` im Diagramm selbst und ist damit mit dem Workflow versioniert.
 *
 * Dieselbe Aufteilung nimmt die Engine vor (`UserTaskFormResolver`); die Schreibweise ist
 * bewusst Camundas, damit ein im Camunda Modeler erstellter Workflow ohne Umbau läuft.
 */

export const EMBEDDED_FORM_PREFIX = 'camunda-forms:bpmn:';

export type FormReference =
  | { kind: 'none' }
  | { kind: 'embedded'; formId: string }
  | { kind: 'stored'; name: string; version: string | null };

/** Der Form-Key, unter dem eine Aufgabe auf ein Formular im Workflow verweist. */
export function embeddedFormKey(formId: string): string {
  return `${EMBEDDED_FORM_PREFIX}${formId}`;
}

/** Der Form-Key für ein Formular aus dem Bestand; ohne Version gilt die neueste. */
export function storedFormKey(name: string, version?: string | null): string {
  const trimmedVersion = version?.trim();
  return trimmedVersion ? `${name.trim()}:${trimmedVersion}` : name.trim();
}

/**
 * Zerlegt einen Form-Key. Die Version wird nur abgetrennt, wenn das Suffix wirklich wie
 * eine Version aussieht — ein Formularname darf selbst Doppelpunkte enthalten
 * („Prüfung: Detail"). Die Engine trennt nach derselben Regel.
 */
export function parseFormKey(formKey: string | null | undefined): FormReference {
  const key = formKey?.trim();
  if (!key) return { kind: 'none' };

  if (key.startsWith(EMBEDDED_FORM_PREFIX)) {
    return { kind: 'embedded', formId: key.slice(EMBEDDED_FORM_PREFIX.length).trim() };
  }

  const separator = key.lastIndexOf(':');
  if (separator < 0) return { kind: 'stored', name: key, version: null };

  const suffix = key.slice(separator + 1).trim();
  if (suffix.length === 0) return { kind: 'stored', name: key.slice(0, separator).trim(), version: null };
  if (!/^[0-9.]+$/.test(suffix)) return { kind: 'stored', name: key, version: null };

  return { kind: 'stored', name: key.slice(0, separator).trim(), version: suffix };
}

/** Beschriftung eines Form-Keys für Listen und Chips. */
export function describeFormKey(formKey: string | null | undefined): string {
  const reference = parseFormKey(formKey);

  if (reference.kind === 'none') return 'Kein Formular';
  if (reference.kind === 'embedded') return reference.formId || 'Formular ohne Kennung';
  return reference.version ? `${reference.name} · v${reference.version}` : reference.name;
}

/**
 * Eine neue Kennung für ein Formular im Workflow. Sie landet als XML-Attribut im Diagramm
 * und muss deshalb ohne Sonderzeichen auskommen; der Zufallsanteil hält sie eindeutig,
 * auch wenn zwei Aufgaben denselben Namen tragen.
 */
export function newEmbeddedFormId(label: string): string {
  const stem =
    label
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-zA-Z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .slice(0, 40) || 'Formular';

  return `Form_${stem}_${Math.random().toString(36).slice(2, 8)}`;
}
