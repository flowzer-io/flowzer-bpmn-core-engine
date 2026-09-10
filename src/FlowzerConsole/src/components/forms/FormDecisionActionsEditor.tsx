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
  function updateAction(index: number, next: FormDecisionActionDraft) {
    onChange(actions.map((action, candidate) => candidate === index ? next : action));
  }

  return (
    <section className="border-border bg-surface mt-4 rounded-[var(--r)] border p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">Entscheidungsaktionen</h3>
          <p className="text-muted mt-1 max-w-[720px] text-xs leading-normal">
            Ersetzt „Aufgabe abschließen“ durch fachliche Aktionen. Die festen Werte werden
            beim Abschluss aus der veröffentlichten Formularversion gesetzt, nicht aus dem Browser.
            Jedes Zielfeld muss als beschreibbares Feld im Formular vorhanden sein.
          </p>
        </div>
        <Button
          size="sm"
          variant="secondary"
          icon="add"
          disabled={actions.length >= 20}
          onClick={() => onChange([...actions, {
            id: `action_${actions.length + 1}`,
            label: `Aktion ${actions.length + 1}`,
            variant: 'secondary',
            set: [{ field: 'decision', value: `value_${actions.length + 1}` }],
          }])}
        >
          Aktion hinzufügen
        </Button>
      </div>

      {actions.length === 0 && (
        <div className="border-border text-muted mt-3 rounded-[var(--r-sm)] border border-dashed px-3 py-4 text-center text-xs">
          Ohne Aktionsdefinition bleibt der generische Abschlussknopf erhalten.
        </div>
      )}

      <div className="mt-3 space-y-3">
        {actions.map((action, actionIndex) => (
          <div key={actionIndex} className="bg-surface-2 border-border rounded-[var(--r-sm)] border p-3">
            <div className="grid gap-3 md:grid-cols-[1fr_1.4fr_180px_auto]">
              <label>
                <FieldLabel>Stabile ID</FieldLabel>
                <TextInput
                  value={action.id}
                  maxLength={64}
                  onChange={(event) => updateAction(actionIndex, { ...action, id: event.target.value })}
                />
              </label>
              <label>
                <FieldLabel>Beschriftung</FieldLabel>
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
                  <option value="primary">Primär</option>
                  <option value="secondary">Sekundär</option>
                  <option value="danger">Kritisch</option>
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
          </div>
        ))}
      </div>
    </section>
  );
}
