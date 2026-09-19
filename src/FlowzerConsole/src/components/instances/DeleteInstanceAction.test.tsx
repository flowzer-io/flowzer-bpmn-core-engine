import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DeleteInstanceAction } from './DeleteInstanceAction';
import type { ProcessInstanceInfoDto } from '@/lib/api/types';

const mocks = vi.hoisted(() => ({
  remove: vi.fn(),
  navigate: vi.fn(),
  can: vi.fn(),
  success: vi.fn(),
  error: vi.fn(),
}));

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => mocks.navigate }));
vi.mock('@/stores/session', () => ({ useCan: () => mocks.can }));
vi.mock('@/lib/api/queries', () => ({
  useDeleteInstance: () => ({ mutate: mocks.remove, isPending: false }),
}));
vi.mock('sonner', () => ({ toast: { success: mocks.success, error: mocks.error } }));

const instance = {
  instanceId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  definitionId: 'definition-1',
  relatedDefinitionId: 'urlaub',
  relatedDefinitionName: 'Urlaubsantrag',
  definitionVersion: { major: 1, minor: 2 },
  state: 'Completed',
  canInspect: true,
  messageSubscriptionCount: 0,
  signalSubscriptionCount: 0,
  userTaskSubscriptionCount: 0,
  serviceSubscriptionCount: 0,
  tokens: [],
  startedAt: '2026-09-08T10:00:00Z',
} as unknown as ProcessInstanceInfoDto;

beforeEach(() => {
  vi.clearAllMocks();
  mocks.can.mockReturnValue(true);
});

describe('Instanz löschen', () => {
  // Testzweck: Ohne Betriebsrecht wird die Aktion gar nicht erst angeboten. Ein Knopf, der
  // unwiderruflich fremde Vorgangsdaten entfernt und dann am Recht scheitert, lädt nur zum
  // Fehlversuch ein.
  it('bietet das Löschen ohne Betriebsrecht nicht an', () => {
    mocks.can.mockReturnValue(false);

    render(<DeleteInstanceAction instance={instance} />);

    expect(screen.queryByRole('button', { name: 'Instanz löschen' })).not.toBeInTheDocument();
  });

  // Testzweck: Gelöscht wird erst nach ausdrücklicher Bestätigung, und die Rückfrage nennt den
  // Workflow. Wer mehrere Instanzen offen hat, muss vor dem unwiderruflichen Schritt sehen,
  // welche er trifft.
  it('löscht die Instanz erst nach ausdrücklicher Bestätigung', async () => {
    const user = userEvent.setup();
    render(<DeleteInstanceAction instance={instance} />);

    await user.click(screen.getByRole('button', { name: 'Instanz löschen' }));
    expect(mocks.remove).not.toHaveBeenCalled();

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Urlaubsantrag');
    await user.click(within(dialog).getByRole('button', { name: 'Endgültig löschen' }));

    expect(mocks.remove).toHaveBeenCalledWith(instance.instanceId, expect.anything());
  });

  // Testzweck: Die Rückfrage sagt, dass der Verlauf mitgeht. Wer nur „Instanz löschen" liest,
  // rechnet mit einem Listeneintrag weniger — nicht damit, dass die Historie des Vorgangs
  // ebenfalls verschwindet.
  it('nennt in der Rückfrage, dass Verlauf und Aufgabendaten mitgehen', async () => {
    const user = userEvent.setup();
    render(<DeleteInstanceAction instance={instance} />);

    await user.click(screen.getByRole('button', { name: 'Instanz löschen' }));

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent(/Verlauf/);
    expect(dialog).toHaveTextContent(/nicht rückgängig/);
  });

  // Testzweck: Nach dem Löschen führt die Oberfläche zurück in die Liste. Die Detailseite zeigt
  // sonst einen Vorgang, den es nicht mehr gibt, und ein Nachladen liefe in einen 404.
  it('kehrt nach dem Löschen in die Instanzliste zurück', async () => {
    mocks.remove.mockImplementation((_id: string, options: { onSuccess: () => void }) =>
      options.onSuccess(),
    );
    const user = userEvent.setup();
    render(<DeleteInstanceAction instance={instance} />);

    await user.click(screen.getByRole('button', { name: 'Instanz löschen' }));
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Endgültig löschen' }),
    );

    expect(mocks.navigate).toHaveBeenCalledWith({ to: '/instances' });
    expect(mocks.success).toHaveBeenCalledWith('Instanz gelöscht');
  });
});
