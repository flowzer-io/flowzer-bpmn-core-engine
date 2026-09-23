import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';

// dmn-js bringt sein eigenes diagram-js-Stylesheet mit — bewusst das aus `dmn-js`, nicht das
// von bpmn-js: Beide heissen gleich, die Fassungen koennen aber auseinanderlaufen.
import 'dmn-js/dist/assets/diagram-js.css';
import 'dmn-js/dist/assets/dmn-font/css/dmn-embedded.css';
import 'dmn-js/dist/assets/dmn-js-shared.css';
import 'dmn-js/dist/assets/dmn-js-drd.css';
import 'dmn-js/dist/assets/dmn-js-decision-table.css';
import 'dmn-js/dist/assets/dmn-js-decision-table-controls.css';
import 'dmn-js/dist/assets/dmn-js-literal-expression.css';
// Eine hochgeladene DMN-Datei darf ein Business Knowledge Model enthalten; dmn-js oeffnet es
// im Boxed-Expression-Editor. Ohne dessen Stylesheets waere diese Ansicht unlesbar.
import 'dmn-js/dist/assets/dmn-js-boxed-expression.css';
import 'dmn-js/dist/assets/dmn-js-boxed-expression-controls.css';

import '@/components/bpmn/bpmn.css';
import './dmn.css';

import { InlineSpinner } from '@/components/ui/States';
import { cn } from '@/lib/cn';
import { germanDmnTranslateModule } from '@/lib/decisions/dmnTranslate';

export interface DmnEditorHandle {
  /** Liefert den aktuellen Stand als formatiertes DMN-XML. */
  getXml: () => Promise<string>;
}

interface DmnEditorProps {
  xml: string | undefined;
  /** Meldet die erste echte Änderung im Editor — für „ungespeichert". */
  onChange?: () => void;
  /**
   * Ohne Modelliererrolle wird der Betrachter geladen statt des Editors. Ein gesperrter
   * Editor waere eine Oberflaeche, die Eingaben annimmt und beim Speichern ablehnt.
   */
  readOnly?: boolean;
  className?: string;
}

/** Die Ansichten, zwischen denen dmn-js umschaltet. */
interface DmnView {
  id: string;
  type: 'drd' | 'decisionTable' | 'literalExpression' | 'boxedExpression';
  element: { id?: string; name?: string };
}

interface DmnViewOptions {
  additionalModules: unknown[];
}

interface DmnManagerOptions {
  container: HTMLElement;
  drd: DmnViewOptions;
  decisionTable: DmnViewOptions;
  literalExpression: DmnViewOptions;
  boxedExpression: DmnViewOptions;
}

interface DmnViewerLike {
  on: (event: string, callback: (event?: unknown) => void) => void;
}

interface DmnManagerLike {
  importXML: (xml: string) => Promise<{ warnings: unknown[] }>;
  saveXML: (options: { format: boolean }) => Promise<{ xml?: string }>;
  on: (event: string, callback: (event: never) => void) => void;
  getViews: () => DmnView[];
  getActiveView: () => DmnView | undefined;
  open: (view: DmnView) => void;
  destroy: () => void;
}

/**
 * Der DMN-Editor der Konsole: DRD-Übersicht, Entscheidungstabelle und Literal-Expression.
 *
 * dmn-js ist kein React-Baustein — es schreibt in einen eigenen Container und hält seinen
 * Zustand dort. Deshalb dasselbe Muster wie beim BPMN-Modeler: eine Instanz je Montage,
 * Import über einen Effekt, Auslesen nur über den Handle.
 *
 * Die Ansichtsumschaltung ist eigenes Markup statt der mitgelieferten Leiste: Die Konsole
 * beschriftet auf Deutsch und benutzt ihre eigenen Flächen.
 */
export const DmnEditor = forwardRef<DmnEditorHandle, DmnEditorProps>(function DmnEditor(
  { xml, onChange, readOnly = false, className },
  ref,
) {
  const containerRef = useRef<HTMLDivElement>(null);
  const managerRef = useRef<DmnManagerLike | null>(null);
  const onChangeRef = useRef(onChange);
  const importingRef = useRef(false);
  const [views, setViews] = useState<DmnView[]>([]);
  const [activeViewId, setActiveViewId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  onChangeRef.current = onChange;

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    let disposed = false;
    let manager: DmnManagerLike | null = null;

    // dmn-js ist gross und wird nur auf dieser Seite gebraucht; der Import bleibt deshalb
    // dynamisch, genau wie die Aufteilung der Bündel es fuer bpmn-js vorsieht.
    void (async () => {
      const module = readOnly
        ? await import('dmn-js/lib/Viewer')
        : await import('dmn-js/lib/Modeler');
      if (disposed) return;

      const Constructor = module.default as new (options: DmnManagerOptions) => DmnManagerLike;
      // Die Uebersetzung muss je Ansicht mitgegeben werden: dmn-js ueberschreibt die
      // `additionalModules` aus `common` mit denen der einzelnen Ansicht.
      const viewOptions = { additionalModules: [germanDmnTranslateModule] };
      manager = new Constructor({
        container,
        drd: viewOptions,
        decisionTable: viewOptions,
        literalExpression: viewOptions,
        boxedExpression: viewOptions,
      });
      managerRef.current = manager;

      manager.on('views.changed', () => {
        if (disposed || !manager) return;
        setViews(manager.getViews());
        setActiveViewId(manager.getActiveView()?.id ?? null);
      });

      // Jede Ansicht hat ihren eigenen Befehlsstapel. Gezaehlt wird nur, was dort als Befehl
      // landet: `elements.changed` feuerte auch beim ersten Oeffnen einer Ansicht, und schon
      // der Wechsel in die Tabelle meldete dann "ungespeicherte Aenderungen".
      manager.on('viewer.created', ({ viewer }: { viewer: DmnViewerLike }) => {
        viewer.on('commandStack.changed', () => {
          if (importingRef.current) return;
          onChangeRef.current?.();
        });
      });

      setReady(true);
    })();

    return () => {
      disposed = true;
      setReady(false);
      managerRef.current = null;
      manager?.destroy();
    };
  }, [readOnly]);

  useEffect(() => {
    const manager = managerRef.current;
    if (!manager || !ready || xml === undefined) return;

    let cancelled = false;
    importingRef.current = true;
    manager
      .importXML(xml)
      .then(() => {
        if (cancelled) return;
        setError(null);
      })
      .catch((cause: unknown) => {
        if (cancelled) return;
        setError(cause instanceof Error ? cause.message : 'Die DMN-Datei konnte nicht geöffnet werden.');
      })
      .finally(() => {
        importingRef.current = false;
      });

    return () => {
      cancelled = true;
    };
  }, [ready, xml]);

  useImperativeHandle(ref, () => ({
    async getXml() {
      const manager = managerRef.current;
      if (!manager) throw new Error('Der DMN-Editor ist nicht bereit.');
      const result = await manager.saveXML({ format: true });
      if (!result.xml) throw new Error('Der DMN-Editor lieferte kein XML.');
      return result.xml;
    },
  }), []);

  return (
    <div className={cn('flex min-h-0 flex-1 flex-col', className)}>
      {views.length > 1 && (
        <div className="border-border bg-surface-2 flex flex-wrap items-center gap-1.5 border-b px-3 py-2">
          {views.map((view) => (
            <button
              key={view.id}
              type="button"
              onClick={() => managerRef.current?.open(view)}
              className={cn(
                'cursor-pointer rounded-[var(--r-sm)] border px-2.5 py-1 text-xs font-semibold',
                view.id === activeViewId
                  ? 'border-accent text-accent bg-surface'
                  : 'border-border text-muted hover:text-text bg-transparent',
              )}
            >
              {viewLabel(view)}
            </button>
          ))}
        </div>
      )}

      {error && (
        <div className="border-warn text-warn m-3 rounded-[var(--r)] border px-4 py-3 text-sm">{error}</div>
      )}

      {!ready && !error && (
        <div className="p-4">
          <InlineSpinner label="Entscheidungseditor wird geladen …" />
        </div>
      )}

      {/*
        dmn-js legt seine Flaeche mit `height: 100%` an. Prozentwerte loesen nur gegen eine
        feste Hoehe auf, nicht gegen eine Mindesthoehe - ohne den absolut positionierten
        Rahmen fiel die Zeichenflaeche auf die SVG-Standardhoehe von 150 px zurueck und die
        Uebersicht war abgeschnitten.
      */}
      <div className="relative min-h-[560px] flex-1">
        <div
          ref={containerRef}
          data-testid="dmn-container"
          className="dmn-surface bpmn-surface absolute inset-0 overflow-auto"
        />
      </div>
    </div>
  );
});

/** Beschriftung einer Ansicht: der Name der Entscheidung, sonst die Art der Ansicht. */
function viewLabel(view: DmnView): string {
  if (view.type === 'drd') return 'Übersicht';
  const name = view.element?.name ?? view.element?.id ?? '';
  if (name.length > 0) return name;
  return view.type === 'literalExpression' ? 'Ausdruck' : 'Entscheidungstabelle';
}
