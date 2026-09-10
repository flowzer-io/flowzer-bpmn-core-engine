import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ApiError } from '@/lib/api/client';

import { TaskLifecycleDialog } from './TaskLifecycleDialog';

vi.mock('@/components/bpmn/properties/DirectorySubjectPicker', () => ({
  DirectorySubjectPicker: ({ onChange, selected }: {
    onChange: (value: unknown[]) => void;
    selected: Array<{ displayName: string }>;
  }) => (
    <div>
      {selected.map((entry) => <span key={entry.displayName}>{entry.displayName}</span>)}
      <button type="button" onClick={() => onChange([{
        subject: { kind: 'user', id: 'user-anna' },
        displayName: 'Anna Beispiel',
        detail: 'anna@example.test',
        available: true,
      }])}>
        Anna wählen
      </button>
    </div>
  ),
}));

describe('Task-Lifecycle-Dialog', () => {
  // Testzweck: Eine Freigabe wird erst mit einem protokollierbaren Grund ermöglicht
  // und behält die beim Öffnen eingefangene Taskrevision.
  it('verlangt beim Zurückgeben einen Grund', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<TaskLifecycleDialog open action="release" taskId="task-1" expectedRevision={7}
      busy={false} error={null} onOpenChange={vi.fn()} onSubmit={onSubmit} />);

    expect(screen.getByRole('button', { name: 'Zurückgeben' })).toBeDisabled();
    await user.type(screen.getByLabelText('Begründung'), 'Zurück an das Team');
    await user.click(screen.getByRole('button', { name: 'Zurückgeben' }));

    expect(onSubmit).toHaveBeenCalledWith({
      action: 'release',
      userTaskId: 'task-1',
      expectedRevision: 7,
      reason: 'Zurück an das Team',
    });
  });

  // Testzweck: Ein Revisionskonflikt darf weder gewählten Bearbeiter noch Grund
  // löschen; die Person muss den aktualisierten Stand bewusst prüfen können.
  it('bewahrt Delegationsdaten bei einem Konflikt', async () => {
    const user = userEvent.setup();
    const props = {
      open: true,
      action: 'delegate' as const,
      taskId: 'task-1',
      expectedRevision: 3,
      busy: false,
      error: null,
      onOpenChange: vi.fn(),
      onSubmit: vi.fn(),
      onUseCurrentRevision: vi.fn(),
    };
    const { rerender } = render(<TaskLifecycleDialog {...props} />);

    await user.click(screen.getByRole('button', { name: 'Anna wählen' }));
    await user.type(screen.getByLabelText('Begründung'), 'Urlaubsvertretung');
    rerender(<TaskLifecycleDialog {...props} error={new ApiError('Conflict', {
      status: 409,
      url: '/usertask/task-1/delegate',
    })} />);

    expect(screen.getByRole('alert')).toHaveTextContent('zwischenzeitlich geändert');
    expect(screen.getByRole('button', { name: 'Aktuellen Stand verwenden' })).toBeInTheDocument();
    expect(screen.getByText('Anna Beispiel')).toBeInTheDocument();
    expect(screen.getByLabelText('Begründung')).toHaveValue('Urlaubsvertretung');
  });
});
