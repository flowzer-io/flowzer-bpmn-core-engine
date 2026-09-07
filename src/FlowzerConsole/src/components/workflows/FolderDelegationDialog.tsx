import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { Modal } from '@/components/ui/Modal';
import type {
  FolderAssignmentDto,
  FolderRole,
  FolderSubjectKind,
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
 * Wer darf in diesem Ordner bearbeiten, wer verantwortet ihn fachlich.
 *
 * Geerbte Zuweisungen stehen mit oben, aber unveränderlich: Wer sie hier ändern könnte,
 * änderte in Wahrheit den übergeordneten Ordner — für alle anderen darunter gleich mit.
 * Der Verweis sagt deshalb, woher die Zeile kommt.
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

  useEffect(() => {
    if (!open) return;
    setAssignments(folder?.assignments ?? []);
    setSubject('');
    setSubjectKind('user');
  }, [open, folder]);

  function add() {
    const trimmed = subject.trim();
    if (!trimmed) return;

    const bereitsDa = assignments.some(
      (assignment) =>
        assignment.subjectKind === subjectKind &&
        assignment.subject.toLowerCase() === trimmed.toLowerCase(),
    );
    if (bereitsDa) {
      setSubject('');
      return;
    }

    setAssignments([...assignments, { subjectKind, subject: trimmed, role: 'editor' }]);
    setSubject('');
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
          <Button size="sm" variant="primary" icon="check" loading={busy} onClick={() => onSubmit(assignments)}>
            Übernehmen
          </Button>
        </>
      }
    >
      <div className="flex flex-col pb-3">
        {(folder?.inheritedAssignments ?? []).map((assignment) => (
          <AssignmentRow
            key={`erbe-${assignment.subjectKind}-${assignment.subject}`}
            assignment={assignment}
            inheritedFrom={assignment.inheritedFromName}
          />
        ))}

        {assignments.map((assignment, index) => (
          <AssignmentRow
            key={`${assignment.subjectKind}-${assignment.subject}`}
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

        <form
          className="flex items-center gap-2 pt-3.5"
          onSubmit={(event) => {
            event.preventDefault();
            add();
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

        <p className="text-faint mt-2.5 text-[12px] leading-normal">
          Genannt werden die Kennungen des Identity Providers — dieselben, mit denen ein
          BPMN-Modell <code className="font-mono">assignee</code> und{' '}
          <code className="font-mono">candidateGroups</code> besetzt.
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
  const geerbt = Boolean(inheritedFrom);

  return (
    <div className={cn('border-border flex items-center gap-2.5 border-b py-2.5', geerbt && 'bg-inset -mx-2 px-2')}>
      <span
        className={cn(
          'grid h-6 w-6 flex-none place-items-center rounded-full text-[10px] font-bold',
          assignment.subjectKind === 'group' ? 'bg-surface-2 text-muted' : 'bg-accent text-accent-ink',
        )}
      >
        {assignment.subjectKind === 'group' ? (
          <Icon name="group" size={14} />
        ) : (
          initials(assignment.displayName ?? assignment.subject)
        )}
      </span>

      <span className="min-w-0 flex-1">
        <span className="block truncate text-[13px] font-semibold">
          {assignment.displayName ?? assignment.subject}
        </span>
        {assignment.displayName && (
          <span className="text-faint block truncate text-[11px]">{assignment.subject}</span>
        )}
      </span>

      {geerbt && (
        <span className="text-faint flex flex-none items-center gap-1 text-[11px] whitespace-nowrap">
          <Icon name="subdirectory_arrow_right" size={14} />
          geerbt von {inheritedFrom}
        </span>
      )}

      <div
        className={cn(
          'flex flex-none overflow-hidden rounded-lg border',
          geerbt ? 'border-border border-dashed' : 'border-border-strong',
        )}
      >
        {(['editor', 'steward'] as const).map((role) => (
          <button
            key={role}
            type="button"
            disabled={geerbt}
            className={cn(
              'border-none px-2.5 py-1.5 text-[11.5px] font-semibold',
              geerbt
                ? 'cursor-default bg-transparent ' + (assignment.role === role ? 'bg-surface-2 text-muted' : 'text-faint')
                : 'cursor-pointer ' +
                  (assignment.role === role ? 'bg-accent text-accent-ink' : 'bg-surface text-muted'),
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
          aria-label={`${assignment.subject} entfernen`}
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

function initials(value: string): string {
  return value
    .split(/[\s.@_-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
}
