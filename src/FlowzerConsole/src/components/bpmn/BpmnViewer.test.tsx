import { describe, expect, it } from 'vitest';

import { createSelectionFrame, SELECTION_FRAME_OFFSET } from './selectionFrame';
import { createTokenBadge } from './tokenBadge';

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
