import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { TaskDraftConflictBanner, TaskDraftStatus } from './TaskDraftStatus';

const statusProps = {
  loadState: 'ready' as const,
  saveState: 'dirty' as const,
  isRefreshing: false,
  isSaving: false,
  isDiscarding: false,
  hasDraft: false,
  updatedAtUtc: null,
  error: null,
  onSave: vi.fn(),
  onDiscard: vi.fn(),
  onLoadServer: vi.fn(),
  dirty: true,
};

describe('Aufgabenentwurf-Status', () => {
  // Testzweck: Speichern und Verwerfen sind explizite, zugängliche Aktionen; der
  // Status benennt ungespeicherte lokale Eingaben eindeutig.
  it('zeigt lokale Änderungen und die beiden Draft-Aktionen', async () => {
    const user = userEvent.setup();
    render(<TaskDraftStatus {...statusProps} />);

    expect(screen.getByText('Ungespeicherte Eingaben')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Speichern' }));
    await user.click(screen.getByRole('button', { name: 'Verwerfen' }));
    expect(statusProps.onSave).toHaveBeenCalledOnce();
    expect(statusProps.onDiscard).toHaveBeenCalledOnce();
  });

  // Testzweck: Der 409-Hinweis lässt die lokale Eingabe unangetastet und verlangt
  // eine ausdrückliche Entscheidung für den Serverstand.
  it('bietet bei einem Konflikt das explizite Laden des Serverstands an', async () => {
    const user = userEvent.setup();
    const onLoadServer = vi.fn();
    render(<TaskDraftConflictBanner error={new Error('409')} loading={false} onLoadServer={onLoadServer} />);

    expect(screen.getByRole('alert')).toHaveTextContent('zwischenzeitlich geändert');
    await user.click(screen.getByRole('button', { name: 'Serverstand laden' }));
    expect(onLoadServer).toHaveBeenCalledOnce();
  });
});
