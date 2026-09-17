import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { RuntimeDiagram as RuntimeDiagramDto } from '@flowzer/sdk';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';

import { RuntimeDiagram } from './RuntimeDiagram';

vi.mock('@/components/bpmn/BpmnViewer', () => ({
  BpmnViewer: ({ ariaLabel, tokenCounts, selectedElementId, onElementClick }: {
    ariaLabel: string;
    tokenCounts: Record<string, number>;
    selectedElementId?: string;
    onElementClick?: (elementId: string) => void;
  }) => (
    <div role="img" aria-label={ariaLabel} data-token-counts={JSON.stringify(tokenCounts)}
      data-selected-element={selectedElementId ?? ''}>
      <button type="button" onClick={() => onElementClick?.('Archive')}>Archive im Diagramm</button>
    </div>
  ),
}));

const RUNTIME: RuntimeDiagramDto = {
  instanceId: 'instance-1', definitionId: 'definition-1', processId: 'Process_1', state: 2,
  snapshotAtUtc: '2026-09-09T10:00:00Z', diagramXml: '<definitions />', events: [],
  nodes: [
    { flowNodeId: 'Review', status: 0, tokenCount: 2 },
    { flowNodeId: 'Archive', status: 2, tokenCount: 1 },
  ],
};

/** Die Auswahl gehoert der Seite; der Test haelt sie wie die Seite von aussen. */
function ControlledDiagram({ onNodeSelect }: { onNodeSelect: (flowNodeId: string) => void }) {
  const [selected, setSelected] = useState('Review');
  return <RuntimeDiagram runtime={RUNTIME} selectedNodeId={selected}
    onNodeSelect={(flowNodeId) => { setSelected(flowNodeId); onNodeSelect(flowNodeId); }} />;
}

describe('RuntimeDiagram', () => {
  // Testzweck: Der visuelle BPMN-Viewer besitzt eine tastaturbedienbare semantische
  // Alternative mit Statussymbol und Klartext; Farbe ist nie das einzige Signal.
  it('zeigt Legende und auswählbare Laufzeitknoten zugänglich an', async () => {
    const user = userEvent.setup();
    const onNodeSelect = vi.fn();
    render(<ControlledDiagram onNodeSelect={onNodeSelect} />);

    expect(screen.getByRole('img', { name: 'BPMN-Laufzeitdiagramm' }))
      .toHaveAttribute('data-token-counts', '{"Review":2}');
    expect(screen.getByRole('list', { name: 'Legende der Laufzeitzustände' }))
      .toHaveTextContent('AktivAbgeschlossenAbgebrochenGestört');
    const review = screen.getByRole('button', { name: /Review Aktiv ×2/ });
    const archive = screen.getByRole('button', { name: /Archive Abgebrochen/ });
    expect(review).toHaveAttribute('aria-current', 'step');

    await user.click(archive);

    expect(onNodeSelect).toHaveBeenCalledWith('Archive');
    expect(archive).toHaveAttribute('aria-current', 'step');
    expect(review).not.toHaveAttribute('aria-current');
  });

  // Testzweck: Der gewaehlte Knoten ist nicht nur in der Knotenliste, sondern auch im
  // Schaubild markiert — egal, ob er dort oder in der Liste angeklickt wurde.
  it('markiert den gewählten Knoten auch im Diagramm', async () => {
    const user = userEvent.setup();
    render(<ControlledDiagram onNodeSelect={vi.fn()} />);
    const viewer = screen.getByRole('img', { name: 'BPMN-Laufzeitdiagramm' });

    expect(viewer).toHaveAttribute('data-selected-element', 'Review');

    await user.click(screen.getByRole('button', { name: /Archive Abgebrochen/ }));
    expect(viewer).toHaveAttribute('data-selected-element', 'Archive');

    await user.click(screen.getByRole('button', { name: /Review Aktiv/ }));
    await user.click(screen.getByRole('button', { name: 'Archive im Diagramm' }));
    expect(viewer).toHaveAttribute('data-selected-element', 'Archive');
    expect(screen.getByRole('button', { name: /Archive Abgebrochen/ })).toHaveAttribute('aria-current', 'step');
  });
});
