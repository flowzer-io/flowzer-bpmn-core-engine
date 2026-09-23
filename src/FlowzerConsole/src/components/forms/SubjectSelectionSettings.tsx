import { useId, useMemo } from 'react';
import { DirectorySubjectPicker, type DirectorySubjectSelection } from '@/components/bpmn/properties/DirectorySubjectPicker';
import { authoringDirectoryAdapter } from './authoringDirectoryAdapter';

export interface SubjectSelectionPolicy {
  allowUsers?: boolean; allowGroups?: boolean; activeOnly?: boolean; includeSubgroups?: boolean;
  allowedUserIds?: string[]; userMemberOfGroupIds?: string[]; allowedGroupIds?: string[];
}

/** Einfache UI, unveränderter boolescher Policy-Vertrag – keine zusätzlichen Migrationsfelder. */
export function SubjectSelectionSettings({ value, onChange, formId }: {
  value: SubjectSelectionPolicy; onChange: (value: SubjectSelectionPolicy) => void; formId?: string;
}) {
  const selectId = useId();
  const adapter = useMemo(() => formId ? authoringDirectoryAdapter(formId) : undefined, [formId]);
  const users = value.allowUsers !== false;
  const groups = value.allowGroups === true;
  const mode = users && groups ? 'all' : groups ? 'group' : users ? 'user' : '';
  const change = (next: Partial<SubjectSelectionPolicy>) => onChange({ ...value, activeOnly: true, ...next });
  function chooseMode(next: string) {
    change({ allowUsers: next !== 'group', allowGroups: next !== 'user',
      ...(next === 'group' ? { allowedUserIds: [], userMemberOfGroupIds: [] } : {}),
      ...(next === 'user' ? { allowedGroupIds: [] } : {}),
    });
  }
  function filter(key: 'allowedUserIds' | 'userMemberOfGroupIds' | 'allowedGroupIds', kind: 'user' | 'group', label: string) {
    if (!adapter) return <p className="text-muted text-sm">{label}: Bitte das Formular zur Filterpflege in der Formularbibliothek öffnen.</p>;
    const selected: DirectorySubjectSelection[] = (value[key] ?? []).map(id => ({
      subject: { kind, id }, displayName: id, detail: '', available: false,
    }));
    return <DirectorySubjectPicker definitionId="" directoryAdapter={adapter} fieldKey={key} kind={kind} multiple
      displayFields={{ name: true, email: true }} label={label} selected={selected} disabled={!adapter}
      disabledReason={!adapter ? 'Bitte das Formular in der Formularbibliothek öffnen, um Filter auszuwählen.' : undefined}
      onChange={entries => change({ [key]: entries.map(entry => entry.subject.id) })} />;
  }
  return <div className="flex flex-col gap-4">
    <div>
      <label htmlFor={selectId} className="mb-1 block font-semibold">Was darf ausgewählt werden?</label>
      <select id={selectId} value={mode} onChange={event => chooseMode(event.target.value)} className="form-control" style={{ height: 'auto', minHeight: 42 }}>
        <option value="" disabled>Bitte auswählen</option>
        <option value="user">Nur Benutzer</option><option value="group">Nur Gruppen</option><option value="all">Benutzer und Gruppen</option>
      </select>
      <p className="text-muted mt-1 text-xs">Beim Wechsel werden Filter für die abgewählte Art entfernt. Es sind immer nur aktive Einträge auswählbar.</p>
    </div>
    <fieldset className="flex flex-col gap-3 rounded border p-3">
      <legend className="px-1 font-semibold">Auswahl einschränken (optional)</legend>
      <p className="text-muted text-sm">Ohne Filter stehen alle aktiven Einträge der gewählten Art zur Verfügung.</p>
      {users && <>
        {filter('allowedUserIds', 'user', 'Bestimmte Benutzer')}
        {filter('userMemberOfGroupIds', 'group', 'Benutzer aus diesen Gruppen')}
        <p className="text-muted text-xs">Wenn beide Filter gesetzt sind: ausgewählte Benutzer <strong>oder</strong> Mitglieder einer gewählten Gruppe.</p>
      </>}
      {groups && filter('allowedGroupIds', 'group', 'Bestimmte Gruppen')}
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={value.includeSubgroups === true}
        onChange={event => change({ includeSubgroups: event.target.checked })} />Untergruppen einbeziehen</label>
    </fieldset>
  </div>;
}
