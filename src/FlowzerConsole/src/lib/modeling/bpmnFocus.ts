export interface BpmnFocusServices {
  readonly registry: { get: (id: string) => unknown };
  readonly selection: { select: (element: unknown) => void };
  readonly canvas: { scrollToElement?: (element: unknown) => void };
}

/** Kapselt den kleinen bpmn-js-Adapter für Diagnose-Sprungziele. */
export function focusBpmnElement(elementId: string, services: BpmnFocusServices): boolean {
  if (!elementId.trim()) return false;
  const element = services.registry.get(elementId);
  if (!element) return false;

  services.selection.select(element);
  services.canvas.scrollToElement?.(element);
  return true;
}
