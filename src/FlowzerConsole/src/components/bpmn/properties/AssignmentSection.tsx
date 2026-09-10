import { useEffect, useState } from 'react';

import { Segmented } from '@/components/ui/Segmented';

import type { BpmnEditor, DirectoryAssignment, ElementProperties } from '../bpmnEditor';
import {
  DirectorySubjectPicker,
  type DirectorySubjectSelection,
} from './DirectorySubjectPicker';
import { Notice, Section, TextRow } from './PropertyFields';

const ASSIGNMENT_MODE_OPTIONS = [
  { value: 'text' as const, label: 'Freitext' },
  { value: 'directory' as const, label: 'Bekannte Benutzer/Gruppen' },
];

interface AssignmentSectionProps {
  definitionId: string;
  properties: ElementProperties;
  editor: BpmnEditor | null;
  readOnly: boolean;
}

/**
 * Trennt Legacy-Freitext und stabile Verzeichnisreferenzen sichtbar voneinander. Ein bloßer
 * Moduswechsel erzeugt noch keinen leeren Directory-Vertrag, den der Server ablehnen müsste.
 */
export function AssignmentSection({
  definitionId,
  properties,
  editor,
  readOnly,
}: AssignmentSectionProps) {
  const modelMode = properties.assignmentMode === 'directory' ? 'directory' : 'text';
  const [mode, setMode] = useState<'text' | 'directory'>(modelMode);
  const [assignee, setAssignee] = useState<DirectorySubjectSelection[]>(() =>
    selection('user', properties.directoryAssignment.assigneeId),
  );
  const [candidateUsers, setCandidateUsers] = useState<DirectorySubjectSelection[]>(() =>
    selections('user', properties.directoryAssignment.candidateUserIds),
  );
  const [candidateGroups, setCandidateGroups] = useState<DirectorySubjectSelection[]>(() =>
    selections('group', properties.directoryAssignment.candidateGroupIds),
  );
  const directorySignature = JSON.stringify(properties.directoryAssignment);

  useEffect(() => {
    setMode(modelMode);
  }, [properties.id, modelMode]);

  // Anzeigeinformationen aus einer gerade erfolgten Suche bleiben erhalten. Nur neue oder
  // extern geänderte IDs werden als historische Fallback-Chips ergänzt.
  useEffect(() => {
    const directoryAssignment = JSON.parse(directorySignature) as DirectoryAssignment;
    setAssignee((current) =>
      mergeSelections(current, selection('user', directoryAssignment.assigneeId)),
    );
    setCandidateUsers((current) =>
      mergeSelections(current, selections('user', directoryAssignment.candidateUserIds)),
    );
    setCandidateGroups((current) =>
      mergeSelections(current, selections('group', directoryAssignment.candidateGroupIds)),
    );
  }, [properties.id, directorySignature]);

  function changeMode(next: 'text' | 'directory') {
    setMode(next);
    if (next === 'text') {
      setAssignee([]);
      setCandidateUsers([]);
      setCandidateGroups([]);
      editor?.setAssignmentMode(properties.id, 'text');
    }
  }

  function changeAssignee(next: DirectorySubjectSelection[]) {
    setAssignee(next);
    editor?.setDirectoryAssignment(properties.id, { assigneeId: next[0]?.subject.id ?? '' });
  }

  function changeCandidateUsers(next: DirectorySubjectSelection[]) {
    setCandidateUsers(next);
    editor?.setDirectoryAssignment(properties.id, {
      candidateUserIds: next.map((entry) => entry.subject.id),
    });
  }

  function changeCandidateGroups(next: DirectorySubjectSelection[]) {
    setCandidateGroups(next);
    editor?.setDirectoryAssignment(properties.id, {
      candidateGroupIds: next.map((entry) => entry.subject.id),
    });
  }

  return (
    <Section
      icon="person"
      title="Zuweisung"
      hint="Freitext bleibt für externe oder dynamische Kennungen erhalten. Bekannte Identitäten werden mit stabilen IDs gespeichert."
    >
      <Segmented
        options={ASSIGNMENT_MODE_OPTIONS}
        value={mode}
        disabled={readOnly}
        onChange={changeMode}
        aria-label="Art der Zuweisung"
      />

      {properties.assignmentContractWarning && (
        <Notice tone="warn">{properties.assignmentContractWarning}</Notice>
      )}

      {mode === 'text' && (
        <>
          <TextRow
            label="Zugewiesen an"
            value={properties.assignee}
            disabled={readOnly}
            placeholder="Benutzername, E-Mail oder Ausdruck"
            onCommit={(value) => editor?.setAssignment(properties.id, { assignee: value })}
          />
          <TextRow
            label="Gruppen"
            value={properties.candidateGroups}
            disabled={readOnly}
            placeholder="einkauf, buchhaltung"
            hint="Mehrere durch Komma getrennt. Freitext wird nicht automatisch mit dem Verzeichnis verknüpft."
            onCommit={(value) => editor?.setAssignment(properties.id, { candidateGroups: value })}
          />
          <TextRow
            label="Personen"
            value={properties.candidateUsers}
            disabled={readOnly}
            placeholder="anna, bruno"
            hint="Mehrere durch Komma getrennt."
            onCommit={(value) => editor?.setAssignment(properties.id, { candidateUsers: value })}
          />
        </>
      )}

      {mode === 'directory' && (
        <>
          <DirectorySubjectPicker
            definitionId={definitionId}
            kind="user"
            selected={assignee}
            multiple={false}
            disabled={readOnly}
            label="Direkter Bearbeiter"
            onChange={changeAssignee}
          />
          <DirectorySubjectPicker
            definitionId={definitionId}
            kind="user"
            selected={candidateUsers}
            multiple
            disabled={readOnly}
            label="Kandidaten"
            onChange={changeCandidateUsers}
          />
          <DirectorySubjectPicker
            definitionId={definitionId}
            kind="group"
            selected={candidateGroups}
            multiple
            disabled={readOnly}
            label="Kandidatengruppen"
            onChange={changeCandidateGroups}
          />
          {assignee.length === 0 && candidateUsers.length === 0 && candidateGroups.length === 0 && (
            <Notice tone="warn">
              Wähle mindestens einen Benutzer oder eine Gruppe. Erst dann ersetzt der Directory-Modus
              eine vorhandene Freitextzuweisung im BPMN.
            </Notice>
          )}
        </>
      )}
    </Section>
  );
}

function selection(kind: 'user' | 'group', id: string): DirectorySubjectSelection[] {
  return id.length > 0 ? selections(kind, [id]) : [];
}

function selections(kind: 'user' | 'group', ids: string[]): DirectorySubjectSelection[] {
  return ids.map((id) => ({
    subject: { kind, id },
    displayName: id,
    detail: 'Stabile Verzeichnis-ID',
    available: false,
  }));
}

function mergeSelections(
  current: DirectorySubjectSelection[],
  incoming: DirectorySubjectSelection[],
): DirectorySubjectSelection[] {
  return incoming.map(
    (entry) =>
      current.find(
        (candidate) =>
          candidate.subject.kind === entry.subject.kind && candidate.subject.id === entry.subject.id,
      ) ?? entry,
  );
}
