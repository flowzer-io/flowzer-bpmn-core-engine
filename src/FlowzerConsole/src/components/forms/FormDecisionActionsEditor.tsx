import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import type {
  FormDecisionActionDraft,
  FormDecisionActionValue,
  FormDecisionActionVariant,
} from '@/lib/forms/formDecisionActions';

interface FormDecisionActionsEditorProps {
  actions: FormDecisionActionDraft[];
  onChange: (actions: FormDecisionActionDraft[]) => void;
}

function valueKind(value: FormDecisionActionValue): 'string' | 'number' | 'boolean' | 'null' {
  return value === null ? 'null' : typeof value as 'string' | 'number' | 'boolean';
}

function initialValue(kind: ReturnType<typeof valueKind>): FormDecisionActionValue {
  if (kind === 'number') return 0;
  if (kind === 'boolean') return false;
  if (kind === 'null') return null;
  return '';
}

/** Begrenzte Root-Konfiguration für Human-Task-Aktionen; Veröffentlichung prüft endgültig. */
export function FormDecisionActionsEditor({ actions, onChange }: FormDecisionActionsEditorProps) {
  // Gelöschte Knöpfe dürfen beim erneuten Hinzufügen keine vorhandene ID duplizieren.
  let nextNumber = 1;
  while (actions.some(action => action.id === `action_${nextNumber}`)) nextNumber += 1;
  function updateAction(index: number, next: FormDecisionActionDraft) {
    onChange(actions.map((action, candidate) => candidate === index ? next : action));
  }

  return (
    <section className="border-border bg-surface mt-4 rounded-[var(--r)] border p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">Abschlussknöpfe</h3>
          <p className="text-muted mt-1 max-w-[720px] text-xs leading-normal">
            Welche Knöpfe soll die bearbeitende Person am Ende der Aufgabe sehen?
            Zum Beispiel „Genehmigen“ oder „Ablehnen“. Ein Klick schließt die Aufgabe ab –
            diese Knöpfe sind nicht „Weiter“ oder „Zurück“ innerhalb des Formulars.
          </p>
        </div>
        <Button
          size="sm"
          variant="secondary"
          icon="add"
          disabled={actions.length >= 20}
          onClick={() => onChange([...actions, {
            id: `action_${nextNumber}`,
            label: `Aktion ${nextNumber}`,
            variant: 'secondary',
            set: [{ field: 'decision', value: `value_${nextNumber}` }],
          }])}
        >
          Aktion hinzufügen
        </Button>
      </div>

      {actions.length === 0 && (
        <div className="border-border text-muted mt-3 rounded-[var(--r-sm)] border border-dashed px-3 py-4 text-center text-xs">
          Ohne eigene Knöpfe erscheint „Aufgabe abschließen“. Für Startformulare bleibt „Workflow starten“ zuständig.
        </div>
      )}

      {actions.length > 0 && <div className="mt-4 rounded-[var(--r-sm)] border border-dashed border-border p-3">
        <p className="text-muted mb-2 text-xs">So sehen die Abschlussknöpfe aus (nur Vorschau, ohne Ausführung):</p>
        <div className="flex flex-wrap gap-2">{actions.map((action, index) =>
          <Button key={index} size="sm" variant={action.variant} type="button">{action.label || 'Ohne Beschriftung'}</Button>
        )}</div>
      </div>}
      <div className="mt-3 space-y-3">
        {actions.map((action, actionIndex) => (
          <div key={actionIndex} className="bg-surface-2 border-border rounded-[var(--r-sm)] border p-3">
            <div className="grid grid-cols-[repeat(auto-fit,minmax(160px,1fr))] gap-3">
              <label>
                <FieldLabel>Knopftext</FieldLabel>
                <TextInput
                  value={action.label}
                  maxLength={100}
                  onChange={(event) => updateAction(actionIndex, { ...action, label: event.target.value })}
                />
              </label>
              <label>
                <FieldLabel>Darstellung</FieldLabel>
                <select
                  value={action.variant}
                  className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-accent"
                  onChange={(event) => updateAction(actionIndex, {
                    ...action,
                    variant: event.target.value as FormDecisionActionVariant,
                  })}
                >
                  <option value="primary">Hervorgehoben</option>
                  <option value="secondary">Neutral</option>
                  <option value="danger">Warnend (z. B. Ablehnen)</option>
                </select>
              </label>
              <Button
                size="sm"
                variant="danger"
                icon="delete"
                className="self-end"
                onClick={() => onChange(actions.filter((_, candidate) => candidate !== actionIndex))}
              >
                Entfernen
              </Button>
            </div>

            <p className="text-muted mt-2 text-xs">Beim Klick: Aufgabe abschließen
              {action.set.length > 0 ? ` und ${action.set.length} Ergebniswert${action.set.length > 1 ? 'e' : ''} setzen.` : '. Noch kein Ergebnis konfiguriert.'}
            </p>
            <details className="mt-3 rounded border border-border p-3">
              <summary className="cursor-pointer text-sm font-semibold">Erweitert: Ergebnis und technische ID</summary>
              <p className="text-muted my-2 text-xs">Hier legst du fest, welches vorhandene, beschreibbare Formularfeld
                der Server beim Abschluss setzt. Beispiel: Feld „decision“, Wert „approved“.
                Ein Workflow kann danach anhand dieses Ergebnisses verzweigen. Die technische ID bestehender Knöpfe nicht ohne Prüfung ändern.</p>
              <label>
                <FieldLabel>Technische ID</FieldLabel>
                <TextInput
                  value={action.id}
                  maxLength={64}
                  onChange={(event) => updateAction(actionIndex, { ...action, id: event.target.value })}
                />
              </label>
            <div className="mt-3 space-y-2">
              {action.set.map((assignment, assignmentIndex) => {
                const kind = valueKind(assignment.value);
                return (
                  <div key={assignmentIndex} className="grid gap-2 md:grid-cols-[1fr_150px_1fr_auto]">
                    <label>
                      <FieldLabel>Zielfeld</FieldLabel>
                      <TextInput
                        value={assignment.field}
                        placeholder="decision"
                        onChange={(event) => updateAction(actionIndex, {
                          ...action,
                          set: action.set.map((entry, candidate) => candidate === assignmentIndex
                            ? { ...entry, field: event.target.value }
                            : entry),
                        })}
                      />
                    </label>
                    <label>
                      <FieldLabel>Werttyp</FieldLabel>
                      <select
                        value={kind}
                        className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-accent"
                        onChange={(event) => {
                          const nextKind = event.target.value as ReturnType<typeof valueKind>;
                          updateAction(actionIndex, {
                            ...action,
                            set: action.set.map((entry, candidate) => candidate === assignmentIndex
                              ? { ...entry, value: initialValue(nextKind) }
                              : entry),
                          });
                        }}
                      >
                        <option value="string">Text</option>
                        <option value="number">Zahl</option>
                        <option value="boolean">Ja/Nein</option>
                        <option value="null">Leer</option>
                      </select>
                    </label>
                    <label>
                      <FieldLabel>Fester Wert</FieldLabel>
                      {kind === 'boolean' ? (
                        <select
                          value={String(assignment.value)}
                          className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-accent"
                          onChange={(event) => updateAction(actionIndex, {
                            ...action,
                            set: action.set.map((entry, candidate) => candidate === assignmentIndex
                              ? { ...entry, value: event.target.value === 'true' }
                              : entry),
                          })}
                        >
                          <option value="false">Nein</option>
                          <option value="true">Ja</option>
                        </select>
                      ) : (
                        <TextInput
                          value={assignment.value === null ? '' : String(assignment.value)}
                          type={kind === 'number' ? 'number' : 'text'}
                          disabled={kind === 'null'}
                          onChange={(event) => updateAction(actionIndex, {
                            ...action,
                            set: action.set.map((entry, candidate) => candidate === assignmentIndex
                              ? {
                                  ...entry,
                                  value: kind === 'number'
                                    ? Number(event.target.value)
                                    : event.target.value,
                                }
                              : entry),
                          })}
                        />
                      )}
                    </label>
                    <Button
                      size="sm"
                      variant="ghost"
                      icon="delete"
                      className="self-end"
                      aria-label="Feldbelegung entfernen"
                      onClick={() => updateAction(actionIndex, {
                        ...action,
                        set: action.set.filter((_, candidate) => candidate !== assignmentIndex),
                      })}
                    >
                      <span className="sr-only">Feldbelegung entfernen</span>
                    </Button>
                  </div>
                );
              })}
              <Button
                size="sm"
                variant="ghost"
                icon="add"
                disabled={action.set.length >= 20}
                onClick={() => updateAction(actionIndex, {
                  ...action,
                  set: [...action.set, { field: '', value: '' }],
                })}
              >
                Feldbelegung hinzufügen
              </Button>
            </div>
            </details>
          </div>
        ))}
      </div>
    </section>
  );
}
