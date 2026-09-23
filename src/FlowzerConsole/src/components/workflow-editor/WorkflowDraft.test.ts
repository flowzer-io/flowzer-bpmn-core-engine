import { describe, expect, it } from 'vitest';
import { WorkflowDraft, isEditorPath } from './WorkflowDraft';

describe('Gemeinsamer Workflow-Arbeitsstand', () => {
  // Testzweck: Ein Ansichtswechsel übernimmt XML, erzeugt aber weder eine Serverversion
  // noch einen fälschlich sauberen Zustand.
  it('erhält Änderungen und Ausgangsversion beim Ansichtswechsel', () => {
    const draft = new WorkflowDraft('<original/>', 'version-1');
    draft.change();
    draft.capture('<edited/>');
    expect(draft.xml).toBe('<edited/>');
    expect(draft.dirty).toBe(true);
    expect(draft.baseId).toBe('version-1');
  });
  // Testzweck: Nur die tatsächlich bestätigte Revision darf die Verlustwarnung entfernen.
  it('entfernt dirty nach bestätigtem Speichern desselben Standes', () => {
    const draft = new WorkflowDraft('<original/>', 'version-1');
    draft.change();
    draft.saved('<saved/>', 'version-2', draft.revision);
    expect(draft.dirty).toBe(false);
    expect(draft.xml).toBe('<saved/>');
  });
  // Testzweck: Während des Requests entstandene Eingaben bleiben auch bei späterem Erfolg erhalten.
  it('überschreibt neuere Änderungen nicht mit einer verspäteten Speicherantwort', () => {
    const draft = new WorkflowDraft('<original/>', 'version-1');
    draft.change();
    const sent = draft.revision;
    draft.change();
    draft.capture('<newer/>');
    draft.saved('<older/>', 'version-2', sent);
    expect(draft.dirty).toBe(true);
    expect(draft.xml).toBe('<newer/>');
    expect(draft.baseId).toBe('version-2');
  });
  // Testzweck: Nur die zwei Ansichten desselben Workflows dürfen ohne Verlustwarnung wechseln.
  it('grenzt Ansichtstausch gegen andere Workflows und Routen ab', () => {
    expect(isEditorPath('/workflows/one/gliederung', 'one')).toBe(true);
    expect(isEditorPath('/workflows/one/', 'one')).toBe(true);
    expect(isEditorPath('/workflows/other', 'one')).toBe(false);
    expect(isEditorPath('/workflows/one/unknown', 'one')).toBe(false);
    expect(isEditorPath('/workflows', 'one')).toBe(false);
  });
});
