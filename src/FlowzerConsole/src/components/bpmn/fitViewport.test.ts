import { describe, expect, it } from 'vitest';

import { type Box, type FitCanvas, fitViewport } from './fitViewport';

/** Zeichenfläche, die nur den gesetzten Ausschnitt festhält. */
function canvasWith(inner: Box, outer: { width: number; height: number }) {
  const calls: { viewbox: Box[]; zoom: unknown[] } = { viewbox: [], zoom: [] };

  const canvas: FitCanvas = {
    viewbox: (box) => {
      if (box) calls.viewbox.push(box);
      return { inner, outer };
    },
    zoom: (mode) => calls.zoom.push(mode),
  };

  return { canvas, calls };
}

describe('fitViewport', () => {
  it('zentriert ein kleines Diagramm ohne es zu vergrößern', () => {
    const { canvas, calls } = canvasWith({ x: 100, y: 50, width: 200, height: 100 }, { width: 400, height: 300 });

    fitViewport(canvas, 16);

    // Kein Hochskalieren: Der Ausschnitt bleibt so groß wie die Zeichenfläche.
    expect(calls.viewbox).toEqual([{ x: 0, y: -50, width: 400, height: 300 }]);
    expect(calls.zoom).toHaveLength(0);
  });

  it('verkleinert ein großes Diagramm und lässt den Rand frei', () => {
    const { canvas, calls } = canvasWith({ x: 0, y: 0, width: 800, height: 200 }, { width: 400, height: 300 });

    fitViewport(canvas, 16);

    // Breite entscheidet: 368 nutzbare Pixel für 800 Einheiten.
    const scale = 368 / 800;
    expect(calls.viewbox).toHaveLength(1);
    const box = calls.viewbox[0]!;
    expect(box.width).toBeCloseTo(400 / scale);
    expect(box.height).toBeCloseTo(300 / scale);
    // Beidseitig gleich viel Luft, in Diagrammeinheiten gerechnet.
    expect(-box.x).toBeCloseTo(16 / scale);
    expect((box.height - 200) / 2).toBeCloseTo(-box.y);
  });

  it('fällt ohne gemessene Größe auf das Standardverhalten zurück', () => {
    const { canvas, calls } = canvasWith({ x: 0, y: 0, width: 0, height: 0 }, { width: 0, height: 0 });

    fitViewport(canvas, 16);

    expect(calls.zoom).toEqual(['fit-viewport']);
    expect(calls.viewbox).toHaveLength(0);
  });
});
