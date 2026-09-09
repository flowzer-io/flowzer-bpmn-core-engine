import { describe, expect, it } from 'vitest';

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
