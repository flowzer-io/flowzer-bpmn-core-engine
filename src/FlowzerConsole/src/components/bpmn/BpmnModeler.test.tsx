import { act, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { BpmnModeler } from './BpmnModeler';

const harness = vi.hoisted(() => ({
  models: [] as Array<{ importXML: ReturnType<typeof vi.fn>; destroy: ReturnType<typeof vi.fn> }>,
}));
vi.mock('./properties/BpmnProperties', () => ({ BpmnProperties: () => null }));
vi.mock('./bpmnEditor', () => ({ createBpmnEditor: () => ({ listFormOwners: () => [] }) }));
vi.mock('bpmn-js/lib/Modeler', () => ({
  default: class {
    importXML = vi.fn().mockResolvedValue({ warnings: [] });
    destroy = vi.fn();
    constructor() { harness.models.push(this); }
    on() { /* Ereignisse sind für den Lifecycle-Test nicht erforderlich. */ }
    get() {
      return { zoom: () => 1, viewbox: () => ({ outer: { width: 600, height: 400 } }),
        remove: () => {}, add: () => {}, addMarker: () => {}, removeMarker: () => {} };
    }
  },
}));

afterEach(() => { harness.models.length = 0; vi.unstubAllGlobals(); });

describe('BpmnModeler lifecycle', () => {
  // Testzweck: Nach einem Rollenwechsel wird ein neuer, passend berechtigter Modeler
  // aufgebaut und das unveränderte XML erneut geladen, auch bei bereits warmen Imports.
  it('importiert das Diagramm nach jedem Wechsel des Bearbeitungsrechts erneut', async () => {
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
    const xml = '<definitions />';
    const { rerender } = render(<BpmnModeler definitionId="workflow" xml={xml} />);
    await waitFor(() => expect(harness.models[0]?.importXML).toHaveBeenCalledWith(xml));

    await act(async () => { rerender(<BpmnModeler definitionId="workflow" xml={xml} readOnly />); });
    await waitFor(() => expect(harness.models[1]?.importXML).toHaveBeenCalledWith(xml));
    expect(harness.models[0]?.destroy).toHaveBeenCalledOnce();

    await act(async () => { rerender(<BpmnModeler definitionId="workflow" xml={xml} />); });
    await waitFor(() => expect(harness.models[2]?.importXML).toHaveBeenCalledWith(xml));
    expect(harness.models[1]?.destroy).toHaveBeenCalledOnce();
  });
});
