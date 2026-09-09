import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AiConnectionsPage } from './AiConnectionsPage';

const mocks = vi.hoisted(() => ({
  update: vi.fn(),
  create: vi.fn(),
  setEnabled: vi.fn(),
  connection: {
    id: 'connection-1',
    name: 'OpenAI EU',
    provider: 'OpenAi' as const,
    location: 'Cloud' as const,
    defaultModel: 'gpt-example',
    enabled: true,
    ready: true,
    revision: 7,
    updatedAtUtc: '2026-09-09T16:00:00Z',
    allowedTools: [],
  },
  tool: {
    id: 'flowzer.directory.lookup',
    version: 1,
    name: 'Verzeichnissuche',
    description: 'Liest einen begrenzten Verzeichniseintrag.',
    inputSchema: '{"type":"object"}',
    outputSchema: '{"type":"object"}',
    sideEffect: 'ReadOnly' as const,
    allowsPreApproval: true,
    contractHash: 'A'.repeat(64),
  },
}));

vi.mock('@/stores/session', () => ({ useCan: () => () => true }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@/lib/api/queries', () => ({
  useAiConnections: () => ({
    data: [mocks.connection],
    isPending: false,
    error: null,
    refetch: vi.fn(),
  }),
  useAiTools: () => ({ data: [mocks.tool], isPending: false, isError: false }),
  useCreateAiConnection: () => ({ mutate: mocks.create, isPending: false }),
  useUpdateAiConnection: () => ({ mutate: mocks.update, isPending: false }),
  useSetAiConnectionEnabled: () => ({ mutate: mocks.setEnabled, isPending: false }),
}));

describe('KI-Verbindungsverwaltung', () => {
  beforeEach(() => vi.clearAllMocks());

  // Testzweck: Eine bestehende Verbindung fuellt niemals eine Secret-Referenz in den Browser
  // zurueck; Speichern mit leerem Feld behaelt sie serverseitig ueber einen ausgelassenen Wert.
  it('haelt die Secret-Referenz bei bestehender Verbindung leer', async () => {
    render(<AiConnectionsPage />);

    await screen.findByDisplayValue('OpenAI EU');
    const secret = screen.getByLabelText('Secret-Referenz') as HTMLInputElement;
    expect(secret.value).toBe('');
    expect(screen.queryByText(/FLOWZER_AI_OPENAI/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Speichern' }));

    await waitFor(() => expect(mocks.update).toHaveBeenCalledOnce());
    expect(mocks.update.mock.calls[0]![0]).toMatchObject({
      connectionId: 'connection-1',
      input: { expectedRevision: 7, secretReference: undefined },
    });
  });

  // Testzweck: Eine neue Verbindung verlangt sichtbar eine Secret-Referenz, ohne daraus ein
  // Passwortfeld zu machen, das einen geheimen Wert im Formular suggerieren wuerde.
  it('beginnt eine neue Verbindung mit leerer Referenz', async () => {
    render(<AiConnectionsPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Verbindung anlegen' }));

    expect((screen.getByLabelText('Secret-Referenz') as HTMLInputElement).value).toBe('');
    expect(screen.getByPlaceholderText('env:FLOWZER_AI_OPENAI')).toBeInTheDocument();
  });

  // Testzweck: Die Verwaltung uebertraegt nur explizit ausgewaehlte registrierte
  // Werkzeugversionen und die getrennte administrative Vorabfreigabegrenze.
  it('speichert die Werkzeug-Allowlist mit expliziter Vorabfreigabe', async () => {
    render(<AiConnectionsPage />);
    await screen.findByDisplayValue('OpenAI EU');

    fireEvent.click(screen.getByRole('checkbox', { name: /Verzeichnissuche/ }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Vorabfreigabe erlauben' }));
    fireEvent.click(screen.getByRole('button', { name: 'Speichern' }));

    await waitFor(() => expect(mocks.update).toHaveBeenCalledOnce());
    expect(mocks.update.mock.calls[0]![0].input.allowedTools).toEqual([
      {
        toolId: 'flowzer.directory.lookup',
        toolVersion: 1,
        allowPreApproval: true,
      },
    ]);
  });
});
