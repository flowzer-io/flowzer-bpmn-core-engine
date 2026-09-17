import { act, render, waitFor } from '@testing-library/react';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { BpmnViewer } from './BpmnViewer';
import { createSelectionFrame, SELECTION_FRAME_OFFSET } from './selectionFrame';
import { createTokenBadge } from './tokenBadge';

/**
 * Ein Ersatz-Viewer, der nur die Overlay-Buchführung von diagram-js nachbildet:
 * Der Lebenszyklus der Rahmen ist die Logik der Komponente, bpmn-js selbst nicht.
 */
const harness = vi.hoisted(() => {
  const overlays: { id: string; elementId: string; type: string }[] = [];
  const removeCalls: (string | { type: string })[] = [];
  const state = { nextId: 0, destroyed: false };

  return {
    overlays,
    removeCalls,
    state,
    reset() {
      overlays.length = 0;
      removeCalls.length = 0;
      state.nextId = 0;
      state.destroyed = false;
    },
    add(elementId: string, type: string) {
      if (state.destroyed) throw new Error('viewer destroyed');
      state.nextId += 1;
      const id = `overlay-${state.nextId}`;
      overlays.push({ id, elementId, type });
      return id;
    },
    remove(filter: string | { type: string }) {
      // Ein zerstörter Viewer wirft — genau daran soll der Abbau nicht scheitern.
      if (state.destroyed) throw new Error('viewer destroyed');
      removeCalls.push(filter);
      for (let index = overlays.length - 1; index >= 0; index -= 1) {
        const overlay = overlays[index]!;
        const hit = typeof filter === 'string' ? overlay.id === filter : overlay.type === filter.type;
        if (hit) overlays.splice(index, 1);
      }
    },
  };
});

vi.mock('zeebe-bpmn-moddle/resources/zeebe.json', () => ({ default: { name: 'Zeebe', prefix: 'zeebe' } }));
vi.mock('bpmn-js/lib/NavigatedViewer', () => ({
  default: class {
    importXML = vi.fn().mockResolvedValue({ warnings: [] });
    destroy = vi.fn();
    on() { /* Klicks sind für den Overlay-Lebenszyklus nicht erforderlich. */ }
    get(name: string) {
      if (name === 'overlays') return { add: harness.add, remove: harness.remove };
      if (name === 'elementRegistry') return { get: () => ({ width: 100, height: 80 }) };
      return {
        zoom: () => {},
        viewbox: () => ({ inner: { x: 0, y: 0, width: 0, height: 0 }, outer: { width: 600, height: 400 } }),
        addMarker: () => {},
        removeMarker: () => {},
      };
    }
  },
}));

// Vor dem Test zurücksetzen, nicht danach: Das Aufräumen von Testing Library baut die
// vorige Ansicht erst ab, nachdem die eigenen `afterEach`-Haken gelaufen sind — deren
// Overlay-Aufrufe landeten sonst in der Buchführung des nächsten Tests.
beforeEach(() => harness.reset());

describe('BpmnViewer Token-Badge', () => {
  // Testzweck: Mehrere aktive Ausführungen an demselben BPMN-Element werden als
  // genau ein Kreis mit Anzahl dargestellt und nicht als übereinanderliegende Punkte.
  it('zeigt die Anzahl nur bei mehreren Ausführungen im gemeinsamen Kreis', () => {
    const single = createTokenBadge(1);
    const merged = createTokenBadge(3);

    expect(single).toHaveClass('flowzer-token');
    expect(single).toHaveTextContent('');
    expect(single).toHaveAttribute('title', '1 aktive Ausführung');
    expect(merged).toHaveClass('flowzer-token');
    expect(merged).toHaveTextContent('3');
    expect(merged).toHaveAttribute('title', '3 aktive Ausführungen');
  });
});

describe('BpmnViewer Auswahlrahmen', () => {
  // Testzweck: Der Rahmen umschließt das gewählte Element rundum mit Abstand, statt
  // dessen Kontur zu überdecken — die trägt bereits den Laufzeitzustand.
  it('ist auf allen Seiten um den Abstand größer als das Element', () => {
    const frame = createSelectionFrame(100, 80);

    expect(frame).toHaveClass('flowzer-selection-frame');
    expect(frame.style.width).toBe(`${100 + 2 * SELECTION_FRAME_OFFSET}px`);
    expect(frame.style.height).toBe(`${80 + 2 * SELECTION_FRAME_OFFSET}px`);
  });
});

describe('BpmnViewer Overlay-Lebenszyklus', () => {
  const XML = '<definitions />';

  async function renderViewer(element: ReactElement) {
    const view = render(element);
    await waitFor(() =>
      expect(harness.overlays.some((overlay) => overlay.type === 'flowzer-selection')).toBe(true),
    );
    return view;
  }

  function selectionFrameId(): string {
    return harness.overlays.find((overlay) => overlay.type === 'flowzer-selection')!.id;
  }

  // Testzweck: Wer im Laufzeitdiagramm einen anderen Schritt wählt, darf nicht mit zwei
  // Rahmen dastehen — der alte muss gezielt über seine Id verschwinden.
  it('tauscht den Auswahlrahmen beim Wechsel des gewählten Schritts', async () => {
    const { rerender } = await renderViewer(<BpmnViewer xml={XML} selectedElementId="Review" />);
    const first = selectionFrameId();

    await act(async () => {
      rerender(<BpmnViewer xml={XML} selectedElementId="Freigabe" />);
    });

    expect(harness.removeCalls).toContainEqual(first);
    const frames = harness.overlays.filter((overlay) => overlay.type === 'flowzer-selection');
    expect(frames).toHaveLength(1);
    expect(frames[0]!.elementId).toBe('Freigabe');
  });

  // Testzweck: Die Token-Zähler werden im Zehn-Sekunden-Takt neu gesetzt. Räumten sie
  // dabei alle Overlays ab, flackerte der Auswahlrahmen bei jedem Abruf weg.
  it('erneuert nur die Token-Zähler und lässt den Auswahlrahmen stehen', async () => {
    const { rerender } = await renderViewer(
      <BpmnViewer xml={XML} selectedElementId="Review" tokenCounts={{ Review: 1 }} />,
    );
    const frame = selectionFrameId();

    await act(async () => {
      rerender(<BpmnViewer xml={XML} selectedElementId="Review" tokenCounts={{ Review: 2 }} />);
    });

    expect(harness.removeCalls).toContainEqual({ type: 'flowzer-token' });
    expect(harness.removeCalls).not.toContainEqual(frame);
    expect(harness.overlays.filter((overlay) => overlay.type === 'flowzer-token')).toHaveLength(1);
    expect(harness.overlays.some((overlay) => overlay.id === frame)).toBe(true);
  });

  // Testzweck: Beim Verlassen der Seite ist der Viewer oft schon zerstört; das Aufräumen
  // des Rahmens darf den Abbau dann nicht mit einer Ausnahme abbrechen.
  it('bricht beim Abbau nicht ab, wenn der Viewer schon zerstört ist', async () => {
    const { unmount } = await renderViewer(<BpmnViewer xml={XML} selectedElementId="Review" />);

    harness.state.destroyed = true;
    expect(() => unmount()).not.toThrow();
  });
});
