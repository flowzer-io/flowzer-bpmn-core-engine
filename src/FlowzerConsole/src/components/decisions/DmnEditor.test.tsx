import { createRef } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { DmnEditor, type DmnEditorHandle } from './DmnEditor';
import { germanDmnTranslateModule } from '@/lib/decisions/dmnTranslate';

/**
 * dmn-js laesst sich in jsdom nicht montieren: diagram-js misst seine Ebenen ueber
 * `SVGGraphicsElement.getBBox()` und liest `transform.baseVal`, und beides kennt jsdom
 * nicht. Der Rauchtest mockt deshalb das Modul — genau wie `BpmnModeler.test.tsx` es fuer
 * bpmn-js tut — und prueft, was die Konsole selbst verantwortet: montieren, importieren,
 * auslesen, abraeumen.
 */
type Listener = (event?: unknown) => void;

const harness = vi.hoisted(() => ({
  instances: [] as Array<{
    container: HTMLElement;
    options: Record<string, unknown>;
    importXML: ReturnType<typeof vi.fn>;
    saveXML: ReturnType<typeof vi.fn>;
    destroy: ReturnType<typeof vi.fn>;
    listeners: Map<string, Listener>;
  }>,
}));

/** Eine Ansicht, wie dmn-js sie beim ersten Oeffnen erzeugt: mit eigenem Ereignis-Bus. */
class FakeViewer {
  listeners = new Map<string, Listener>();
  on(event: string, callback: Listener) { this.listeners.set(event, callback); }
  fire(event: string) { this.listeners.get(event)?.(); }
}

class FakeManager {
  container: HTMLElement;
  options: Record<string, unknown>;
  importXML = vi.fn().mockResolvedValue({ warnings: [] });
  saveXML = vi.fn().mockResolvedValue({ xml: '<definitions gespeichert="ja" />' });
  destroy = vi.fn();
  listeners = new Map<string, Listener>();

  constructor(options: { container: HTMLElement } & Record<string, unknown>) {
    this.container = options.container;
    this.options = options;
    harness.instances.push(this);
  }

  on(event: string, callback: Listener) { this.listeners.set(event, callback); }
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

  // Testzweck: Jede Ansicht bekommt die deutsche Beschriftung. dmn-js ueberschreibt die
  // Module aus `common` mit denen der Ansicht - ueber `common` allein bliebe alles englisch.
  it('gibt jeder Ansicht die deutsche Beschriftung mit', async () => {
    render(<DmnEditor xml="<definitions />" />);
    await waitFor(() => expect(harness.instances).toHaveLength(1));

    const options = harness.instances[0]!.options;
    for (const view of ['drd', 'decisionTable', 'literalExpression', 'boxedExpression']) {
      expect(options[view]).toEqual({ additionalModules: [germanDmnTranslateModule] });
    }
  });

  // Testzweck: Allein das Oeffnen einer Ansicht ist keine Bearbeitung. Vorher meldete schon
  // der Wechsel in die Tabelle "ungespeicherte Aenderungen", weil dmn-js dabei neu zeichnet.
  // Erst ein Befehl auf dem Befehlsstapel zaehlt.
  it('meldet eine Aenderung erst bei einem Bearbeitungsbefehl', async () => {
    const onChange = vi.fn();
    render(<DmnEditor xml="<definitions />" onChange={onChange} />);
    await waitFor(() => expect(harness.instances[0]!.importXML).toHaveBeenCalled());

    const viewer = new FakeViewer();
    harness.instances[0]!.listeners.get('viewer.created')?.({ viewer });

    viewer.fire('elements.changed');
    expect(onChange).not.toHaveBeenCalled();

    viewer.fire('commandStack.changed');
    expect(onChange).toHaveBeenCalledOnce();
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
