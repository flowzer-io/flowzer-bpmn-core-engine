import { createRef } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { DmnEditor, type DmnEditorHandle } from './DmnEditor';

/**
 * dmn-js laesst sich in jsdom nicht montieren: diagram-js misst seine Ebenen ueber
 * `SVGGraphicsElement.getBBox()` und liest `transform.baseVal`, und beides kennt jsdom
 * nicht. Der Rauchtest mockt deshalb das Modul — genau wie `BpmnModeler.test.tsx` es fuer
 * bpmn-js tut — und prueft, was die Konsole selbst verantwortet: montieren, importieren,
 * auslesen, abraeumen.
 */
const harness = vi.hoisted(() => ({
  instances: [] as Array<{
    container: HTMLElement;
    importXML: ReturnType<typeof vi.fn>;
    saveXML: ReturnType<typeof vi.fn>;
    destroy: ReturnType<typeof vi.fn>;
  }>,
}));

class FakeManager {
  container: HTMLElement;
  importXML = vi.fn().mockResolvedValue({ warnings: [] });
  saveXML = vi.fn().mockResolvedValue({ xml: '<definitions gespeichert="ja" />' });
  destroy = vi.fn();

  constructor({ container }: { container: HTMLElement }) {
    this.container = container;
    harness.instances.push(this);
  }

  on() { /* Für den Rauchtest ist die Ereignisverdrahtung ohne Belang. */ }
  getViews() { return []; }
  getActiveView() { return undefined; }
  open() {}
}

vi.mock('dmn-js/lib/Modeler', () => ({ default: FakeManager }));
vi.mock('dmn-js/lib/Viewer', () => ({ default: FakeManager }));

afterEach(() => {
  harness.instances.length = 0;
  vi.clearAllMocks();
});

describe('DMN-Editor', () => {
  // Testzweck: Der Editor montiert genau eine dmn-js-Instanz in seinen eigenen Container
  // und laedt das uebergebene DMN-XML hinein.
  it('montiert und importiert das DMN-XML', async () => {
    render(<DmnEditor xml="<definitions />" />);

    await waitFor(() => expect(harness.instances).toHaveLength(1));
    await waitFor(() => expect(harness.instances[0]!.importXML).toHaveBeenCalledWith('<definitions />'));
    expect(harness.instances[0]!.container).toBe(screen.getByTestId('dmn-container'));
  });

  // Testzweck: Der Handle liest den Stand aus dem Editor statt aus dem zuletzt gesetzten
  // Prop — nur so landet im Speichern, was jemand gerade in der Tabelle geaendert hat.
  it('liefert das aktuelle XML ueber den Handle', async () => {
    const ref = createRef<DmnEditorHandle>();
    render(<DmnEditor ref={ref} xml="<definitions />" />);

    await waitFor(() => expect(harness.instances).toHaveLength(1));
    await expect(ref.current!.getXml()).resolves.toBe('<definitions gespeichert="ja" />');
    expect(harness.instances[0]!.saveXML).toHaveBeenCalledWith({ format: true });
  });

  // Testzweck: Beim Abbauen wird die dmn-js-Instanz zerstoert; sonst bliebe ihr
  // Ereignis-Bus samt Container-Inhalt nach jedem Seitenwechsel stehen.
  it('raeumt die Instanz beim Abbauen ab', async () => {
    const { unmount } = render(<DmnEditor xml="<definitions />" />);
    await waitFor(() => expect(harness.instances).toHaveLength(1));

    unmount();
    expect(harness.instances[0]!.destroy).toHaveBeenCalledOnce();
  });
});
