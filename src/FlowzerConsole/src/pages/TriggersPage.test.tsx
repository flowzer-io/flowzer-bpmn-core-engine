import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { TriggersPage } from './TriggersPage';

const mocks = vi.hoisted(() => ({
  create: vi.fn(),
  update: vi.fn(),
  rotate: vi.fn(),
  remove: vi.fn(),
  trigger: {
    id: 'trigger-1',
    key: 'shop-bestellung-4f2a',
    name: 'Bestellung aus dem Shop',
    kind: 'start' as const,
    definitionId: 'bestellprozess',
    messageName: null,
    correlationKeyPath: null,
    variablesMode: 'body' as const,
    allowedFields: [] as string[],
    enabled: true,
    createdAt: '2026-09-01T08:00:00Z',
    lastUsedAt: '2026-09-18T10:30:00Z',
    useCount: 12,
    lastFailureAt: '2026-09-17T09:15:00Z',
    lastFailureReason: 'signature',
  },
  definition: {
    definitionId: 'bestellprozess',
    name: 'Bestellprozess',
    latestVersionDateTime: '2026-09-01T08:00:00Z',
    deployedVersionDateTime: '2026-09-01T08:00:00Z',
  },
}));

vi.mock('@/stores/session', () => ({ useCan: () => () => true }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@/lib/api/queries', () => ({
  useInboundTriggers: () => ({
    data: [mocks.trigger],
    isPending: false,
    error: null,
    refetch: vi.fn(),
  }),
  useDefinitions: () => ({
    data: [mocks.definition],
    isPending: false,
    error: null,
    refetch: vi.fn(),
  }),
  useCreateInboundTrigger: () => ({ mutate: mocks.create, isPending: false }),
  useUpdateInboundTrigger: () => ({ mutate: mocks.update, isPending: false }),
  useRotateInboundTriggerSecret: () => ({ mutate: mocks.rotate, isPending: false }),
  useDeleteInboundTrigger: () => ({ mutate: mocks.remove, isPending: false }),
}));

describe('Auslöserverwaltung', () => {
  beforeEach(() => vi.clearAllMocks());

  // Testzweck: Die Liste ist die einzige Stelle, an der der Betrieb einen Auslöser
  // beurteilen kann — Art, Ziel, Schlüssel, Nutzung und der letzte Fehlgrund müssen
  // dort stehen, und der Fehlgrund in verständlichem Deutsch statt als Code.
  it('zeigt Art, Ziel, Schlüssel, Nutzung und den letzten Fehler', () => {
    render(<TriggersPage />);

    expect(screen.getByText('Bestellung aus dem Shop')).toBeInTheDocument();
    expect(screen.getByText('Workflow starten')).toBeInTheDocument();
    expect(screen.getByText('Bestellprozess')).toBeInTheDocument();
    expect(screen.getByText('shop-bestellung-4f2a')).toBeInTheDocument();
    expect(screen.getByText(/12 Aufrufe/)).toBeInTheDocument();
    expect(screen.getByText(/Signatur passte nicht zum Geheimnis/)).toBeInTheDocument();
    expect(screen.queryByText(/^signature$/)).not.toBeInTheDocument();
    expect(screen.getByText(/\/trigger\/shop-bestellung-4f2a$/)).toBeInTheDocument();
  });

  // Testzweck: Der Anlegen-Dialog ist die einzige Stelle, an der die Art gewählt wird.
  // Art, Ziel, Variablenmodus und die erlaubten Felder müssen unverfälscht und als
  // getrennte Werte an die Mutation gehen — nicht als roher Text des Eingabefelds.
  it('überträgt Art, Ziel, Variablenmodus und erlaubte Felder an die Mutation', async () => {
    render(<TriggersPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Auslöser anlegen' }));

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Zahlungseingang' } });
    fireEvent.change(screen.getByLabelText('Art'), { target: { value: 'message' } });
    fireEvent.change(screen.getByLabelText('Nachrichtenname'), { target: { value: 'Zahlung' } });
    fireEvent.change(screen.getByLabelText('Korrelationspfad'), { target: { value: 'order.id' } });
    fireEvent.change(screen.getByLabelText('Variablen'), { target: { value: 'fields' } });
    fireEvent.change(screen.getByLabelText('Erlaubte Felder'), { target: { value: 'order, customer' } });
    fireEvent.click(screen.getByRole('button', { name: 'Anlegen' }));

    await waitFor(() => expect(mocks.create).toHaveBeenCalledOnce());
    expect(mocks.create.mock.calls[0]![0]).toEqual({
      name: 'Zahlungseingang',
      kind: 'message',
      messageName: 'Zahlung',
      correlationKeyPath: 'order.id',
      variablesMode: 'fields',
      allowedFields: ['order', 'customer'],
    });
  });

  // Testzweck: Das Geheimnis ist genau einmal abrufbar. Es muss mit curl-Beispiel
  // erscheinen, nach dem Schließen vollständig aus dem Dokument verschwinden und darf
  // auch danach nirgends in der Liste stehen.
  it('zeigt das Geheimnis mit curl-Beispiel genau einmal', async () => {
    mocks.create.mockImplementation((
      _input: unknown,
      options?: { onSuccess?: (result: unknown) => void },
    ) => {
      options?.onSuccess?.({
        trigger: { ...mocks.trigger, id: 'trigger-2', key: 'neuer-schluessel', name: 'Neuer Auslöser' },
        secret: 'sehr-geheimer-wert',
      });
    });

    render(<TriggersPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Auslöser anlegen' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Neuer Auslöser' } });
    fireEvent.change(screen.getByLabelText('Workflow'), { target: { value: 'bestellprozess' } });
    fireEvent.click(screen.getByRole('button', { name: 'Anlegen' }));

    expect(await screen.findByText('sehr-geheimer-wert')).toBeInTheDocument();
    expect(screen.getByText(/openssl dgst -sha256 -hmac/)).toBeInTheDocument();
    expect(screen.getByText(/X-Flowzer-Signature: sha256=\$SIG/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Geheimnis kopieren' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Fertig' }));

    await waitFor(() => expect(screen.queryByText('sehr-geheimer-wert')).not.toBeInTheDocument());
    expect(screen.queryByText(/sehr-geheimer-wert/)).not.toBeInTheDocument();
    expect(screen.getByText('Bestellung aus dem Shop')).toBeInTheDocument();
  });
});
