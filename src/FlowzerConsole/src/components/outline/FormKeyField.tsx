import { useId, type ReactNode } from 'react';

import { FieldLabel, TextInput } from '@/components/ui/Field';
import { useForms } from '@/lib/api/queries';
import { parseFormKey } from '@/lib/formKey';

/** Ueber welches Attribut das Formular gebunden ist. */
export type FormBinding = 'formKey' | 'formId';

interface FormKeyFieldProps {
  /** Beschriftung: „Formular" am Schritt, „Startformular" am Startereignis. */
  label: string;
  formKey?: string;
  formId?: string;
  /** Erklaerung unter dem Feld, etwa dass das Startformular freiwillig ist. */
  hint?: ReactNode;
  disabled?: boolean;
  onChange: (value: string | undefined, binding: FormBinding) => void;
}

/**
 * Das Bedienelement fuer ein Formular — ein Name aus dem Bestand mit
 * Vorschlagsliste, oder die unveraenderliche Kennung eines Formulars, das im
 * Workflow selbst liegt. Schritt und Startereignis benutzen dasselbe Feld,
 * damit ein Formular ueberall gleich ausgewaehlt wird.
 */
export function FormKeyField({ label, formKey, formId, hint, disabled, onChange }: FormKeyFieldProps) {
  const formsQuery = useForms();
  const fieldId = useId();
  const listId = `${fieldId}-formulare`;
  const reference = parseFormKey(formKey);

  if (reference.kind === 'embedded') return <EmbeddedForm label={label} formId={reference.formId} />;

  // Camunda bindet ein verknuepftes Formular ueber `formId`, ein eingebettetes
  // ueber `formKey`. Wer nur den Text aendert, soll nicht ungewollt die Art der
  // Bindung wechseln.
  const binding: FormBinding = formId === undefined ? 'formKey' : 'formId';

  return (
    <div>
      <FieldLabel htmlFor={fieldId}>{binding === 'formKey' ? label : `${label} (Kennung)`}</FieldLabel>
      <TextInput
        id={fieldId}
        list={listId}
        disabled={disabled}
        value={formKey ?? formId ?? ''}
        placeholder="Name oder Kennung des Formulars"
        onChange={(event) => onChange(event.target.value || undefined, binding)}
      />
      <datalist id={listId}>
        {(formsQuery.data ?? []).map((form) => (
          <option key={form.formId} value={form.name} />
        ))}
      </datalist>
      {hint && <p className="text-muted mt-1.5 text-[12px]">{hint}</p>}
    </div>
  );
}

/**
 * Ein Formular, das im Workflow selbst liegt. Es bleibt hier unangetastet: Der
 * Verweis ist eine Kennung, kein Name — wer sie ueberschreibt, kappt die
 * Verbindung, ohne es zu merken. Bearbeitet wird es im Diagramm.
 */
function EmbeddedForm({ label, formId }: { label: string; formId: string }) {
  return (
    <div>
      <FieldLabel>{label} im Workflow</FieldLabel>
      <div className="border-border bg-surface-2 rounded-[var(--r-sm)] border border-dashed px-3 py-2.5 font-mono text-[12.5px]">
        {formId}
      </div>
      <p className="text-muted mt-1.5 text-[12px]">
        Dieses Formular ist Teil des Workflows und mit ihm versioniert. Es wird im Diagramm bearbeitet.
      </p>
    </div>
  );
}
