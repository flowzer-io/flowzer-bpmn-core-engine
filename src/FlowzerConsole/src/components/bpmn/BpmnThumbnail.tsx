import { useEffect, useRef, useState } from 'react';

import { useDefinitionXml } from '@/lib/api/queries';
import { cn } from '@/lib/cn';

import { BpmnViewer } from './BpmnViewer';

interface BpmnThumbnailProps {
  /** Guid der Definitionsversion, deren XML gezeichnet wird. */
  versionGuid: string | null | undefined;
  className?: string;
}

/**
 * Vorschaubild eines Prozesses auf den Workflow-Karten.
 *
 * Das Diagramm wird erst geladen, wenn die Karte tatsächlich sichtbar wird — bei
 * vielen Workflows würden sonst alle XMLs gleichzeitig angefragt und ebenso viele
 * bpmn-js-Instanzen entstehen.
 */
export function BpmnThumbnail({ versionGuid, className }: BpmnThumbnailProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const element = containerRef.current;
    if (!element || visible) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          setVisible(true);
          observer.disconnect();
        }
      },
      { rootMargin: '200px' },
    );

    observer.observe(element);
    return () => observer.disconnect();
  }, [visible]);

  const { data: xml } = useDefinitionXml(visible ? versionGuid : undefined);

  return (
    <div ref={containerRef} className={cn('canvas-grid border-border relative border-b', className)}>
      {xml ? (
        <BpmnViewer xml={xml} interactive={false} className="pointer-events-none h-full w-full" />
      ) : (
        <PlaceholderDiagram />
      )}
    </div>
  );
}

/** Stilisierte Prozesskette, solange kein echtes Diagramm vorliegt. */
function PlaceholderDiagram() {
  return (
    <div className="flex h-full w-full items-center justify-center" aria-hidden>
      <div className="flex items-center opacity-70">
        <span className="border-accent bg-surface h-[15px] w-[15px] flex-none rounded-full border-2" />
        <span className="from-accent to-accent-2 h-0.5 w-5 flex-none bg-gradient-to-r" />
        <span className="border-border-strong bg-surface h-[22px] w-9 flex-none rounded-[5px] border-[1.5px]" />
        <span className="bg-border-strong h-0.5 w-5 flex-none" />
        <span className="border-border-strong bg-surface h-[15px] w-[15px] flex-none rotate-45 border-[1.5px]" />
        <span className="bg-border-strong h-0.5 w-5 flex-none" />
        <span className="border-border-strong bg-surface h-[22px] w-9 flex-none rounded-[5px] border-[1.5px]" />
        <span className="bg-border-strong h-0.5 w-5 flex-none" />
        <span className="bg-accent h-[15px] w-[15px] flex-none rounded-full" />
      </div>
    </div>
  );
}
