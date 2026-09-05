import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';

import 'bpmn-js/dist/assets/diagram-js.css';
import 'bpmn-js/dist/assets/bpmn-js.css';
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn-embedded.css';
import '@bpmn-io/properties-panel/assets/properties-panel.css';
import './bpmn.css';

import { InlineSpinner } from '@/components/ui/States';
import { cn } from '@/lib/cn';

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
}

interface CommandStackLike {
  undo: () => void;
  redo: () => void;
}

/**
 * BPMN-Modeler mit Camunda-8-Eigenschaften-Panel.
 *
 * Die Engine parst `zeebe:`-Erweiterungen (Form-Key, Assignment, Task-Schedule,
 * IO-Mapping) — deshalb werden hier exakt die Zeebe-Module geladen und nicht die
 * Camunda-7-Variante.
 */
export const BpmnModeler = forwardRef<BpmnModelerHandle, BpmnModelerProps>(function BpmnModeler(
  { xml, onChange, onZoomChange, className },
  ref,
) {
  const canvasRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const modelerRef = useRef<ModelerLike | null>(null);
  const onChangeRef = useRef(onChange);
  const onZoomChangeRef = useRef(onZoomChange);

  const [ready, setReady] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

    async function create() {
      const container = canvasRef.current;
      const propertiesPanel = panelRef.current;
      if (!container || !propertiesPanel) return;

      const [
        { default: Modeler },
        propertiesPanelModules,
        { default: zeebeModdle },
        { default: zeebeBehaviors },
      ] = await Promise.all([
        import('bpmn-js/lib/Modeler'),
        import('bpmn-js-properties-panel'),
        import('zeebe-bpmn-moddle/resources/zeebe.json'),
        import('camunda-bpmn-js-behaviors/lib/camunda-cloud'),
      ]);

      if (disposed) return;

      const ModelerCtor = Modeler as unknown as new (options: Record<string, unknown>) => ModelerLike;
      const modeler = new ModelerCtor({
        container,
        propertiesPanel: { parent: propertiesPanel },
        additionalModules: [
          propertiesPanelModules.BpmnPropertiesPanelModule,
          propertiesPanelModules.BpmnPropertiesProviderModule,
          propertiesPanelModules.ZeebePropertiesProviderModule,
          zeebeBehaviors,
        ],
        moddleExtensions: { zeebe: zeebeModdle },
      });

      modelerRef.current = modeler;

      modeler.on('commandStack.changed', () => onChangeRef.current?.());
      modeler.on('canvas.viewbox.changed', () => {
        const canvas = modeler.get<CanvasLike>('canvas');
        onZoomChangeRef.current?.(Math.round(canvas.zoom() * 100));
      });

      setReady(true);
    }

    void create();

    return () => {
      disposed = true;
      modelerRef.current?.destroy();
      modelerRef.current = null;
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
        modeler!.get<CanvasLike>('canvas').zoom('fit-viewport');
        onZoomChangeRef.current?.(Math.round(modeler!.get<CanvasLike>('canvas').zoom() * 100));
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

  return (
    <div className={cn('flex min-h-0 flex-1', className)}>
      <div className="canvas-grid bpmn-surface relative min-w-0 flex-1">
        <div ref={canvasRef} className="h-full w-full" />
        {!ready && (
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

      <div
        ref={panelRef}
        className="bpmn-properties border-border w-[320px] flex-none overflow-auto border-l"
      />
    </div>
  );
});

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
