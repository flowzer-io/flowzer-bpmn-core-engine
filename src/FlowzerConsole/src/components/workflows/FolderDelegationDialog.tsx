import { useEffect, useState } from 'react';

import { DirectorySubjectPicker, type DirectorySubjectSelection } from '@/components/bpmn/properties/DirectorySubjectPicker';
import { Button } from '@/components/ui/Button';
import { TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { Modal } from '@/components/ui/Modal';
import { Segmented } from '@/components/ui/Segmented';
import type {
  FolderAssignmentDto,
  FolderReferenceMode,
  FolderRole,
  FolderSubjectKind,
  SubjectRefDto,
  WorkflowFolderDto,
} from '@/lib/api/types';
import { cn } from '@/lib/cn';

const ROLE_LABEL: Record<FolderRole, string> = {
  editor: 'Bearbeiten',
  steward: 'Fachverantwortung',
};

interface FolderDelegationDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  folder: WorkflowFolderDto | null;
  busy?: boolean;
  onSubmit: (assignments: FolderAssignmentDto[]) => void;
}

/**
 * Pflegt die Zuständigkeiten eines Ordners. Freitext bleibt der unveränderte Legacy-Pfad;
 * Directory-Zuweisungen werden ausschließlich über den ordnergebundenen Picker ausgewählt.
 */
export function FolderDelegationDialog({
  open,
  onOpenChange,
  folder,
  busy = false,
  onSubmit,
}: FolderDelegationDialogProps) {
  const [assignments, setAssignments] = useState<FolderAssignmentDto[]>([]);
  const [subject, setSubject] = useState('');
  const [subjectKind, setSubjectKind] = useState<FolderSubjectKind>('user');
  const [referenceMode, setReferenceMode] = useState<FolderReferenceMode>('text');

  useEffect(() => {
    if (!open) return;
    // Fehlende referenceMode-Werte stammen aus alten Antworten und sind weiterhin Freitext.
    setAssignments((folder?.assignments ?? []).map(normalizeAssignment));
    setSubject('');
    setSubjectKind('user');
    setReferenceMode('text');
  }, [open, folder]);

  function addTextAssignment() {
    const trimmed = subject.trim();
    if (!trimmed) return;

    const alreadyThere = assignments.some(
      (assignment) =>
        assignment.referenceMode !== 'directory' &&
        assignment.subjectKind === subjectKind &&
        assignment.subject.toLowerCase() === trimmed.toLowerCase(),
    );
    if (alreadyThere) {
      setSubject('');
      return;
    }

    setAssignments([
      ...assignments,
      { referenceMode: 'text', subjectKind, subject: trimmed, role: 'editor' },
    ]);
    setSubject('');
  }

  const directorySelections = assignments
    .filter((assignment) => assignment.referenceMode === 'directory')
    .map(toDirectorySelection);

  function changeDirectorySelections(next: DirectorySubjectSelection[]) {
    const existingDirectory = new Map(
      assignments
        .filter((assignment) => assignment.referenceMode === 'directory')
        .map((assignment) => [subjectKey(assignment.subjectRef ?? fallbackRef(assignment)), assignment]),
    );
    const nextDirectory = next.map((selection): FolderAssignmentDto => {
      const existing = existingDirectory.get(subjectKey(selection.subject));
      return existing
        ? {
            ...existing,
            subject: selection.subject.id,
            displayName: selection.displayName,
            subjectRef: selection.subject,
          }
        : {
            referenceMode: 'directory',
            subjectKind: selection.subject.kind,
            // Der Backend erhält die ID zusätzlich als additive Kompatibilitätsprojektion.
            subject: selection.subject.id,
            subjectRef: selection.subject,
            role: 'editor',
            displayName: selection.displayName,
          };
    });
    setAssignments([
      ...assignments.filter((assignment) => assignment.referenceMode !== 'directory'),
      ...nextDirectory,
    ]);
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={folder ? `Zuständigkeiten für „${folder.name}“` : 'Zuständigkeiten'}
      icon="manage_accounts"
      className="w-[min(600px,calc(100vw-32px))]"
      description="Gilt für diesen Ordner und alle Unterordner. Lesen und Starten bleibt für alle Zugelassenen offen."
      footer={
        <>
          <Button size="sm" onClick={() => onOpenChange(false)} disabled={busy}>
            Abbrechen
          </Button>
          <Button
            size="sm"
            variant="primary"
            icon="check"
            loading={busy}
            onClick={() => onSubmit(assignments.map(normalizeAssignment))}
          >
            Übernehmen
          </Button>
        </>
      }
    >
      <div className="flex flex-col pb-3">
        {(folder?.inheritedAssignments ?? []).map((assignment) => (
          <AssignmentRow
            key={`inherited-${assignmentKey(assignment)}-${assignment.inheritedFromId}`}
            assignment={assignment}
            inheritedFrom={assignment.inheritedFromName}
          />
        ))}

        {assignments.map((assignment, index) => (
          <AssignmentRow
            key={`${assignmentKey(assignment)}-${index}`}
            assignment={assignment}
            onRoleChange={(role) =>
              setAssignments(assignments.map((entry, i) => (i === index ? { ...entry, role } : entry)))
            }
            onRemove={() => setAssignments(assignments.filter((_, i) => i !== index))}
          />
        ))}

        {assignments.length === 0 && (folder?.inheritedAssignments ?? []).length === 0 && (
          <p className="text-faint border-border border-b py-3 text-[12.5px] leading-normal">
            Noch niemand eingetragen. Ohne Eintrag bleibt der Ordner der Rolle fürs Modellieren
            vorbehalten.
          </p>
        )}

        <Segmented
          options={[
            { value: 'text' as const, label: 'Freitext (Legacy)' },
            { value: 'directory' as const, label: 'Bekannte Directory-Identität' },
          ]}
          value={referenceMode}
          onChange={setReferenceMode}
          aria-label="Art der Ordnerzuweisung"
          className="mt-3.5"
        />

        {referenceMode === 'text' ? (
          <form
            className="flex items-center gap-2 pt-3.5"
            onSubmit={(event) => {
              event.preventDefault();
              addTextAssignment();
            }}
          >
            <div className="border-border-strong flex flex-none overflow-hidden rounded-lg border">
              {(['user', 'group'] as const).map((kind) => (
                <button
                  key={kind}
                  type="button"
                  className={cn(
                    'cursor-pointer border-none px-2.5 py-2 text-[12px] font-semibold',
                    subjectKind === kind ? 'bg-accent text-accent-ink' : 'bg-surface text-muted',
                  )}
                  onClick={() => setSubjectKind(kind)}
                >
                  {kind === 'user' ? 'Person' : 'Gruppe'}
                </button>
              ))}
            </div>
            <TextInput
              value={subject}
              placeholder={
                subjectKind === 'user'
                  ? 'Kennung oder E-Mail, z. B. anna.weber@maass.it'
                  : 'Gruppenname oder -pfad, z. B. /abteilungen/einkauf'
              }
              onChange={(event) => setSubject(event.target.value)}
            />
            <Button type="submit" size="sm" icon="person_add" className="flex-none" disabled={!subject.trim()}>
              Hinzufügen
            </Button>
          </form>
        ) : (
          <div className="pt-3.5">
            <DirectorySubjectPicker
              definitionId=""
              folderId={folder?.id ?? ''}
              kind="all"
              selected={directorySelections}
              multiple
              disabled={!folder}
              label="Person oder Gruppe"
              onChange={changeDirectorySelections}
            />
          </div>
        )}

        <p className="text-faint mt-2.5 text-[12px] leading-normal">
          Freitext wird nicht mit dem Verzeichnis verknüpft. Directory-Zuweisungen verwenden stabile
          Identitäten; historische oder nicht mehr aktive Referenzen bleiben sichtbar und können entfernt werden.
        </p>
      </div>
    </Modal>
  );
}

interface AssignmentRowProps {
  assignment: FolderAssignmentDto;
  inheritedFrom?: string;
  onRoleChange?: (role: FolderRole) => void;
  onRemove?: () => void;
}

function AssignmentRow({ assignment, inheritedFrom, onRoleChange, onRemove }: AssignmentRowProps) {
  const inherited = Boolean(inheritedFrom);
  const isDirectory = assignment.referenceMode === 'directory';
  const subjectLabel = isDirectory
    ? assignment.displayName ?? assignment.subjectRef?.id ?? assignment.subject
    : assignment.displayName ?? assignment.subject;

  return (
    <div className={cn('border-border flex items-center gap-2.5 border-b py-2.5', inherited && 'bg-inset -mx-2 px-2')}>
      <span
        className={cn(
          'grid h-6 w-6 flex-none place-items-center rounded-full text-[10px] font-bold',
          assignment.subjectKind === 'group' ? 'bg-surface-2 text-muted' : 'bg-accent text-accent-ink',
        )}
      >
        {assignment.subjectKind === 'group' ? <Icon name="group" size={14} /> : initials(subjectLabel)}
      </span>

      <span className="min-w-0 flex-1">
        <span className="block truncate text-[13px] font-semibold">{subjectLabel}</span>
        {(assignment.displayName || isDirectory) && (
          <span className="text-faint block truncate text-[11px]">
            {isDirectory ? assignment.subjectRef?.id ?? assignment.subject : assignment.subject}
          </span>
        )}
      </span>

      {inherited && (
        <span className="text-faint flex flex-none items-center gap-1 text-[11px] whitespace-nowrap">
          <Icon name="subdirectory_arrow_right" size={14} />
          geerbt von {inheritedFrom}
        </span>
      )}

      <div className={cn('flex flex-none overflow-hidden rounded-lg border', inherited ? 'border-border border-dashed' : 'border-border-strong')}>
        {(['editor', 'steward'] as const).map((role) => (
          <button
            key={role}
            type="button"
            disabled={inherited}
            className={cn(
              'border-none px-2.5 py-1.5 text-[11.5px] font-semibold',
              inherited
                ? `cursor-default bg-transparent ${assignment.role === role ? 'bg-surface-2 text-muted' : 'text-faint'}`
                : `cursor-pointer ${assignment.role === role ? 'bg-accent text-accent-ink' : 'bg-surface text-muted'}`,
            )}
            onClick={() => onRoleChange?.(role)}
          >
            {ROLE_LABEL[role]}
          </button>
        ))}
      </div>

      {onRemove ? (
        <button
          type="button"
          aria-label={`${subjectLabel} entfernen`}
          className="text-faint hover:text-fail grid h-7 w-7 flex-none cursor-pointer place-items-center rounded-md border-none bg-transparent"
          onClick={onRemove}
        >
          <Icon name="close" size={16} />
        </button>
      ) : (
        <span className="w-7 flex-none" />
      )}
    </div>
  );
}

function normalizeAssignment(assignment: FolderAssignmentDto): FolderAssignmentDto {
  return {
    ...assignment,
    referenceMode: assignment.referenceMode === 'directory' ? 'directory' : 'text',
  };
}

function fallbackRef(assignment: FolderAssignmentDto): SubjectRefDto {
  return { kind: assignment.subjectKind, id: assignment.subjectRef?.id ?? assignment.subject };
}

function toDirectorySelection(assignment: FolderAssignmentDto): DirectorySubjectSelection {
  const subject = assignment.subjectRef ?? fallbackRef(assignment);
  return {
    subject,
    displayName: assignment.displayName ?? subject.id,
    detail: assignment.displayName ? subject.id : 'Historische Directory-Referenz',
    available: false,
  };
}

function subjectKey(subject: SubjectRefDto): string {
  return `${subject.kind}:${subject.id}`;
}

function assignmentKey(assignment: FolderAssignmentDto): string {
  return assignment.referenceMode === 'directory'
    ? subjectKey(assignment.subjectRef ?? fallbackRef(assignment))
    : `${assignment.subjectKind}:${assignment.subject}`;
}

function initials(value: string): string {
  return value
    .split(/[\s.@_-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
}
