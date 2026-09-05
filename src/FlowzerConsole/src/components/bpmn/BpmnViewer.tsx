import { useEffect, useMemo, useRef, useState } from 'react';

import 'bpmn-js/dist/assets/diagram-js.css';
import 'bpmn-js/dist/assets/bpmn-js.css';
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn-embedded.css';
import './bpmn.css';

import { cn } from '@/lib/cn';

/** Darstellungszustand eines BPMN-Elements im Instanzverlauf. */
export type NodeMarker = 'completed' | 'active' | 'failed';

interface BpmnViewerProps {
  xml: string | undefined;
  /** Elemente, die farblich hervorgehoben werden (Flow-Node-Id → Zustand). */
  markers?: Record<string, NodeMarker>;
  /** Element-Ids, an denen ein pulsierender Token gezeichnet wird. */
  tokens?: string[];
  onElementClick?: (elementId: string) => void;
  className?: string;
  /** Interaktion (Zoom/Pan) erlauben. Für Vorschaubilder abschalten. */
  interactive?: boolean;
  /** Nach dem Import auf die Zeichenfläche einpassen. */
  fit?: boolean;
}

interface CanvasLike {
  zoom: (mode: string | number, center?: unknown) => void;
  addMarker: (elementId: string, marker: string) => void;
  removeMarker: (elementId: string, marker: string) => void;
}

interface OverlaysLike {
  add: (elementId: string, config: unknown) => string;
  clear: () => void;
}

interface ViewerLike {
  importXML: (xml: string) => Promise<{ warnings: unknown[] }>;
  get: <T>(name: string) => T;
  on: (event: string, callback: (event: { element: { id: string } }) => void) => void;
  destroy: () => void;
}

const ALL_MARKERS = ['flowzer-completed', 'flowzer-active', 'flowzer-failed'] as const;

/**
 * Nur-Lese-Ansicht eines BPMN-Diagramms mit Zustandsmarkierungen.
 *
 * Erzeugung und Import liegen bewusst in *einem* Effekt: Würde der Viewer in einem
 * eigenen Effekt entstehen und erst später importieren, könnten sich unter React
 * StrictMode (doppeltes Mounten) zwei Importe auf derselben Instanz überholen —
 * der zweite leert die Zeichenfläche, während der erste noch schreibt, und
 * bpmn-js bricht mit "rootElement required" ab.
 *
 * bpmn-js wird dynamisch geladen, damit die Bibliothek nicht im Haupt-Bundle landet.
 */
export function BpmnViewer({
  xml,
  markers,
  tokens,
  onElementClick,
  className,
  interactive = true,
  fit = true,
}: BpmnViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewerRef = useRef<ViewerLike | null>(null);
  const onElementClickRef = useRef(onElementClick);
  const [imported, setImported] = useState(false);
  const [error, setError] = useState<string | null>(null);

  onElementClickRef.current = onElementClick;

  // Stabile Schlüssel, damit ein bei jedem Render neu gebautes Objekt mit
  // gleichem Inhalt keine erneute Markierung auslöst.
  const markerKey = useMemo(() => JSON.stringify(markers ?? {}), [markers]);
  const tokenKey = useMemo(() => JSON.stringify(tokens ?? []), [tokens]);

  useEffect(() => {
    let disposed = false;
    let viewer: ViewerLike | null = null;

    async function mount() {
      const container = containerRef.current;
      if (!container || !xml) return;

      setImported(false);
      setError(null);

      try {
        const [module, { default: zeebeModdle }] = await Promise.all([
          interactive ? import('bpmn-js/lib/NavigatedViewer') : import('bpmn-js/lib/Viewer'),
          import('zeebe-bpmn-moddle/resources/zeebe.json'),
        ]);
        if (disposed) return;

        const Viewer = module.default as unknown as new (options: Record<string, unknown>) => ViewerLike;
        // Ohne die Zeebe-Erweiterung stolpert der Viewer über `zeebe:formDefinition`
        // & Co. und bricht den Import mit "unparsable content" ab.
        viewer = new Viewer({ container, moddleExtensions: { zeebe: zeebeModdle } });

        if (onElementClickRef.current) {
          viewer.on('element.click', (event) => onElementClickRef.current?.(event.element.id));
        }

        await viewer.importXML(xml);

        if (disposed) {
          viewer.destroy();
          viewer = null;
          return;
        }

        viewerRef.current = viewer;
        if (fit) viewer.get<CanvasLike>('canvas').zoom('fit-viewport');
        setImported(true);
      } catch (cause) {
        viewer?.destroy();
        viewer = null;
        if (disposed) return;
        setError(describeImportError(cause));
      }
    }

    void mount();

    return () => {
      disposed = true;
      viewer?.destroy();
      if (viewerRef.current === viewer) viewerRef.current = null;
      setImported(false);
    };
  }, [xml, interactive, fit]);

  // Markierungen und Token-Punkte werden nach dem Import gesetzt und bei
  // Änderungen aktualisiert, ohne das Diagramm neu zu importieren.
  useEffect(() => {
    const viewer = viewerRef.current;
    if (!imported || !viewer) return;

    applyMarkers(viewer, JSON.parse(markerKey) as Record<string, NodeMarker>, JSON.parse(tokenKey) as string[]);
  }, [imported, markerKey, tokenKey]);

  return (
    <div className={cn('bpmn-surface relative', className)}>
      <div ref={containerRef} className="h-full w-full" />
      {error && (
        <div className="bg-surface/90 text-fail absolute inset-0 grid place-items-center p-6 text-center text-[13.5px]">
          {error}
        </div>
      )}
    </div>
  );
}

/**
 * bpmn-js hängt an Importfehlern die gesammelten Warnungen an. Sie benennen
 * meist die eigentliche Ursache (unbekanntes Element, kaputte Referenz) viel
 * genauer als die Fehlermeldung selbst und gehören deshalb in die Anzeige.
 */
function describeImportError(cause: unknown): string {
  const message =
    cause instanceof Error ? cause.message : 'Das Diagramm konnte nicht gezeichnet werden.';

  const warnings = (cause as { warnings?: { message?: string }[] })?.warnings ?? [];
  if (warnings.length === 0) return message;

  const details = warnings
    .slice(0, 3)
    .map((warning) => warning?.message ?? String(warning))
    .join(' · ');

  return `${message} — ${details}`;
}

function applyMarkers(viewer: ViewerLike, markers: Record<string, NodeMarker>, tokens: string[]): void {
  const canvas = viewer.get<CanvasLike>('canvas');
  const overlays = viewer.get<OverlaysLike>('overlays');

  overlays.clear();

  for (const [elementId, marker] of Object.entries(markers)) {
    try {
      for (const existing of ALL_MARKERS) canvas.removeMarker(elementId, existing);
      canvas.addMarker(elementId, `flowzer-${marker}`);
    } catch {
      // Elemente aus älteren Versionen können im aktuellen Diagramm fehlen —
      // eine fehlende Markierung darf die Ansicht nicht abbrechen.
    }
  }

  for (const elementId of tokens) {
    try {
      const dot = document.createElement('div');
      dot.className = 'flowzer-token';
      overlays.add(elementId, { position: { top: -9, left: -9 }, html: dot });
    } catch {
      // siehe oben
    }
  }
}
