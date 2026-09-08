import { useEffect, useRef } from 'react';

import { ApiError } from '@/lib/api/client';

const messages = new Map(Object.entries({
  required: 'Dieses Feld ist erforderlich.',
  'type.number': 'Bitte eine Zahl angeben.',
  'type.boolean': 'Bitte einen gültigen Ja-/Nein-Wert angeben.',
  'type.string': 'Bitte Text statt eines strukturierten Werts angeben.',
  'type.scalar': 'Bitte einen einzelnen Wert angeben.',
  'type.array': 'Bitte eine Mehrfachauswahl angeben.',
  'type.subject_ref': 'Bitte eine gültige Benutzer- oder Gruppenreferenz auswählen.',
  'text.min_length': 'Die Eingabe ist zu kurz.',
  'text.max_length': 'Die Eingabe ist zu lang.',
  'text.pattern': 'Die Eingabe entspricht nicht dem vorgesehenen Format.',
  'text.email': 'Bitte eine gültige E-Mail-Adresse angeben.',
  'text.url': 'Bitte eine gültige HTTP-/HTTPS-Adresse angeben.',
  'number.min': 'Der Wert unterschreitet das erlaubte Minimum.',
  'number.max': 'Der Wert überschreitet das erlaubte Maximum.',
  'selection.invalid': 'Diese Auswahl ist nicht erlaubt.',
  'selection.duplicate': 'Dieselbe Benutzer- oder Gruppenreferenz darf nur einmal ausgewählt werden.',
  'selection.min': 'Bitte weitere Einträge auswählen.',
  'selection.max': 'Es sind zu viele Einträge ausgewählt.',
  'date.invalid': 'Bitte ein gültiges Datum angeben.',
  'time.invalid': 'Bitte eine gültige Uhrzeit angeben.',
  'date.order': 'Das Enddatum muss nach dem Anfangsdatum liegen; Gleichheit hängt von der Regel ab.',
  'field.read_only': 'Dieser Kontextwert darf nicht verändert werden.',
  'field.calculated': 'Dieser Wert wird ausschließlich vom Server berechnet.',
  'field.undeclared': 'Dieses Feld ist im veröffentlichten Formular nicht vorgesehen.',
  'field.inactive': 'Dieses Feld ist unter den aktuellen Bedingungen nicht freigegeben.',
  'directory.unavailable': 'Die ausgewählte Benutzer- oder Gruppenreferenz ist nicht mehr aktiv verfügbar.',
  'form.binding_missing': 'Der Formularstand muss vom Betrieb geklärt werden.',
  'form.contract_unsupported': 'Dieses Formular benötigt eine unterstützte serverseitige Regeldefinition.',
  'repeat.array': 'Bitte eine Liste von Einträgen angeben.',
  'repeat.row_object': 'Dieser Eintrag besitzt nicht die erwartete Feldstruktur.',
  'repeat.min': 'Bitte weitere Einträge hinzufügen.',
  'repeat.max': 'Die Liste enthält zu viele Einträge.',
}));

function labelsOf(schema?: string): Map<string, string> {
  const labels = new Map<string, string>();
  function visit(value: unknown, depth = 0) {
    if (!value || typeof value !== 'object' || depth > 24) return;
    if (Array.isArray(value)) { value.forEach(child => visit(child, depth + 1)); return; }
    const object = value as Record<string, unknown>;
    if (typeof object.key === 'string' && typeof object.label === 'string' && object.label.trim()) labels.set(object.key, object.label);
    for (const key of ['components', 'columns', 'rows']) visit(object[key], depth + 1);
  }
  try { visit(JSON.parse(schema ?? '{}') as unknown); } catch { /* Feldkennung bleibt lesbar. */ }
  return labels;
}

function labelOf(field: string, labels: Map<string, string>): string {
  const direct = labels.get(field);
  if (direct) return direct;
  const repeat = /^([A-Za-z][A-Za-z0-9_]*)\[(\d+)](?:\.([A-Za-z][A-Za-z0-9_]*))?$/.exec(field);
  if (!repeat) return field || 'Formular';
  const [, groupKey, indexText, childKey] = repeat;
  const group = labels.get(groupKey!) ?? groupKey!;
  const row = Number(indexText) + 1;
  return childKey ? `${group} ${row} – ${labels.get(childKey) ?? childKey}` : `${group} ${row}`;
}

/** Gemeinsame, wertefreie Fehleranzeige. Der Renderer und seine Eingaben bleiben bestehen. */
export function FormValidationErrors({ error, schema }: { error: unknown; schema?: string }) {
  const ref = useRef<HTMLElement>(null);
  const body = error instanceof ApiError ? error.body : null;
  const raw = body && typeof body === 'object' ? (body as Record<string, unknown>).errors : null;
  const entries = raw && typeof raw === 'object' && !Array.isArray(raw)
    ? Object.entries(raw).filter((entry): entry is [string, string[]] => Array.isArray(entry[1]) && entry[1].every(code => typeof code === 'string')).slice(0, 100)
    : [];
  const labels = labelsOf(schema);

  useEffect(() => { ref.current?.focus(); }, [error]);
  if (!entries.length) return null;
  return (
    <section ref={ref} role="alert" tabIndex={-1} className="border-fail/30 bg-fail/5 text-fail my-3 rounded-lg border p-4 text-sm outline-none focus:ring-2 focus:ring-current">
      <h3 className="font-semibold">Bitte die Angaben prüfen</h3>
      <ul className="mt-2 list-disc space-y-1 pl-5">
        {entries.map(([field, codes]) => (
          <li key={field}><strong>{labelOf(field, labels)}:</strong>{' '}
            {[...new Set(codes.map(code => messages.get(code) ?? 'Die Eingabe ist nicht zulässig.'))].join(' ')}
          </li>
        ))}
      </ul>
    </section>
  );
}
