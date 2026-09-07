/** Rechteck in Diagrammkoordinaten. */
export interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Der Ausschnitt der Zeichenfläche, wie diagram-js ihn führt. */
export interface FitCanvas {
  zoom: (mode: string | number, center?: unknown) => void;
  viewbox: (box?: Box) => { inner: Box; outer: { width: number; height: number } };
}

/**
 * Passt das Diagramm mittig und mit Rand in die Zeichenfläche ein.
 *
 * `canvas.zoom('fit-viewport')` allein legt das Diagramm ohne Zentrierung an die
 * obere linke Ecke und lässt es an den Kanten kleben — auf den Vorschaubildern der
 * Workflow-Karten fällt beides sofort auf. Deshalb wird der Ausschnitt selbst
 * gesetzt: Er umfasst das Diagramm zuzüglich `padding` und teilt den übrigen Platz
 * gleichmäßig auf beide Seiten.
 */
export function fitViewport(canvas: FitCanvas, padding: number): void {
  const { inner, outer } = canvas.viewbox();

  // Ohne Maße — etwa solange der Container noch nicht gemessen wurde — bleibt nur
  // das Standardverhalten von diagram-js.
  if (!inner.width || !inner.height || !outer.width || !outer.height) {
    canvas.zoom('fit-viewport', 'auto');
    return;
  }

  // Wie beim Standardverhalten wird nur verkleinert: Ein kleines Diagramm auf
  // Kartenbreite aufzublasen ergäbe klobige Formen statt einer Vorschau.
  const available = {
    width: Math.max(outer.width - padding * 2, 1),
    height: Math.max(outer.height - padding * 2, 1),
  };
  const scale = Math.min(1, available.width / inner.width, available.height / inner.height);

  const width = outer.width / scale;
  const height = outer.height / scale;

  canvas.viewbox({
    x: inner.x - (width - inner.width) / 2,
    y: inner.y - (height - inner.height) / 2,
    width,
    height,
  });
}
