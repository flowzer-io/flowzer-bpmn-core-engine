/** Ein Editorbesuch, nur im Arbeitsspeicher; kein globaler Store und kein Browser-Storage. */
export class WorkflowDraft {
  dirty = false;
  saving = false;
  revision = 0;
  xml: string;
  baseId: string;
  constructor(xml: string, baseId: string) { this.xml = xml; this.baseId = baseId; }
  change() { this.revision += 1; this.dirty = true; }
  capture(xml: string) { this.xml = xml; }
  saved(xml: string, baseId: string, submittedRevision: number) {
    this.baseId = baseId;
    if (this.revision !== submittedRevision) return;
    this.xml = xml;
    this.dirty = false;
  }
}

export function isEditorPath(pathname: string, definitionId: string): boolean {
  const match = /^\/workflows\/([^/]+)(?:\/gliederung)?\/?$/.exec(pathname);
  try { return Boolean(match && decodeURIComponent(match[1]!) === definitionId); }
  catch { return false; }
}
