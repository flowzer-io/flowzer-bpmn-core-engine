import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { SubjectSelectionSettings } from './SubjectSelectionSettings';

vi.mock('@/components/bpmn/properties/DirectorySubjectPicker', () => ({
  DirectorySubjectPicker: ({ label, onChange }: { label: string; onChange: (entries: unknown[]) => void }) =>
    <button onClick={() => onChange([{ subject: { kind: 'group', id: 'group-1' } }])}>{label}</button>,
}));

describe('Einfache Verzeichniskonfiguration', () => {
  // Testzweck: Die Auswahlart ist ein einzelnes verständliches Feld statt widersprüchlicher Flags.
  it('wechselt zu Gruppen und entfernt nur unpassende Benutzerfilter', async () => {
    const onChange = vi.fn();
    render(<SubjectSelectionSettings value={{ allowUsers: true, allowedUserIds: ['old-user'], allowedGroupIds: ['kept-group'] }} onChange={onChange} formId="form-1" />);
    await userEvent.selectOptions(screen.getByLabelText('Was darf ausgewählt werden?'), 'group');
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ allowUsers: false, allowGroups: true,
      allowedUserIds: [], userMemberOfGroupIds: [], allowedGroupIds: ['kept-group'] }));
    expect(screen.queryByText(/JSON|UUID/)).not.toBeInTheDocument();
  });
  // Testzweck: Gruppenfilter speichern unverändert stabile IDs, keine Anzeigenamen.
  it('übernimmt ausgewählte Gruppen ohne Freitextkonfiguration', async () => {
    const onChange = vi.fn();
    render(<SubjectSelectionSettings value={{ allowUsers: true }} onChange={onChange} formId="form-1" />);
    await userEvent.click(screen.getByRole('button', { name: 'Benutzer aus diesen Gruppen' }));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ userMemberOfGroupIds: ['group-1'] }));
  });
});
