import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { RuntimeDiagram } from './RuntimeDiagram';

vi.mock('@/components/bpmn/BpmnViewer', () => ({
  BpmnViewer: ({ ariaLabel }: { ariaLabel: string }) => <div role="img" aria-label={ariaLabel} />,
}));

describe('RuntimeDiagram', () => {
  // Testzweck: Der visuelle BPMN-Viewer besitzt eine tastaturbedienbare semantische
  // Alternative mit Statussymbol und Klartext; Farbe ist nie das einzige Signal.
  it('zeigt Legende und auswählbare Laufzeitknoten zugänglich an', async () => {
    const user = userEvent.setup();
    render(<RuntimeDiagram runtime={{
      instanceId: 'instance-1', definitionId: 'definition-1', processId: 'Process_1', state: 2,
      snapshotAtUtc: '2026-09-09T10:00:00Z', diagramXml: '<definitions />', events: [],
      nodes: [
        { flowNodeId: 'Review', status: 0, tokenCount: 2 },
        { flowNodeId: 'Archive', status: 2, tokenCount: 1 },
      ],
    }} />);

    expect(screen.getByRole('img', { name: 'BPMN-Laufzeitdiagramm' })).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Legende der Laufzeitzustände' }))
      .toHaveTextContent('AktivAbgeschlossenAbgebrochenGestört');
    const review = screen.getByRole('button', { name: /Review Aktiv ×2/ });
    const archive = screen.getByRole('button', { name: /Archive Abgebrochen/ });
    expect(review).toHaveAttribute('aria-current', 'step');

    await user.click(archive);

    expect(archive).toHaveAttribute('aria-current', 'step');
    expect(review).not.toHaveAttribute('aria-current');
  });
});
