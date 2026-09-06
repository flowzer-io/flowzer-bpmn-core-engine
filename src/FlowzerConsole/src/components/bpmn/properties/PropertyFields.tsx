import { useEffect, useId, useState, type ReactNode } from 'react';

import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';

import type { IoMapping } from '../bpmnEditor';

/** Ein Abschnitt des Eigenschaften-Panels. */
export function Section({
  icon,
  title,
  hint,
  children,
}: {
  icon: string;
  title: string;
  hint?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="border-border border-b px-4 py-4 last:border-b-0">
      <div className="mb-3 flex items-center gap-2">
        <Icon name={icon} size={16} className="text-accent" />
        <h3 className="font-display text-text m-0 text-[13px] font-semibold">{title}</h3>
      </div>
      {hint && <p className="text-muted mt-0 mb-3 text-[12px] leading-normal">{hint}</p>}
      <div className="flex flex-col gap-3.5">{children}</div>
    </section>
  );
}

interface TextRowProps {
  label: string;
  value: string;
  onCommit: (value: string) => void;
  placeholder?: string;
  hint?: ReactNode;
  disabled?: boolean;
  monospace?: boolean;
}

/**
 * Ein Textfeld, das erst beim Verlassen schreibt.
 *
 * bpmn-js führt jede Änderung als eigenen Befehl aus. Würde bei jedem Tastendruck
 * geschrieben, bräuchte „Rückgängig" so viele Schritte, wie der Wert Zeichen hat.
 */
export function TextRow({ label, value, onCommit, placeholder, hint, disabled, monospace }: TextRowProps) {
  const fieldId = useId();
  const [draft, setDraft] = useState(value);

  useEffect(() => setDraft(value), [value]);

  return (
    <div>
      <FieldLabel htmlFor={fieldId}>{label}</FieldLabel>
      <TextInput
        id={fieldId}
        value={draft}
        disabled={disabled}
        placeholder={placeholder}
        onChange={(event) => setDraft(event.target.value)}
        onBlur={() => {
          if (draft !== value) onCommit(draft);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur();
          if (event.key === 'Escape') {
            setDraft(value);
            event.currentTarget.blur();
          }
        }}
        className={cn('py-2', monospace && 'font-mono text-[12.5px]')}
      />
      {hint && <p className="text-faint mt-1.5 text-[11.5px] leading-normal">{hint}</p>}
    </div>
  );
}

interface SelectRowProps {
  label: string;
  value: string;
  options: { value: string; label: string }[];
  onChange: (value: string) => void;
  disabled?: boolean;
  hint?: ReactNode;
}

export function SelectRow({ label, value, options, onChange, disabled, hint }: SelectRowProps) {
  const fieldId = useId();

  return (
    <div>
      <FieldLabel htmlFor={fieldId}>{label}</FieldLabel>
      <select
        id={fieldId}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        className={cn(
          'bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2',
          'text-[13.5px] outline-none focus:border-accent disabled:opacity-55',
        )}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      {hint && <p className="text-faint mt-1.5 text-[11.5px] leading-normal">{hint}</p>}
    </div>
  );
}

/** Kurzer Hinweis, der auf eine Lücke im Modell zeigt. */
export function Notice({ tone = 'muted', children }: { tone?: 'muted' | 'warn'; children: ReactNode }) {
  return (
    <p
      className={cn(
        'm-0 rounded-[var(--r-sm)] px-2.5 py-2 text-[12px] leading-normal',
        tone === 'warn' ? 'text-fail bg-fail/10' : 'text-muted bg-surface-2',
      )}
    >
      {children}
    </p>
  );
}

interface IoMappingEditorProps {
  label: string;
  /** Beschriftung der linken Spalte — beim Eingang der Prozesswert, beim Ausgang das Ergebnis. */
  sourceLabel: string;
  targetLabel: string;
  value: IoMapping[];
  onChange: (mappings: IoMapping[]) => void;
  disabled?: boolean;
}

/**
 * Zuordnungen zwischen Prozessdaten und Aufgabendaten (`zeebe:ioMapping`).
 *
 * Die Liste wird als Ganzes geschrieben: Ein einzelnes Paar hat im BPMN keine Identität,
 * über die sich eine Einzeländerung zuordnen ließe.
 */
export function IoMappingEditor({
  label,
  sourceLabel,
  targetLabel,
  value,
  onChange,
  disabled,
}: IoMappingEditorProps) {
  const signature = JSON.stringify(value);
  const [draft, setDraft] = useState(value);
  const [lastSignature, setLastSignature] = useState(signature);

  // Uebernimmt einen von aussen geaenderten Stand — etwa nach „Rueckgaengig" — ohne Umweg
  // ueber einen Effekt, der erst nach einem Zwischenbild mit veralteten Werten greift.
  if (signature !== lastSignature) {
    setLastSignature(signature);
    setDraft(value);
  }

  function update(index: number, patch: Partial<IoMapping>) {
    setDraft((current) => current.map((entry, position) => (position === index ? { ...entry, ...patch } : entry)));
  }

  function commit(next: IoMapping[]) {
    setDraft(next);
    onChange(next);
  }

  /** Schreibt nur, wenn sich wirklich etwas geaendert hat — sonst waere jedes Verlassen
      eines Feldes ein eigener Schritt im Rueckgaengig-Verlauf. */
  function commitIfChanged() {
    if (JSON.stringify(draft) !== signature) onChange(draft);
  }

  return (
    <div>
      <FieldLabel>{label}</FieldLabel>

      {draft.length === 0 && <p className="text-faint m-0 text-[12px]">Keine Zuordnung.</p>}

      <div className="flex flex-col gap-1.5">
        {draft.map((mapping, index) => (
          <div key={index} className="flex items-center gap-1.5">
            <TextInput
              value={mapping.source}
              disabled={disabled}
              placeholder={sourceLabel}
              aria-label={`${sourceLabel} ${index + 1}`}
              onChange={(event) => update(index, { source: event.target.value })}
              onBlur={commitIfChanged}
              className="py-1.5 font-mono text-[12px]"
            />
            <Icon name="arrow_forward" size={14} className="text-faint flex-none" />
            <TextInput
              value={mapping.target}
              disabled={disabled}
              placeholder={targetLabel}
              aria-label={`${targetLabel} ${index + 1}`}
              onChange={(event) => update(index, { target: event.target.value })}
              onBlur={commitIfChanged}
              className="py-1.5 font-mono text-[12px]"
            />
            <button
              type="button"
              disabled={disabled}
              title="Zuordnung entfernen"
              onClick={() => commit(draft.filter((_, position) => position !== index))}
              className="text-faint hover:text-fail grid h-7 w-7 flex-none cursor-pointer place-items-center rounded-md border-none bg-transparent disabled:cursor-not-allowed"
            >
              <span className="sr-only">Zuordnung {index + 1} entfernen</span>
              <Icon name="close" size={15} />
            </button>
          </div>
        ))}
      </div>

      <Button
        size="sm"
        variant="ghost"
        icon="add"
        disabled={disabled}
        className="mt-1.5 px-1"
        onClick={() => setDraft([...draft, { source: '', target: '' }])}
      >
        Zuordnung
      </Button>
    </div>
  );
}
