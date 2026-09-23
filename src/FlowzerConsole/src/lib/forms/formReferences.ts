/** Auswahl einer unveränderlichen Formularversion für die Verwendung als Komponente. */
export interface FormReferenceSelection {
  formId: string;
  version: { major: number; minor: number };
  label?: string;
}

/**
 * Fügt nur einen serverseitig auflösbaren Marker ein. Das referenzierte Schema selbst wird
 * weder vom Browser geladen noch kopiert; Vorschau und Publish expandieren es auf dem Server.
 */
export function appendFormReference(schemaText: string, selection: FormReferenceSelection): string {
  let parsed: unknown;
  try { parsed = JSON.parse(schemaText); }
  catch { throw new Error('Das Formular-Schema ist kein JSON-Objekt.'); }
  if (!isRecord(parsed)) throw new Error('Das Formular-Schema ist kein JSON-Objekt.');

  const components = Array.isArray(parsed.components) ? parsed.components : [];
  const used = new Set<string>();
  collectKeys(components, used);
  components.push({
    type: 'flowzerForm',
    key: uniqueKey(slug(selection.label ?? 'formular'), used),
    label: selection.label ?? 'Formular',
    formId: selection.formId,
    version: `${selection.version.major}.${selection.version.minor}`,
    input: false,
    tableView: false,
  });
  parsed.components = components;
  return JSON.stringify(parsed, null, 2);
}

function uniqueKey(base: string, used: Set<string>): string {
  if (!used.has(base)) return base;
  for (let index = 2; index < 10_000; index += 1) {
    const candidate = `${base}_${index}`;
    if (!used.has(candidate)) return candidate;
  }
  throw new Error('Kein eindeutiger Feldschlüssel verfügbar.');
}

function slug(value: string): string {
  const normalized = value.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
    .replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
  return normalized || 'formular';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function collectKeys(value: unknown, keys: Set<string>): void {
  if (Array.isArray(value)) {
    value.forEach((entry) => collectKeys(entry, keys));
    return;
  }
  if (!isRecord(value)) return;
  if (typeof value.key === 'string' && value.key.length > 0) keys.add(value.key);
  Object.values(value).forEach((entry) => collectKeys(entry, keys));
}
