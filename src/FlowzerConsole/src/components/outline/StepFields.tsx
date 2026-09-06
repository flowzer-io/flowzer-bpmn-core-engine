import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Segmented } from '@/components/ui/Segmented';
import { useForms } from '@/lib/api/queries';
import { updateStep } from '@/lib/outline/edit';
import type { OutlineDocument, OutlineStep, TaskKind } from '@/lib/outline/model';

interface StepFieldsProps {
  document: OutlineDocument;
  step: OutlineStep;
  onChange: (next: OutlineDocument) => void;
}

const TASK_OPTIONS = [
  { value: 'user' as const, label: 'Mensch' },
  { value: 'service' as const, label: 'Dienst' },
];

/**
 * Die Angaben eines Schritts: Formular, Zuweisung und Frist direkt bearbeitbar —
 * das ist der Grund, warum die Gliederung neben dem Diagramm steht.
 */
export function StepFields({ document, step, onChange }: StepFieldsProps) {
  const formsQuery = useForms();
  const set = (patch: Partial<OutlineStep>) => onChange(updateStep(document, step.id, patch));

  return (
    <div className="flex flex-col gap-4">
      <div>
        <FieldLabel>Wer erledigt das?</FieldLabel>
        <Segmented
          options={TASK_OPTIONS}
          value={step.task}
          aria-label="Art des Schritts"
          onChange={(task: TaskKind) => set({ task })}
        />
      </div>

      {step.task === 'user' ? (
        <>
          <div>
            <FieldLabel>Formular</FieldLabel>
            <TextInput
              list="gliederung-formulare"
              value={step.formKey ?? step.formId ?? ''}
              placeholder="Name oder Kennung des Formulars"
              onChange={(event) => set({ formKey: event.target.value || undefined, formId: undefined })}
            />
            <datalist id="gliederung-formulare">
              {(formsQuery.data ?? []).map((form) => (
                <option key={form.formId} value={form.name} />
              ))}
            </datalist>
          </div>

          <div>
            <FieldLabel>Zuständige Gruppen</FieldLabel>
            <TextInput
              value={step.candidateGroups ?? ''}
              placeholder="z. B. Vorgesetzte"
              onChange={(event) => set({ candidateGroups: event.target.value || undefined })}
            />
          </div>

          <div>
            <FieldLabel>Feste Person</FieldLabel>
            <TextInput
              value={step.assignee ?? ''}
              placeholder="Benutzerkennung, sonst leer lassen"
              onChange={(event) => set({ assignee: event.target.value || undefined })}
            />
          </div>

          <div>
            <FieldLabel>Frist (ISO-8601-Dauer)</FieldLabel>
            <TextInput
              value={step.dueDate ?? ''}
              placeholder="z. B. PT48H oder P3D"
              onChange={(event) => set({ dueDate: event.target.value || undefined })}
            />
          </div>
        </>
      ) : (
        <div>
          <FieldLabel>Typ des Dienstes</FieldLabel>
          <TextInput
            value={step.workerType ?? ''}
            placeholder="z. B. urlaub-vertretung-pruefen"
            onChange={(event) => set({ workerType: event.target.value || undefined })}
          />
          <p className="text-muted mt-1.5 text-[12px]">
            Ein Worker meldet sich mit diesem Typ und übernimmt die Aufgabe.
          </p>
        </div>
      )}

      <Mappings step={step} />
    </div>
  );
}

/**
 * Ein- und Ausgangszuordnungen bleiben im Prototyp lesend: Sie gehoeren zum
 * Modell und duerfen nicht verloren gehen, ihre Bearbeitung ist aber ein
 * eigenes Thema.
 */
function Mappings({ step }: { step: OutlineStep }) {
  if (step.inputs.length === 0 && step.outputs.length === 0) return null;

  return (
    <div>
      <FieldLabel>Zuordnungen</FieldLabel>
      <div className="border-border bg-surface-2 rounded-[var(--r-sm)] border p-2.5 font-mono text-[11.5px]">
        {step.inputs.map((entry) => (
          <div key={`in-${entry.target}`} className="text-muted">
            <span className="text-faint">ein </span>
            {entry.source} → {entry.target}
          </div>
        ))}
        {step.outputs.map((entry) => (
          <div key={`out-${entry.target}`} className="text-muted">
            <span className="text-faint">aus </span>
            {entry.source} → {entry.target}
          </div>
        ))}
      </div>
      <p className="text-faint mt-1.5 text-[11.5px]">Im Prototyp nur lesbar; sie bleiben beim Speichern erhalten.</p>
    </div>
  );
}
