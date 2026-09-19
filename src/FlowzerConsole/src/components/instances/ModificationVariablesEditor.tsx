import { useId } from 'react';

import { FieldLabel } from '@/components/ui/Field';

interface ModificationVariablesEditorProps {
  /** Der Text des Felds — vorbelegt mit den aktuellen Prozessvariablen. */
  draft: string;
  onDraftChange: (draft: string) => void;
  /** Der Grund, aus dem der Text gerade nicht gelesen werden kann. */
  error?: string;
  /** Die Namen der aktuellen Prozessvariablen; nur sie lassen sich entfernen. */
  names: string[];
  /** Die zum Entfernen angehakten Namen. */
  removals: string[];
  onToggleRemoval: (name: string, remove: boolean) => void;
  disabled?: boolean;
}

/**
 * Variablen der Prozessebene korrigieren — schreiben im Feld, entfernen in der Liste.
 *
 * Beides ist bewusst getrennt: Ein Name, der im Feld fehlt, ist keine Löschung, sondern
 * schlicht nicht genannt. Wer eine Variable loswerden will, hakt sie an; sonst verschwände
 * beim Kürzen des Textes still etwas, das niemand löschen wollte.
 */
export function ModificationVariablesEditor({
  draft,
  onDraftChange,
  error,
  names,
  removals,
  onToggleRemoval,
  disabled,
}: ModificationVariablesEditorProps) {
  const fieldId = useId();

  return (
    <section className="border-border rounded-[var(--r)] border p-3">
      <h3 className="m-0 mb-3 text-[13.5px] font-semibold">Variablen</h3>

      <FieldLabel htmlFor={fieldId}>Variablen setzen (JSON)</FieldLabel>
      <textarea
        id={fieldId}
        rows={8}
        spellCheck={false}
        value={draft}
        disabled={disabled}
        onChange={(event) => onDraftChange(event.target.value)}
        aria-invalid={error ? true : undefined}
        className={[
          'bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5',
          'font-mono text-[12.5px] outline-none focus:border-accent',
          error ? 'border-fail' : '',
        ].join(' ')}
        placeholder='{ "betrag": 120 }'
      />
      <div className="text-faint mt-1.5 text-[12.5px]">
        Vorbelegt mit dem aktuellen Stand. Gesendet wird nur, was du änderst oder ergänzt;
        alles andere bleibt unberührt.
      </div>
      {error && <div className="text-fail mt-1.5 text-[12.5px]">{error}</div>}

      {names.length > 0 && (
        <fieldset className="m-0 mt-4 border-none p-0">
          <legend className="text-muted mb-1.5 block p-0 font-mono text-[10.5px] font-medium tracking-[0.06em] uppercase">
            Variablen entfernen
          </legend>
          <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
            {names.map((name) => (
              <li key={name}>
                <label className="flex cursor-pointer items-center gap-2.5 text-[13px]">
                  <input
                    type="checkbox"
                    checked={removals.includes(name)}
                    disabled={disabled}
                    onChange={(event) => onToggleRemoval(name, event.target.checked)}
                    className="accent-accent h-4 w-4 cursor-pointer"
                  />
                  <span className="font-mono text-[12.5px]">{name}</span>
                </label>
              </li>
            ))}
          </ul>
        </fieldset>
      )}
    </section>
  );
}
