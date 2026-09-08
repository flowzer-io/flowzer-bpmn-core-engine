import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { BpmnEditor } from '../bpmnEditor';
import { readElementProperties } from '../elementProperties';
import type { DiagramElement, ModdleElement } from '../moddle';
import { AssignmentSection } from './AssignmentSection';

vi.mock('./DirectorySubjectPicker', () => ({
  DirectorySubjectPicker: ({
    label,
    kind,
    selected,
    onChange,
  }: {
    label: string;
    kind: 'user' | 'group';
    selected: Array<{ subject: { kind: 'user' | 'group'; id: string } }>;
    onChange: (value: Array<{ subject: { kind: 'user' | 'group'; id: string }; displayName: string; detail: string; available: boolean }>) => void;
  }) => (
    <div data-testid={`picker-${label}`}>
      {selected.map((entry) => entry.subject.id).join(',')}
      <button
        type="button"
        onClick={() =>
          onChange([
            {
              subject: { kind, id: kind === 'user' ? 'user-known' : 'group-known' },
              displayName: 'Bekannt',
              detail: 'Eindeutig',
              available: true,
            },
          ])
        }
      >
        {label} wählen
      </button>
    </div>
  ),
}));

function properties(...extensions: ModdleElement[]) {
  return readElementProperties({
    id: 'Task_1',
    type: 'bpmn:UserTask',
    businessObject: {
      $type: 'bpmn:UserTask',
      extensionElements: { $type: 'bpmn:ExtensionElements', values: extensions },
    },
  } as DiagramElement);
}

function editorDouble() {
  return {
    setAssignment: vi.fn(),
    setAssignmentMode: vi.fn(),
    setDirectoryAssignment: vi.fn(),
  } as unknown as BpmnEditor;
}

// Testzweck: Freitext bleibt der kompatible Standard. Der Directory-Schalter erzeugt noch
// keinen leeren, serverseitig ungültigen Vertrag; erst eine konkrete stabile Auswahl schreibt.
describe('AssignmentSection-Moduswechsel', () => {
  it('wechselt von Freitext zur Auswahl und schreibt erst den gewählten Benutzer', async () => {
    const editor = editorDouble();
    const user = userEvent.setup();
    render(
      <AssignmentSection
        definitionId="urlaub"
        properties={properties({ $type: 'zeebe:AssignmentDefinition', assignee: 'anna' })}
        editor={editor}
        readOnly={false}
      />,
    );

    expect(screen.getByDisplayValue('anna')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Bekannte Benutzer/Gruppen' }));

    expect(editor.setAssignmentMode).not.toHaveBeenCalledWith('Task_1', 'directory');
    expect(screen.getByText(/Wähle mindestens einen Benutzer oder eine Gruppe/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Direkter Bearbeiter wählen' }));
    expect(editor.setDirectoryAssignment).toHaveBeenCalledWith('Task_1', {
      assigneeId: 'user-known',
    });
  });

  // Testzweck: Ein bewusster Wechsel zurück zu Freitext entfernt auch lokale Directory-Chips;
  // sie dürfen bei erneutem Umschalten nicht unbemerkt in das BPMN zurückkehren.
  it('verwirft Directory-Auswahlen beim Wechsel zurück zu Freitext', async () => {
    const editor = editorDouble();
    const user = userEvent.setup();
    render(
      <AssignmentSection
        definitionId="urlaub"
        properties={properties({
          $type: 'flowzer:TaskAssignment',
          mode: 'directory',
          assigneeId: 'user-old',
        })}
        editor={editor}
        readOnly={false}
      />,
    );

    expect(screen.getByTestId('picker-Direkter Bearbeiter')).toHaveTextContent('user-old');
    await user.click(screen.getByRole('tab', { name: 'Freitext' }));
    expect(editor.setAssignmentMode).toHaveBeenCalledWith('Task_1', 'text');
    await user.click(screen.getByRole('tab', { name: 'Bekannte Benutzer/Gruppen' }));

    expect(screen.getByTestId('picker-Direkter Bearbeiter')).not.toHaveTextContent('user-old');
  });
});
