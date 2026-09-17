import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type * as InstanceView from '@/lib/instanceView';

import { InstancesPage } from './InstancesPage';

const mocks = vi.hoisted(() => ({ instances: vi.fn() }));
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/lib/api/queries', () => ({ useInstances: mocks.instances }));
vi.mock('@/lib/instanceView', async (importOriginal) => ({
  ...(await importOriginal<typeof InstanceView>()),
  useDefinitionModels: () => new Map(),
}));

const instance = {
  instanceId: 'a1b2c3d4-0000-0000-0000-000000000000', definitionId: 'definition-1',
  relatedDefinitionId: 'urlaub', relatedDefinitionName: 'Urlaubsantrag', state: 'Waiting',
  canInspect: true, userTaskSubscriptionCount: 0, messageSubscriptionCount: 0,
  signalSubscriptionCount: 0, serviceSubscriptionCount: 0, tokens: [],
  startedAt: '2026-09-08T10:00:00Z',
};

beforeEach(() => vi.clearAllMocks());

describe('Instanzliste', () => {
  // Testzweck: Instanzen desselben Workflows laufen nach einem Deployment auf verschiedenen
  // Versionen. Die Liste nennt die Version je Zeile, damit man sie auseinanderhalten kann.
  it('nennt je Instanz die gebundene Workflow-Version', () => {
    mocks.instances.mockReturnValue({
      data: [
        { ...instance, definitionVersion: { major: 1, minor: 0 } },
        { ...instance, instanceId: 'ffffffff-0000-0000-0000-000000000000', definitionVersion: { major: 2, minor: 0 } },
      ],
      isPending: false,
    });
    render(<InstancesPage />);

    expect(screen.getByText(/v1\.0/)).toBeInTheDocument();
    expect(screen.getByText(/v2\.0/)).toBeInTheDocument();
  });

  // Testzweck: Liegt die gebundene Definition nicht mehr vor, steht dort eine erkennbar
  // unbekannte Version statt einer geratenen oder gar keiner.
  it('kennzeichnet eine unbekannte Version', () => {
    mocks.instances.mockReturnValue({ data: [{ ...instance, definitionVersion: null }], isPending: false });
    render(<InstancesPage />);

    expect(screen.getByText(/v\?/)).toBeInTheDocument();
  });
});
