/** Erstellt den einzigen sichtbaren Token-Marker eines BPMN-Knotens. */
export function createTokenBadge(count: number): HTMLDivElement {
  const normalizedCount = Math.max(1, Math.trunc(count));
  const badge = document.createElement('div');
  badge.className = 'flowzer-token';
  badge.title = normalizedCount === 1
    ? '1 aktive Ausführung'
    : `${normalizedCount} aktive Ausführungen`;
  if (normalizedCount > 1) badge.textContent = String(normalizedCount);
  return badge;
}
