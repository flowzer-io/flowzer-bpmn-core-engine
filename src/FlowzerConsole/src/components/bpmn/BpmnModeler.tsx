import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';

import 'bpmn-js/dist/assets/diagram-js.css';
import 'bpmn-js/dist/assets/bpmn-js.css';
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn-embedded.css';
import './bpmn.css';

import { ICON_PATHS } from '@/components/ui/icons.gen';
import { InlineSpinner } from '@/components/ui/States';
import { cn } from '@/lib/cn';
import { describeFormKey } from '@/lib/formKey';

import { createBpmnEditor, type BpmnEditor } from './bpmnEditor';
import { BpmnProperties } from './properties/BpmnProperties';

export interface BpmnModelerHandle {
  /** Liefert das aktuelle Diagramm als formatiertes BPMN-XML. */
  getXml: () => Promise<string>;
  zoomIn: () => void;
  zoomOut: () => void;
  zoomReset: () => void;
  undo: () => void;
  redo: () => void;
  /** Aktuelle Zoomstufe in Prozent. */
  getZoom: () => number;
}

interface BpmnModelerProps {
  xml: string | undefined;
  onChange?: () => void;
  onZoomChange?: (zoom: number) => void;
  className?: string;
  /** Ohne Modelliererrolle bleibt das Panel lesbar, aber unveränderlich. */
  readOnly?: boolean;
}

interface ModelerLike {
  importXML: (xml: string) => Promise<{ warnings: unknown[] }>;
  saveXML: (options: { format: boolean }) => Promise<{ xml?: string }>;
  get: <T>(name: string) => T;
  on: (event: string, callback: (event?: unknown) => void) => void;
  destroy: () => void;
}

interface CanvasLike {
  zoom: (mode?: string | number, center?: unknown) => number;
  viewbox: () => { outer: { width: number; height: number } };
}

interface CommandStackLike {
  undo: () => void;
  redo: () => void;
}

interface OverlaysLike {
  add: (elementId: string, type: string, config: unknown) => string;
  remove: (filter: { type: string }) => void;
}

interface SelectionChangedEvent {
  newSelection: { id: string }[];
}

interface SelectionLike {
  select: (element: unknown) => void;
}

interface ElementRegistryLike {
  get: (id: string) => unknown;
}

const FORM_OVERLAY_TYPE = 'flowzer-form';

/**
 * BPMN-Modeler mit dem Eigenschaften-Panel der Konsole.
 *
 * Die Engine parst `zeebe:`-Erweiterungen (Formular, Zuweisung, Frist, Zuordnungen,
 * Auftragstyp) — deshalb wird die Zeebe-Moddle-Erweiterung geladen. Das Panel dazu ist
 * bewusst ein eigenes React-Panel statt des mitgelieferten Camunda-Panels: Es zeigt nur
 * die Felder, die diese Engine wirklich auswertet, und benennt sie in Flowzers Begriffen.
 *
 * Camundas Verhaltensmodul (`camunda-bpmn-js-behaviors`) laeuft hier bewusst nicht mit.
 * Es schreibt die Camunda-8.5-Semantik ins Modell: Jede neue menschliche Aufgabe bekaeme
 * ein `zeebe:userTask`, und ihr Form-Key wanderte danach in `zeebe:externalReference` —
 * ein Attribut, das dieser Parser gar nicht liest. Ein so modellierter Workflow liesse
 * sich nicht mehr speichern.
 */
export const BpmnModeler = forwardRef<BpmnModelerHandle, BpmnModelerProps>(function BpmnModeler(
  { xml, onChange, onZoomChange, className, readOnly = false },
  ref,
) {
  const canvasRef = useRef<HTMLDivElement>(null);
  const modelerRef = useRef<ModelerLike | null>(null);
  const pendingFitRef = useRef(false);
  const onChangeRef = useRef(onChange);
  const onZoomChangeRef = useRef(onZoomChange);

  const [ready, setReady] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<BpmnEditor | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);

  onChangeRef.current = onChange;
  onZoomChangeRef.current = onZoomChange;

  useImperativeHandle(
    ref,
    () => ({
      getXml: async () => {
        const modeler = modelerRef.current;
        if (!modeler) throw new Error('Der Modeler ist noch nicht bereit.');
        const { xml: result } = await modeler.saveXML({ format: true });
        if (!result) throw new Error('Das Diagramm konnte nicht serialisiert werden.');
        return result;
      },
      zoomIn: () => adjustZoom(modelerRef.current, 0.1, onZoomChangeRef.current),
      zoomOut: () => adjustZoom(modelerRef.current, -0.1, onZoomChangeRef.current),
      zoomReset: () => {
        const canvas = modelerRef.current?.get<CanvasLike>('canvas');
        if (!canvas) return;
        canvas.zoom('fit-viewport');
        onZoomChangeRef.current?.(Math.round(canvas.zoom() * 100));
      },
      undo: () => modelerRef.current?.get<CommandStackLike>('commandStack').undo(),
      redo: () => modelerRef.current?.get<CommandStackLike>('commandStack').redo(),
      getZoom: () => Math.round((modelerRef.current?.get<CanvasLike>('canvas').zoom() ?? 1) * 100),
    }),
    [],
  );

  useEffect(() => {
    let disposed = false;
    let resizeObserver: ResizeObserver | null = null;

    async function create() {
      const container = canvasRef.current;
      if (!container) return;

      const [{ default: Modeler }, { default: zeebeModdle }] = await Promise.all([
        import('bpmn-js/lib/Modeler'),
        import('zeebe-bpmn-moddle/resources/zeebe.json'),
      ]);

      if (disposed) return;

      const ModelerCtor = Modeler as unknown as new (options: Record<string, unknown>) => ModelerLike;
      const modeler = new ModelerCtor({
        container,
        moddleExtensions: { zeebe: zeebeModdle },
      });

      modelerRef.current = modeler;

      modeler.on('commandStack.changed', () => {
        onChangeRef.current?.();
        setRevision((current) => current + 1);
      });
      modeler.on('selection.changed', (event) => {
        const selection = (event as SelectionChangedEvent).newSelection;
        // Nur bei genau einem Element gibt es sinnvolle Eigenschaften; bei mehreren zeigt
        // das Panel wieder die Sicht auf den ganzen Workflow.
        setSelectedId(selection.length === 1 ? (selection[0]?.id ?? null) : null);
      });
      modeler.on('canvas.viewbox.changed', () => {
        const canvas = modeler.get<CanvasLike>('canvas');
        onZoomChangeRef.current?.(Math.round(canvas.zoom() * 100));
      });

      // Bekommt die Zeichenflaeche ihre Groesse erst nach dem Import, wird das Einpassen
      // hier nachgeholt. Ohne das bliebe das Diagramm in der linken oberen Ecke stehen.
      resizeObserver = new ResizeObserver(() => {
        if (!pendingFitRef.current) return;
        if (fitViewport(modeler, onZoomChangeRef.current)) pendingFitRef.current = false;
      });
      resizeObserver.observe(container);

      setEditor(() => createBpmnEditor(modeler));
      setReady(true);
    }

    // Ohne diesen Fang blieb ein Fehler beim Nachladen der bpmn-js-Buendel eine stille
    // abgelehnte Zusage: `ready` wurde nie wahr, und die Seite zeigte dauerhaft
    // „Modeler wird geladen …“ — von aussen nicht von einem Hänger zu unterscheiden.
    create().catch((cause: unknown) => {
      if (disposed) return;
      console.error('[BpmnModeler] Start fehlgeschlagen', cause);
      setError(
        cause instanceof Error
          ? `Der Modeler konnte nicht gestartet werden: ${cause.message}`
          : 'Der Modeler konnte nicht gestartet werden.',
      );
    });

    return () => {
      disposed = true;
      resizeObserver?.disconnect();
      modelerRef.current?.destroy();
      modelerRef.current = null;
      setEditor(null);
      setReady(false);
    };
  }, []);

  useEffect(() => {
    const modeler = modelerRef.current;
    if (!ready || !modeler || !xml) return;

    let cancelled = false;

    async function load() {
      try {
        await modeler!.importXML(xml!);
        if (cancelled) return;
        setError(null);
        setSelectedId(null);
        setRevision((current) => current + 1);

        // Das Einpassen gehoert nicht mehr in diesen Versuch: Es misst die Zeichenflaeche,
        // und die ist beim ersten Zeichnen der Seite mitunter noch null Pixel breit. Ein
        // Fehler dabei ist kein Ladefehler — das Diagramm steht dann laengst.
        pendingFitRef.current = true;
        if (fitViewport(modeler!, onZoomChangeRef.current)) pendingFitRef.current = false;
      } catch (cause) {
        if (cancelled) return;
        setError(cause instanceof Error ? cause.message : 'Das Diagramm konnte nicht geladen werden.');
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [ready, xml]);

  // Markiert jede menschliche Aufgabe, an der ein Formular hängt. Ohne die Markierung ist
  // dem Diagramm nicht anzusehen, welche Aufgabe schon eine Eingabemaske hat und welche nicht.
  useEffect(() => {
    const modeler = modelerRef.current;
    if (!ready || !modeler || !editor) return;

    const overlays = modeler.get<OverlaysLike>('overlays');
    overlays.remove({ type: FORM_OVERLAY_TYPE });

    for (const task of editor.listUserTasks()) {
      if (!task.formKey) continue;
      try {
        overlays.add(task.id, FORM_OVERLAY_TYPE, {
          position: { top: 2, right: 2 },
          html: formBadge(task.formKey),
        });
      } catch {
        // Ein Element, das zwischen Lesen und Zeichnen verschwunden ist, darf die
        // uebrigen Markierungen nicht verhindern.
      }
    }
  }, [ready, editor, revision]);

  return (
    <div className={cn('flex min-h-0 flex-1', className)}>
      <div className="canvas-grid bpmn-surface relative min-h-[420px] min-w-0 flex-1">
        <div ref={canvasRef} className="h-full w-full" />
        {!ready && !error && (
          <div className="absolute inset-0 grid place-items-center">
            <InlineSpinner label="Modeler wird geladen …" />
          </div>
        )}
        {error && (
          <div className="bg-surface/90 text-fail absolute inset-0 grid place-items-center p-6 text-center text-[13.5px]">
            {error}
          </div>
        )}
      </div>

      <div className="border-border bg-surface w-[320px] flex-none overflow-auto border-l">
        <BpmnProperties
          editor={editor}
          selectedId={selectedId}
          revision={revision}
          readOnly={readOnly}
          onSelect={(elementId) => {
            const modeler = modelerRef.current;
            if (!modeler) return;
            const element = modeler.get<ElementRegistryLike>('elementRegistry').get(elementId);
            if (element) modeler.get<SelectionLike>('selection').select(element);
          }}
        />
      </div>
    </div>
  );
});

/**
 * Passt das Diagramm in die Zeichenflaeche ein. Liefert `false`, solange die Flaeche noch
 * keine Groesse hat — bpmn-js rechnet dann mit einem Massstab, den SVG nicht annimmt.
 */
function fitViewport(modeler: ModelerLike, notify: ((zoom: number) => void) | undefined): boolean {
  const canvas = modeler.get<CanvasLike>('canvas');
  const { outer } = canvas.viewbox();

  if (!outer || outer.width <= 0 || outer.height <= 0) return false;

  canvas.zoom('fit-viewport');
  notify?.(Math.round(canvas.zoom() * 100));
  return true;
}

/** Die Markierung am Element: dasselbe Symbol, das die Konsole für Formulare benutzt. */
function formBadge(formKey: string): HTMLElement {
  const badge = document.createElement('div');
  badge.className = 'flowzer-form-badge';
  badge.title = `Formular: ${describeFormKey(formKey)}`;

  const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  icon.setAttribute('viewBox', '0 -960 960 960');
  icon.setAttribute('width', '12');
  icon.setAttribute('height', '12');
  icon.setAttribute('fill', 'currentColor');
  icon.innerHTML = ICON_PATHS['description'] ?? '';
  badge.appendChild(icon);

  return badge;
}

function adjustZoom(
  modeler: ModelerLike | null,
  delta: number,
  notify: ((zoom: number) => void) | undefined,
): void {
  const canvas = modeler?.get<CanvasLike>('canvas');
  if (!canvas) return;

  const next = Math.min(4, Math.max(0.2, canvas.zoom() + delta));
  canvas.zoom(next);
  notify?.(Math.round(next * 100));
}
