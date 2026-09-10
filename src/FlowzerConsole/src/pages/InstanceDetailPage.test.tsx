import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { InstanceDetailPage } from './InstanceDetailPage';

const mocks = vi.hoisted(() => ({ instance: vi.fn(), runtime: vi.fn(), history: vi.fn(), subscriptions: vi.fn(), navigate: vi.fn() }));
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => mocks.navigate }));
vi.mock('@flowzer/react', () => ({
  useInstanceHistory: mocks.history,
  useInstanceRuntimeDiagram: mocks.runtime,
}));
vi.mock('@/stores/breadcrumbs', () => ({ useBreadcrumbs: vi.fn() }));
vi.mock('@/lib/api/queries', () => ({
  useInstance: mocks.instance, useInstanceSubscriptions: mocks.subscriptions,
  queryKeys: {},
}));
vi.mock('@/components/bpmn/BpmnViewer', () => ({ BpmnViewer: () => <div>Technisches Diagramm</div> }));

const overview = {
  instanceId: 'instance-1', definitionId: 'definition-1', relatedDefinitionId: 'urlaub',
  relatedDefinitionName: 'Urlaubsantrag', state: 'Waiting', canInspect: false,
  userTaskSubscriptionCount: 1, messageSubscriptionCount: 0, signalSubscriptionCount: 0,
  serviceSubscriptionCount: 0, tokens: [], startedAt: '2026-09-08T10:00:00Z',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.instance.mockReturnValue({ data: overview, isPending: false });
  mocks.runtime.mockReturnValue({ data: undefined, isPending: false });
  mocks.history.mockReturnValue({ data: undefined, isPending: false });
  mocks.subscriptions.mockReturnValue({ data: undefined, isPending: false });
});

describe('Datensparsame Instanzansicht', () => {
  // Testzweck: Ohne Diagnosefreigabe werden weder BPMN noch technische Subscriptions
  // angefordert, auch nicht schon während des initialen Ladens.
  it('fragt für die Übersicht keine technischen Ressourcen an', () => {
    render(<InstanceDetailPage instanceId="instance-1" />);
    expect(mocks.runtime).toHaveBeenCalledWith('instance-1', {
      enabled: false,
      refetchInterval: false,
    });
    expect(mocks.subscriptions).toHaveBeenCalledWith(undefined);
    expect(mocks.history).toHaveBeenCalledWith('instance-1', { enabled: false });
  });

  // Testzweck: Fehlende Tokens sind in der Übersicht beabsichtigter Datenschutz und
  // dürfen weder als leere Prozessdaten noch als kaputtes Diagramm erscheinen.
  it('erklärt die reduzierte Übersicht statt leere Diagnosetabs zu zeigen', () => {
    render(<InstanceDetailPage instanceId="instance-1" />);
    expect(screen.getByText('Vorgangsübersicht')).toBeInTheDocument();
    expect(screen.getByText('Urlaubsantrag')).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Variablen' })).not.toBeInTheDocument();
    expect(screen.queryByText('Technisches Diagramm')).not.toBeInTheDocument();
  });

  // Testzweck: Die Diagnoseansicht zeigt echte append-only Engine- und Aufgabenereignisse,
  // aber keine aus dem aktuellen Tokenstand erfundene zweite Historie.
  it('zeigt Engine- und Aufgabenereignisse ohne Token-Pseudohistorie', async () => {
    mocks.instance.mockReturnValue({
      data: {
        ...overview,
        canInspect: true,
        tokens: [{
          id: 'token-1', currentFlowNodeId: 'Review', state: 'Active', variables: {},
          startTime: '2026-09-09T09:55:00Z', lastStateChangeTime: '2026-09-09T09:55:00Z',
        }],
      },
      isPending: false,
    });
    mocks.history.mockReturnValue({
      data: {
        instanceId: 'instance-1',
        events: [{
          id: 'event-1', userTaskId: 'task-1', flowNodeId: 'Review', action: 'claim',
          revision: 1, occurredAtUtc: '2026-09-09T10:00:00Z',
        }],
      },
      isPending: false,
    });
    mocks.runtime.mockReturnValue({
      data: {
        instanceId: 'instance-1', definitionId: 'definition-1', processId: 'Process_1', state: 2,
        snapshotAtUtc: '2026-09-09T10:00:00Z', diagramXml: '<definitions />',
        nodes: [{ flowNodeId: 'Review', status: 0, tokenCount: 1 }],
        events: [{
          id: 'runtime-1', flowNodeId: 'Review', state: 1,
          occurredAtUtc: '2026-09-09T09:59:00Z',
        }],
      },
      isPending: false,
    });

    const user = userEvent.setup();
    render(<InstanceDetailPage instanceId="instance-1" />);
    await user.click(screen.getByRole('tab', { name: 'Verlauf' }));

    expect(mocks.history).toHaveBeenCalledWith('instance-1', { enabled: true });
    expect(mocks.runtime).toHaveBeenCalledWith('instance-1', {
      enabled: true,
      refetchInterval: 10_000,
    });
    expect(screen.getByText(/Übernommen · Revision 1/)).toBeInTheDocument();
    expect(screen.getByText('Engine-Ereignisse')).toBeInTheDocument();
    expect(screen.getByText('Aufgabenaktionen')).toBeInTheDocument();
    expect(screen.queryByText('Aktueller Tokenstand')).not.toBeInTheDocument();
  });

  // Testzweck: Die technische Instanzsicht liest den Prozessscope aus dem
  // Master-Token und trennt ihn von den persistierten Ein-/Ausgaben eines Knotens.
  it('zeigt Prozessvariablen sowie Ein- und Ausgaben einzelner Ausführungen', async () => {
    mocks.instance.mockReturnValue({
      data: {
        ...overview,
        canInspect: true,
        tokens: [
          {
            id: 'master-token', parentTokenId: null, currentFlowNodeId: 'Process_1', state: 'Active',
            variables: { requestId: 'REQ-42' }, outputData: null,
            startTime: '2026-09-09T09:00:00Z', lastStateChangeTime: '2026-09-09T09:00:00Z',
          },
          {
            id: 'review-token', parentTokenId: 'master-token', currentFlowNodeId: 'Review', state: 'Active',
            variables: { selectedApprover: 'directory-user-7' }, outputData: { decision: 'approved' },
            startTime: '2026-09-09T09:30:00Z', lastStateChangeTime: '2026-09-09T09:35:00Z',
          },
        ],
      },
      isPending: false,
    });
    mocks.runtime.mockReturnValue({
      data: {
        instanceId: 'instance-1', definitionId: 'definition-1', processId: 'Process_1', state: 2,
        snapshotAtUtc: '2026-09-09T10:00:00Z', diagramXml: '<definitions />', events: [],
        nodes: [{ flowNodeId: 'Review', status: 0, tokenCount: 1 }],
      },
      isPending: false,
    });

    const user = userEvent.setup();
    render(<InstanceDetailPage instanceId="instance-1" />);

    expect(screen.getByText('requestId')).toBeInTheDocument();
    expect(screen.getByText('"REQ-42"')).toBeInTheDocument();
    expect(screen.queryByText('selectedApprover')).not.toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Schrittdaten' }));

    expect(screen.getByText('Gebundener Input')).toBeInTheDocument();
    expect(screen.getByText('selectedApprover')).toBeInTheDocument();
    expect(screen.getByText('"directory-user-7"')).toBeInTheDocument();
    expect(screen.getByText('Erzeugter Output')).toBeInTheDocument();
    expect(screen.getByText('decision')).toBeInTheDocument();
    expect(screen.getByText('"approved"')).toBeInTheDocument();
  });
});
