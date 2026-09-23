import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { DashboardPage } from './DashboardPage';

const mocks = vi.hoisted(() => ({
  storage: {
    storageRootHint: '(default)',
    totalDefinitions: 3,
    activeDefinitions: 2,
    definitionMetadataEntries: 3,
    formMetadataEntries: 1,
    totalInstances: 20,
    activeInstances: 4,
    completedInstances: 10,
    failedInstances: 2,
    cancelledInstances: 4,
    pendingMessages: 0,
    pendingTimers: 0,
    openUserTasks: 0,
    pendingSignals: 0,
    pendingServices: 0,
  },
}));

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@flowzer/react', () => ({
  useUserTasks: () => ({ data: [], isPending: false, error: null, refetch: vi.fn() }),
}));
vi.mock('@/lib/activity', () => ({ useActivityFeed: () => [] }));
vi.mock('@/stores/session', () => ({
  useSession: (selector: (state: { user: { name: string } }) => unknown) =>
    selector({ user: { name: 'Christian Maaß' } }),
}));
vi.mock('@/components/workflows/useStartWorkflow', () => ({
  useStartWorkflow: () => ({ isBusy: () => false, start: vi.fn(), dialog: {} }),
}));
vi.mock('@/components/workflows/StartWorkflowDialog', () => ({ StartWorkflowDialog: () => null }));
vi.mock('@/lib/api/queries', () => ({
  useDiagnostics: () => ({ data: { storage: mocks.storage }, isPending: false, error: null, refetch: vi.fn() }),
  useDefinitions: () => ({ data: [], isPending: false, error: null, refetch: vi.fn() }),
}));

describe('Startseite', () => {
  // Testzweck: Die Kachel „Laufende Instanzen“ darf Abbrüche nicht als Fehler ausweisen; sie
  // nennt sie als eigenen Ausgang neben den abgeschlossenen und den fehlerhaften Instanzen.
  it('nennt Abbrüche getrennt von den fehlerhaften Instanzen', () => {
    render(<DashboardPage />);

    expect(screen.getByText('10 abgeschlossen · 4 abgebrochen · 2 fehlerhaft')).toBeInTheDocument();
  });
});
