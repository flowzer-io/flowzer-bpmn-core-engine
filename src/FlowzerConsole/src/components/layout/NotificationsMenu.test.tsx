import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const notificationsMock = vi.hoisted(() => vi.fn());
const markReadMock = vi.hoisted(() => vi.fn());

vi.mock('@/lib/api/queries', () => ({
  useNotifications: notificationsMock,
  useMarkNotificationRead: () => ({ mutate: markReadMock }),
}));
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));

import { NotificationsMenu } from './NotificationsMenu';

describe('NotificationsMenu', () => {
  beforeEach(() => {
    notificationsMock.mockReset();
    markReadMock.mockReset();
  });

  // Testzweck: Der persistente Serverfeed rendert die vom Server gelieferte Meldung und
  // quittiert sie erst beim bewussten Öffnen der Meldung.
  it('rendert Feed und markiert ungelesene Meldung beim Klick gelesen', () => {
    notificationsMock.mockReturnValue({
      data: [{
        id: 'notification-1',
        kind: 'due',
        occurredAtUtc: '2026-09-08T10:00:00Z',
        readAtUtc: null,
        title: 'Aufgabe fällig',
        message: 'Freigabe erteilen',
      }],
      isPending: false,
      error: null,
    });

    render(<NotificationsMenu />);
    fireEvent.click(screen.getByRole('button', { name: /Benachrichtigungen/ }));
    expect(screen.getByText('Aufgabe fällig: Freigabe erteilen')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Aufgabe fällig: Freigabe erteilen'));

    expect(markReadMock).toHaveBeenCalledWith('notification-1');
  });

  // Testzweck: Lade- und Fehlerzustände des persistenten Endpoints bleiben für Nutzer
  // verständlich, statt einen leeren Feed als „keine Meldungen“ auszugeben.
  it('zeigt Lade- und Fehlerzustand', () => {
    notificationsMock.mockReturnValue({ data: undefined, isPending: true, error: null });
    const { rerender } = render(<NotificationsMenu />);
    fireEvent.click(screen.getByRole('button', { name: 'Benachrichtigungen' }));
    expect(screen.getByText('Benachrichtigungen werden geladen …')).toBeInTheDocument();

    notificationsMock.mockReturnValue({ data: undefined, isPending: false, error: new Error('offline') });
    rerender(<NotificationsMenu />);
    expect(screen.getByRole('alert')).toHaveTextContent('konnten nicht geladen werden');
  });
});
