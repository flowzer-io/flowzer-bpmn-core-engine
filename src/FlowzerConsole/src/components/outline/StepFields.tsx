import { FormKeyField } from '@/components/outline/FormKeyField';
import {
  DirectorySubjectPicker,
  type DirectorySubjectSelection,
} from '@/components/bpmn/properties/DirectorySubjectPicker';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Segmented } from '@/components/ui/Segmented';
import { useAiConnections } from '@/lib/api/queries';
import type { AiConnectionDto } from '@/lib/api/types';
import { AI_WORKER_TYPE, DEFAULT_AI_TASK } from '@/lib/aiTaskContract';
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

const SERVICE_OPTIONS = [
  { value: 'worker' as const, label: 'Worker' },
  { value: 'ai' as const, label: 'KI' },
];

/**
 * Die Angaben eines Schritts: Formular, Zuweisung und Frist direkt bearbeitbar —
 * das ist der Grund, warum die Gliederung neben dem Diagramm steht.
 */
export function StepFields({ definitionId, document, step, onChange }: StepFieldsProps) {
  const set = (patch: Partial<OutlineStep>) => onChange(updateStep(document, step.id, patch));
  const aiConnections = useAiConnections({ enabled: step.task === 'service' && step.serviceTaskMode === 'ai' });

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
        <>
          <div>
            <FieldLabel>Ausführungsart</FieldLabel>
            <Segmented
              options={SERVICE_OPTIONS}
              value={step.serviceTaskMode === 'ai' ? 'ai' : 'worker'}
              aria-label="Ausführungsart des Dienstschritts"
              onChange={(mode) =>
                set(
                  mode === 'ai'
                    ? { serviceTaskMode: 'ai', workerType: AI_WORKER_TYPE, aiTask: { ...DEFAULT_AI_TASK } }
                    : {
                        serviceTaskMode: 'worker',
                        workerType: step.workerType === AI_WORKER_TYPE ? undefined : step.workerType,
                        aiTask: undefined,
                      },
                )
              }
            />
          </div>

          {step.serviceTaskMode === 'ai' && step.aiTask ? (
            <AiTaskFields
              step={step}
              connections={aiConnections.data ?? []}
              unavailable={aiConnections.isError}
              onChange={(aiTask) => set({ aiTask })}
            />
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
        </>
      )}

      <Mappings step={step} set={set} />
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
function Mappings({ step, set }: { step: OutlineStep; set: (patch: Partial<OutlineStep>) => void }) {
  return (
    <div>
      <FieldLabel>Zuordnungen</FieldLabel>
      <div className="flex flex-col gap-2">
        <MappingRows
          label="Eingang"
          entries={step.inputs}
          onChange={(inputs) => set({ inputs })}
        />
        <MappingRows
          label="Ausgang"
          entries={step.outputs}
          onChange={(outputs) => set({ outputs })}
        />
      </div>
    </div>
  );
}

function MappingRows({
  label,
  entries,
  onChange,
}: {
  label: string;
  entries: OutlineStep['inputs'];
  onChange: (entries: OutlineStep['inputs']) => void;
}) {
  return (
    <div className="border-border rounded-[var(--r-sm)] border p-2">
      <div className="text-muted mb-1 text-[11.5px] font-semibold">{label}</div>
      {entries.map((entry, index) => (
        <div key={index} className="mb-1 flex items-center gap-1">
          <TextInput
            aria-label={`${label} Quelle ${index + 1}`}
            value={entry.source}
            placeholder="=prozesswert"
            onChange={(event) =>
              onChange(entries.map((item, position) => position === index ? { ...item, source: event.target.value } : item))
            }
          />
          <span className="text-faint">→</span>
          <TextInput
            aria-label={`${label} Ziel ${index + 1}`}
            value={entry.target}
            placeholder="feld"
            onChange={(event) =>
              onChange(entries.map((item, position) => position === index ? { ...item, target: event.target.value } : item))
            }
          />
          <button
            type="button"
            aria-label={`${label} ${index + 1} entfernen`}
            className="text-faint hover:text-fail border-none bg-transparent px-1"
            onClick={() => onChange(entries.filter((_, position) => position !== index))}
          >
            ×
          </button>
        </div>
      ))}
      <button
        type="button"
        className="text-accent border-none bg-transparent p-0 text-[12px] font-semibold"
        onClick={() => onChange([...entries, { source: '', target: '' }])}
      >
        + {label}
      </button>
    </div>
  );
}

function AiTaskFields({
  step,
  connections,
  unavailable,
  onChange,
}: {
  step: OutlineStep;
  connections: readonly AiConnectionDto[];
  unavailable: boolean;
  onChange: (value: NonNullable<OutlineStep['aiTask']>) => void;
}) {
  const ai = step.aiTask!;
  const set = (patch: Partial<typeof ai>) => onChange({ ...ai, ...patch });
  return (
    <div className="border-border flex flex-col gap-3 rounded-[var(--r-sm)] border p-3">
      <div>
        <FieldLabel>KI-Verbindung</FieldLabel>
        <select
          value={ai.connectionId}
          onChange={(event) => set({ connectionId: event.target.value })}
          className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2 text-[13px]"
        >
          <option value="">Verbindung auswählen …</option>
          {connections.map((connection) => (
            <option key={connection.id} value={connection.id}>
              {connection.name} · {connection.defaultModel}{connection.ready ? '' : ' · nicht bereit'}
            </option>
          ))}
          {ai.connectionId && !connections.some((connection) => connection.id === ai.connectionId) && (
            <option value={ai.connectionId}>Nicht verfügbar · {ai.connectionId}</option>
          )}
        </select>
        {unavailable && <p className="text-fail mt-1 text-[11.5px]">Verbindungen konnten nicht geladen werden.</p>}
      </div>
      <div>
        <FieldLabel>Modell (optional)</FieldLabel>
        <TextInput value={ai.model} onChange={(event) => set({ model: event.target.value })} />
      </div>
      <div>
        <FieldLabel>Version der Anweisung</FieldLabel>
        <TextInput value={ai.instructionVersion} onChange={(event) => set({ instructionVersion: event.target.value })} />
      </div>
      <div>
        <FieldLabel>Anweisung</FieldLabel>
        <textarea
          value={ai.instruction}
          rows={4}
          onChange={(event) => set({ instruction: event.target.value })}
          className="bg-surface-2 border-border text-text w-full resize-y rounded-[var(--r-sm)] border px-3 py-2 text-[13px]"
        />
      </div>
      <div>
        <FieldLabel>Ergebnisschema (JSON Schema)</FieldLabel>
        <textarea
          value={ai.resultSchema}
          rows={5}
          onChange={(event) => set({ resultSchema: event.target.value })}
          className="bg-surface-2 border-border text-text w-full resize-y rounded-[var(--r-sm)] border px-3 py-2 font-mono text-[12px]"
        />
      </div>
      <div className="grid grid-cols-3 gap-2">
        <LimitField label="Eingabetokens" value={ai.maxInputTokens} onChange={(maxInputTokens) => set({ maxInputTokens })} />
        <LimitField label="Ausgabetokens" value={ai.maxOutputTokens} onChange={(maxOutputTokens) => set({ maxOutputTokens })} />
        <LimitField label="Sekunden" value={ai.timeoutSeconds} onChange={(timeoutSeconds) => set({ timeoutSeconds })} />
      </div>
    </div>
  );
}

function LimitField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <div>
      <FieldLabel>{label}</FieldLabel>
      <TextInput value={value} inputMode="numeric" onChange={(event) => onChange(event.target.value)} />
    </div>
  );
}
