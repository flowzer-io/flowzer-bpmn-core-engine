import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { UserTaskWorkState } from '@flowzer/sdk';

import { TaskLifecyclePanel } from './TaskLifecyclePanel';

const available: UserTaskWorkState = {
  revision: 4,
  claimed: false,
  actualAssigneeDisplayName: null,
  isAssignedToCurrentUser: false,
  canWork: false,
  canClaim: true,
  canRelease: false,
  canAssign: false,
  canDelegate: false,
};

describe('Task-Lifecycle-Bedienung', () => {
  // Testzweck: Eine freie Kandidatenaufgabe wird nicht als persönliche Zuweisung
  // dargestellt und kann mit genau ihrer aktuellen Revision übernommen werden.
  it('zeigt eine freie Aufgabe und bietet Claim an', async () => {
    const user = userEvent.setup();
    const onClaim = vi.fn();
    render(<TaskLifecyclePanel state={available} busy={false} draftDirty={false} onClaim={onClaim}
      onRelease={vi.fn()} onAssign={vi.fn()} onDelegate={vi.fn()} />);

    expect(screen.getByText('Offen zur Übernahme')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Übernehmen' }));
    expect(onClaim).toHaveBeenCalledWith(4);
  });

  // Testzweck: Nach Claim zeigt die Oberfläche den tatsächlichen Bearbeiter und nur
  // die serverseitig erlaubten Übergabeaktionen.
  it('zeigt den eigenen Claim mit Release und Delegation', () => {
    render(<TaskLifecyclePanel state={{ ...available, revision: 5, claimed: true,
      actualAssignee: { kind: 'user', id: 'user-anna' }, actualAssigneeDisplayName: 'Anna Beispiel',
      isAssignedToCurrentUser: true, canWork: true, canClaim: false, canRelease: true,
      canDelegate: true }} busy={false} draftDirty={false} onClaim={vi.fn()}
      onRelease={vi.fn()} onAssign={vi.fn()} onDelegate={vi.fn()} />);

    expect(screen.getByText('Von dir übernommen')).toBeInTheDocument();
    expect(screen.getByText('Anna Beispiel')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Zurückgeben' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delegieren' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Zuweisen' })).not.toBeInTheDocument();
  });

  // Testzweck: Eine Übergabe darf keine ungespeicherten Formulareingaben verlieren;
  // Release und Delegate bleiben deshalb bis zum Speichern oder Verwerfen gesperrt.
  it('blockiert Übergaben bei lokalen Änderungen', () => {
    render(<TaskLifecyclePanel state={{ ...available, claimed: true,
      isAssignedToCurrentUser: true, canWork: true, canClaim: false, canRelease: true,
      canDelegate: true }} busy={false} draftDirty onClaim={vi.fn()}
      onRelease={vi.fn()} onAssign={vi.fn()} onDelegate={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Zurückgeben' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Delegieren' })).toBeDisabled();
    expect(screen.getByText(/Entwurf zuerst speichern oder verwerfen/)).toBeInTheDocument();
  });
});
