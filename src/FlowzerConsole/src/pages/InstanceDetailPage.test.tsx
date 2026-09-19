import { render, screen, within, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { InstanceDetailPage } from './InstanceDetailPage';

const mocks = vi.hoisted(() => ({
  instance: vi.fn(), runtime: vi.fn(), history: vi.fn(), subscriptions: vi.fn(), navigate: vi.fn(),
  cancel: vi.fn(), migrationPreview: vi.fn(), migrate: vi.fn(), children: vi.fn(), remove: vi.fn(),
  incidents: vi.fn(), retryJob: vi.fn(), can: vi.fn(),
}));
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => mocks.navigate }));
vi.mock('@flowzer/react', () => ({
  useInstanceHistory: mocks.history,
  useInstanceRuntimeDiagram: mocks.runtime,
}));
vi.mock('@/stores/breadcrumbs', () => ({ useBreadcrumbs: vi.fn() }));
vi.mock('@/stores/session', () => ({ useCan: () => mocks.can }));
vi.mock('@/lib/api/queries', () => ({
  useInstance: mocks.instance, useInstanceSubscriptions: mocks.subscriptions,
  useInstanceChildren: mocks.children,
  useCancelInstance: () => ({ mutate: mocks.cancel, isPending: false }),
  useDeleteInstance: () => ({ mutate: mocks.remove, isPending: false }),
  useInstanceMigrationPreview: mocks.migrationPreview,
  useIncidents: mocks.incidents,
  useRetryJob: () => ({ mutate: mocks.retryJob, isPending: false }),
  useMigrateInstances: () => ({
    mutate: mocks.migrate, isPending: false, data: undefined, error: null, reset: vi.fn(),
  }),
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
  mocks.children.mockReturnValue({ data: undefined, isPending: false });
  mocks.migrationPreview.mockReturnValue({
    data: undefined, isPending: true, error: null, refetch: vi.fn(),
  });
  mocks.incidents.mockReturnValue({ data: [], isPending: false, error: null, refetch: vi.fn() });
  mocks.can.mockReturnValue(false);
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

describe('Abbruch und Version in der Betriebsansicht', () => {
  const inspectable = {
    ...overview, canInspect: true, definitionVersion: { major: 2, minor: 1 },
  };

  beforeEach(() => {
    mocks.runtime.mockReturnValue({
      data: {
        instanceId: 'instance-1', definitionId: 'definition-1', processId: 'Process_1', state: 2,
        snapshotAtUtc: '2026-09-09T10:00:00Z', diagramXml: '<definitions />', events: [], nodes: [],
      },
      isPending: false,
    });
  });

  // Testzweck: Wer abbricht oder später migriert, muss sehen, auf welcher Workflow-Version
  // die Instanz läuft — im Kopf der Instanz, nicht erst in einer Diagnoseliste.
  it('zeigt die Workflow-Version der Instanz', () => {
    mocks.instance.mockReturnValue({ data: inspectable, isPending: false });
    render(<InstanceDetailPage instanceId="instance-1" />);
    expect(screen.getByText('v2.1')).toBeInTheDocument();
  });

  // Testzweck: Ein Abbruch beendet fremde Arbeit und lässt sich nicht zurücknehmen. Er
  // geschieht deshalb nie mit einem einzelnen Klick, sondern erst nach der Rückfrage.
  it('bricht die Instanz erst nach ausdrücklicher Bestätigung ab', async () => {
    mocks.instance.mockReturnValue({ data: inspectable, isPending: false });
    const user = userEvent.setup();
    render(<InstanceDetailPage instanceId="instance-1" />);

    await user.click(screen.getByRole('button', { name: 'Instanz abbrechen' }));
    expect(mocks.cancel).not.toHaveBeenCalled();

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Urlaubsantrag');
    // „Abbrechen" wäre hier doppeldeutig: Dialog schließen oder Instanz abbrechen?
    expect(within(dialog).getByRole('button', { name: 'Instanz weiterlaufen lassen' })).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Abbrechen' })).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Instanz abbrechen' }));

    expect(mocks.cancel).toHaveBeenCalledWith('instance-1', expect.anything());
  });

  // Testzweck: Beendete Instanzen lassen sich nicht abbrechen (die API antwortet mit 409);
  // die Oberfläche bietet es dann gar nicht erst an.
  it('bietet den Abbruch für beendete Instanzen nicht an', () => {
    mocks.instance.mockReturnValue({
      data: { ...inspectable, state: 'Completed', finishedAt: '2026-09-09T10:00:00Z' },
      isPending: false,
    });
    render(<InstanceDetailPage instanceId="instance-1" />);
    expect(screen.queryByRole('button', { name: 'Instanz abbrechen' })).not.toBeInTheDocument();
  });

  // Testzweck: Eine abgebrochene Instanz ist zu Ende. Ein zweiter Abbruch liefe ins Leere,
  // und migrieren ließe sich nichts mehr — beides darf gar nicht erst angeboten werden.
  it('bietet für eine abgebrochene Instanz weder Abbruch noch Migration an', () => {
    mocks.instance.mockReturnValue({
      data: { ...inspectable, state: 'Terminated', finishedAt: '2026-09-09T10:00:00Z' },
      isPending: false,
    });
    // Ohne Laufzeitdiagramm bleibt „Abgebrochen“ eindeutig der Statuschip und nicht
    // die Legende des Diagramms, die dasselbe Wort für einzelne Knoten führt.
    mocks.runtime.mockReturnValue({ data: undefined, isPending: false });
    render(<InstanceDetailPage instanceId="instance-1" />);

    expect(screen.getByText('Abgebrochen')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Instanz abbrechen' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Migrieren …' })).not.toBeInTheDocument();
  });

  // Testzweck: Ohne Betriebsrecht lehnt die API den Abbruch ab; die datensparsame
  // Übersicht zeigt die Schaltfläche deshalb nicht.
  it('bietet den Abbruch ohne Betriebsrecht nicht an', () => {
    render(<InstanceDetailPage instanceId="instance-1" />);
    expect(screen.queryByRole('button', { name: 'Instanz abbrechen' })).not.toBeInTheDocument();
  });

  // Testzweck: Wer eine einzelne laufende Instanz vor sich hat, soll sie von dort aus
  // migrieren können — ohne den Umweg über die Liste und die Mehrfachauswahl.
  it('bietet den Migrationsassistenten für eine laufende Instanz an', async () => {
    mocks.instance.mockReturnValue({ data: inspectable, isPending: false });
    const user = userEvent.setup();
    render(<InstanceDetailPage instanceId="instance-1" />);

    // Die Prüfung startet erst mit dem Dialog, nicht schon beim Betrachten der Instanz.
    expect(mocks.migrationPreview).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Migrieren …' }));

    // Zweites Argument: die Zuordnung von Hand — beim Öffnen noch leer.
    expect(mocks.migrationPreview).toHaveBeenCalledWith(['instance-1'], {});
    expect(screen.getByRole('dialog')).toHaveTextContent('Instanzen migrieren');
  });

  // Testzweck: Eine beendete Instanz hat keinen Token mehr, den man umhängen könnte;
  // ohne Betriebsrecht lehnt die API ohnehin ab. Beides bietet die Oberfläche nicht an.
  it('bietet die Migration für beendete Instanzen und ohne Betriebsrecht nicht an', () => {
    mocks.instance.mockReturnValue({
      data: { ...inspectable, state: 'Completed', finishedAt: '2026-09-09T10:00:00Z' },
      isPending: false,
    });
    const { unmount } = render(<InstanceDetailPage instanceId="instance-1" />);
    expect(screen.queryByRole('button', { name: 'Migrieren …' })).not.toBeInTheDocument();
    unmount();

    mocks.instance.mockReturnValue({ data: overview, isPending: false });
    render(<InstanceDetailPage instanceId="instance-1" />);
    expect(screen.queryByRole('button', { name: 'Migrieren …' })).not.toBeInTheDocument();
  });
});

describe('Störungen an der Instanz', () => {
  const inspectable = { ...overview, canInspect: true };
  const stalledJob = {
    kind: 'jobExhausted' as const,
    instanceId: 'instance-1',
    metaDefinitionId: 'urlaub',
    definitionId: 'definition-1',
    definitionName: 'Urlaubsantrag',
    flowNodeId: 'ServiceTask_1',
    flowNodeName: 'Zahlung auslösen',
    jobId: 'job-1',
    jobType: 'zahlung',
    message: 'IBAN ungültig',
    since: '2026-09-19T10:00:00Z',
    manualRetries: 0,
    variables: { iban: 'DE00' },
  };

  beforeEach(() => {
    mocks.instance.mockReturnValue({ data: inspectable, isPending: false });
    mocks.runtime.mockReturnValue({
      data: {
        instanceId: 'instance-1', definitionId: 'definition-1', processId: 'Process_1', state: 2,
        snapshotAtUtc: '2026-09-09T10:00:00Z', diagramXml: '<definitions />', events: [], nodes: [],
      },
      isPending: false,
    });
  });

  // Testzweck: „Gescheitert“ allein sagt nicht, woran. Die Begründung der Engine steht deshalb
  // im Kopf der Instanz und nicht nur im Betriebsbild.
  it('zeigt die Begründung einer gescheiterten Instanz', () => {
    mocks.instance.mockReturnValue({
      data: {
        ...inspectable,
        state: 'Failed',
        finishedAt: '2026-09-19T10:00:00Z',
        failureReason: "Unhandled BPMN error 'BONITAET' at 'ServiceTask_1'.",
      },
      isPending: false,
    });
    render(<InstanceDetailPage instanceId="instance-1" />);

    expect(screen.getByText(/Unhandled BPMN error 'BONITAET'/)).toBeInTheDocument();
  });

  // Testzweck: Wer die Instanz vor sich hat, soll ihren liegen gebliebenen Auftrag von dort aus
  // freigeben können — ohne den Umweg über die Betriebsseite.
  it('bietet die Freigabe eines liegen gebliebenen Auftrags dieser Instanz an', async () => {
    mocks.can.mockReturnValue(true);
    mocks.incidents.mockReturnValue({
      data: [stalledJob], isPending: false, error: null, refetch: vi.fn(),
    });
    const user = userEvent.setup();
    render(<InstanceDetailPage instanceId="instance-1" />);

    await user.click(screen.getByRole('button', { name: 'Erneut freigeben' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Zahlung auslösen');
    // Das Korrekturfeld ist mit den aktuellen Eingaben vorbelegt; mitgeschickt wird nur, was
    // sich davon unterscheidet — sonst stünde jedes Feld in der Freigabespur als korrigiert.
    fireEvent.change(within(dialog).getByLabelText(/Eingaben korrigieren/), {
      target: { value: '{ "iban": "DE02", "amount": 5 }' },
    });
    await user.click(within(dialog).getByRole('button', { name: 'Erneut freigeben' }));

    expect(mocks.retryJob).toHaveBeenCalledWith(
      { jobId: 'job-1', retries: 1, variables: { iban: 'DE02', amount: 5 } },
      expect.anything(),
    );
  });

  // Testzweck: Die Störung einer fremden Instanz gehört nicht in diese Instanz. Ohne den Filter
  // böte die Detailseite eine Freigabe an, die einen ganz anderen Vorgang beträfe.
  it('bietet die Freigabe für den liegen gebliebenen Auftrag einer anderen Instanz nicht an', () => {
    mocks.can.mockReturnValue(true);
    mocks.incidents.mockReturnValue({
      data: [{ ...stalledJob, instanceId: 'instance-2' }],
      isPending: false, error: null, refetch: vi.fn(),
    });
    render(<InstanceDetailPage instanceId="instance-1" />);

    expect(screen.queryByRole('button', { name: 'Erneut freigeben' })).not.toBeInTheDocument();
  });

  // Testzweck: Ohne Betriebsrecht lehnt die API die Störungsliste ab; die Oberfläche fragt sie
  // dann gar nicht erst an, statt im Hintergrund an einer 403 zu scheitern.
  it('fragt die Störungsliste ohne Betriebsrecht nicht an', () => {
    render(<InstanceDetailPage instanceId="instance-1" />);
    expect(mocks.incidents).toHaveBeenCalledWith({ enabled: false });
});

describe('Eltern- und Kindbezug einer Call Activity', () => {
  const inspectable = { ...overview, canInspect: true };

  // Testzweck: Eine Kindinstanz ist ohne ihren Aufrufer nicht zu verstehen — der Vorgang
  // beginnt woanders. Der Hinweis steht deshalb im Kopf und führt dorthin.
  it('nennt die aufrufende Instanz und öffnet sie', async () => {
    mocks.instance.mockReturnValue({
      data: { ...inspectable, parentInstanceId: 'parent-1', parentTokenId: 'token-9' },
      isPending: false,
    });

    const user = userEvent.setup();
    render(<InstanceDetailPage instanceId="instance-1" />);

    const link = screen.getByRole('button', { name: /Aufgerufen von/ });
    await user.click(link);

    expect(mocks.navigate).toHaveBeenCalledWith({ to: '/instances/parent-1' });
  });

  // Testzweck: Die Elterninstanz wartet an der Call Activity auf fremde Vorgänge. Ohne deren
  // Namen, Version und Zustand bliebe der wartende Schritt unerklärt.
  it('listet die aufgerufenen Vorgänge mit Zustand und Sprung in die Kindinstanz', async () => {
    // „Läuft" statt „Wartet": Sonst trüge der Statuschip der Elterninstanz denselben Text
    // wie der der Kindinstanz, und der geprüfte Zustand wäre nicht mehr zuzuordnen.
    mocks.instance.mockReturnValue({ data: { ...inspectable, state: 'Running' }, isPending: false });
    mocks.children.mockReturnValue({
      data: [{
        instanceId: 'child-1', relatedDefinitionId: 'pruefung',
        relatedDefinitionName: 'Bonitätsprüfung', definitionVersion: { major: 3, minor: 2 },
        state: 'Waiting', callActivityFlowNodeId: 'CallActivity_1',
      }],
      isPending: false,
    });

    const user = userEvent.setup();
    render(<InstanceDetailPage instanceId="instance-1" />);
    await user.click(screen.getByRole('tab', { name: 'Warteobjekte' }));

    expect(mocks.children).toHaveBeenCalledWith('instance-1');
    expect(screen.getByText('Aufgerufene Vorgänge')).toBeInTheDocument();
    expect(screen.getByText('Bonitätsprüfung')).toBeInTheDocument();
    expect(screen.getByText(/v3\.2/)).toBeInTheDocument();
    expect(screen.getByText('Wartet')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Bonitätsprüfung/ }));
    expect(mocks.navigate).toHaveBeenCalledWith({ to: '/instances/child-1' });
  });

  // Testzweck: Die allermeisten Instanzen rufen nichts auf. Ein leerer Kasten „Keine
  // aufgerufenen Vorgänge“ wäre in jeder von ihnen reines Rauschen.
  it('zeigt ohne Kindinstanzen gar keinen Abschnitt', async () => {
    mocks.instance.mockReturnValue({ data: inspectable, isPending: false });
    mocks.children.mockReturnValue({ data: [], isPending: false });

    const user = userEvent.setup();
    render(<InstanceDetailPage instanceId="instance-1" />);
    await user.click(screen.getByRole('tab', { name: 'Warteobjekte' }));

    expect(screen.queryByText('Aufgerufene Vorgänge')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Aufgerufen von/ })).not.toBeInTheDocument();
  });
});
