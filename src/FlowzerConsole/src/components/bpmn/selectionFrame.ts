/** Abstand des Auswahlrahmens zur Form, in Diagrammeinheiten. */
export const SELECTION_FRAME_OFFSET = 7;

/**
 * Erstellt den Auswahlrahmen um ein BPMN-Element.
 *
 * Ein Overlay statt einer Markierungsklasse: Kontur und Füllung der Form tragen bereits den
 * Laufzeitzustand, und den Rahmen mit Abstand, den diagram-js dafür sonst zeichnet, gibt es
 * im Nur-Lese-Viewer nicht. Das Overlay skaliert mit dem Diagramm und bleibt so deckungsgleich.
 */
export function createSelectionFrame(elementWidth: number, elementHeight: number): HTMLDivElement {
  const frame = document.createElement('div');
  frame.className = 'flowzer-selection-frame';
  frame.style.width = `${elementWidth + 2 * SELECTION_FRAME_OFFSET}px`;
  frame.style.height = `${elementHeight + 2 * SELECTION_FRAME_OFFSET}px`;
  return frame;
}
