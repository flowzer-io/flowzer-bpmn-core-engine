import { FormKeyField } from '@/components/outline/FormKeyField';
import {
  DirectorySubjectPicker,
  type DirectorySubjectSelection,
} from '@/components/bpmn/properties/DirectorySubjectPicker';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Segmented } from '@/components/ui/Segmented';
import { updateStep } from '@/lib/outline/edit';
import type { OutlineDocument, OutlineStep, TaskKind } from '@/lib/outline/model';

interface StepFieldsProps {
  definitionId: string;
  document: OutlineDocument;
  step: OutlineStep;
  onChange: (next: OutlineDocument) => void;
}

const TASK_OPTIONS = [
  { value: 'user' as const, label: 'Mensch' },
  { value: 'service' as const, label: 'Dienst' },
];

const ASSIGNMENT_OPTIONS = [
  { value: 'text' as const, label: 'Freitext' },
  { value: 'directory' as const, label: 'Bekannte Benutzer/Gruppen' },
];

/**
 * Die Angaben eines Schritts: Formular, Zuweisung und Frist direkt bearbeitbar —
 * das ist der Grund, warum die Gliederung neben dem Diagramm steht.
 */
export function StepFields({ definitionId, document, step, onChange }: StepFieldsProps) {
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
          <FormKeyField
            label="Formular"
            formKey={step.formKey}
            formId={step.formId}
            onChange={(value, binding) => set(binding === 'formKey' ? { formKey: value } : { formId: value })}
          />

          <div>
            <FieldLabel>Art der Zuweisung</FieldLabel>
            <Segmented
              options={ASSIGNMENT_OPTIONS}
              value={step.assignmentMode === 'directory' ? 'directory' : 'text'}
              aria-label="Art der Zuweisung"
              onChange={(mode) =>
                set(
                  mode === 'directory'
                    ? {
                        assignmentMode: 'directory',
                        assignee: undefined,
                        candidateUsers: undefined,
                        candidateGroups: undefined,
                      }
                    : {
                        assignmentMode: 'text',
                        directoryAssigneeId: undefined,
                        directoryCandidateUserIds: undefined,
                        directoryCandidateGroupIds: undefined,
                      },
                )
              }
            />
          </div>

          {step.assignmentMode === 'directory' ? (
            <>
              <DirectorySubjectPicker
                definitionId={definitionId}
                kind="user"
                selected={selection('user', step.directoryAssigneeId)}
                multiple={false}
                label="Direkter Bearbeiter"
                onChange={(selected) => set({ directoryAssigneeId: selected[0]?.subject.id })}
              />
              <DirectorySubjectPicker
                definitionId={definitionId}
                kind="user"
                selected={selections('user', step.directoryCandidateUserIds)}
                multiple
                label="Kandidaten"
                onChange={(selected) =>
                  set({ directoryCandidateUserIds: selected.map((entry) => entry.subject.id) })
                }
              />
              <DirectorySubjectPicker
                definitionId={definitionId}
                kind="group"
                selected={selections('group', step.directoryCandidateGroupIds)}
                multiple
                label="Kandidatengruppen"
                onChange={(selected) =>
                  set({ directoryCandidateGroupIds: selected.map((entry) => entry.subject.id) })
                }
              />
            </>
          ) : (
            <>
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
                  placeholder="Benutzerkennung, Ausdruck oder leer"
                  onChange={(event) => set({ assignee: event.target.value || undefined })}
                />
              </div>
            </>
          )}

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

function selection(kind: 'user' | 'group', id: string | undefined): DirectorySubjectSelection[] {
  return id ? selections(kind, [id]) : [];
}

function selections(
  kind: 'user' | 'group',
  ids: readonly string[] | undefined,
): DirectorySubjectSelection[] {
  return (ids ?? []).map((id) => ({
    subject: { kind, id },
    displayName: id,
    detail: 'Stabile Verzeichnis-ID',
    available: false,
  }));
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
