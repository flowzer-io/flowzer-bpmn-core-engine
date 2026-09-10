import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { InstanceDetailPage } from './InstanceDetailPage';

const mocks = vi.hoisted(() => ({ instance: vi.fn(), xml: vi.fn(), subscriptions: vi.fn(), navigate: vi.fn() }));
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => mocks.navigate }));
vi.mock('@/stores/breadcrumbs', () => ({ useBreadcrumbs: vi.fn() }));
vi.mock('@/lib/api/queries', () => ({
  useInstance: mocks.instance, useDefinitionXml: mocks.xml, useInstanceSubscriptions: mocks.subscriptions,
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
  mocks.xml.mockReturnValue({ data: undefined, isPending: false });
  mocks.subscriptions.mockReturnValue({ data: undefined, isPending: false });
});

describe('Datensparsame Instanzansicht', () => {
  // Testzweck: Ohne Diagnosefreigabe werden weder BPMN noch technische Subscriptions
  // angefordert, auch nicht schon während des initialen Ladens.
  it('fragt für die Übersicht keine technischen Ressourcen an', () => {
    render(<InstanceDetailPage instanceId="instance-1" />);
    expect(mocks.xml).toHaveBeenCalledWith(undefined);
    expect(mocks.subscriptions).toHaveBeenCalledWith(undefined);
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
});
