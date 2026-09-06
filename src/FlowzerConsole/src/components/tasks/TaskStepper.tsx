import { useEffect, useRef } from 'react';

import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';
import { mainPath, type BpmnFlowNode, type BpmnModelSummary } from '@/lib/bpmnModel';

interface TaskStepperProps {
  model: BpmnModelSummary;
  /** Der Knoten, auf dem die Instanz gerade steht. */
  currentNodeId: string | null | undefined;
  className?: string;
}

/** Ein Tor ist kein Schritt, den jemand erledigt — die Engine entscheidet dort. */
function isGateway(node: BpmnFlowNode) {
  return node.type.endsWith('Gateway');
}

/**
 * NICHT EINGEHAENGT. Die Aufgabenseite zeigt diese Leiste zurzeit nicht.
 *
 * Zwei Fragen sind offen, und beide sind fachlich, nicht gestalterisch:
 *
 * Wer darf den Ablauf ueberhaupt sehen? Wie ein Antrag im Haus laeuft, wer ihn
 * freigibt und was danach kommt, kann heikel sein. Ein Aussenstehender, der eine
 * einzelne Aufgabe erledigt, muss den ganzen Prozess nicht kennen — sichtbar sein
 * sollte das also je Aufgabe entscheidbar, nicht pauschal.
 *
 * Und der naechste Schritt steht oft gar nicht fest: Nach einem Tor haengt er vom
 * Ergebnis ab. „Im Anschluss: …" behauptet dann eine Sicherheit, die es nicht gibt.
 *
 * Die Entwuerfe dazu liegen in Claude Design („Schrittleiste"). Bis das entschieden
 * ist, bleibt die Komponente stehen, aber ungenutzt — sie funktioniert.
 *
 * Zeigt, an welcher Stelle des Prozesses die Aufgabe steht.
 * Grundlage ist der Hauptpfad des BPMN-Modells (siehe `mainPath`).
 *
 * Die Leiste laesst sich schieben, statt alles in eine Zeile zu pressen: Ein Prozess mit
 * vierzehn Knoten bekaeme sonst je Schritt ein paar Pixel, und aus „Urlaubstage pruefen"
 * wuerde „U.". Tore tragen nur einen Rautenpunkt — ihre Namen („alle geprueft") kosten
 * Platz, den die eigentlichen Schritte brauchen; der volle Name steht im Titel.
 */
export function TaskStepper({ model, currentNodeId, className }: TaskStepperProps) {
  const path = mainPath(model, currentNodeId);
  const scrollRef = useRef<HTMLDivElement>(null);

  const currentIndex = path.findIndex((node) => node.id === currentNodeId);

  useEffect(() => {
    // Den aktuellen Schritt in die Mitte holen. Ohne das steht man bei einem laengeren
    // Prozess am Anfang der Leiste und sieht ausgerechnet die eigene Aufgabe nicht.
    let frame = 0;

    const zentrieren = () => {
      const rail = scrollRef.current;
      // Der eigene Schritt wird ueber seine Position geholt, nicht ueber ein Ref: Ein Ref,
      // das je nach Bedingung an einem anderen Geschwisterelement haengt, kann nach dem
      // Wechsel null sein — dann bliebe die Leiste stumm am Anfang stehen.
      const active = rail?.children[currentIndex] as HTMLElement | undefined;
      if (!rail || !active) return;

      // Beim ersten Durchlauf hat die Leiste noch keine Breite; mit clientWidth = 0
      // landete die Rechnung rechts neben dem eigenen Schritt. Dann eben ein Bild spaeter.
      if (rail.clientWidth === 0) {
        frame = requestAnimationFrame(zentrieren);
        return;
      }

      // Absolut gerechnet, nicht als Verschiebung: Im Entwicklungsmodus laesst React den
      // Effekt zweimal laufen, und ein `+=` schoebe dann doppelt.
      rail.scrollLeft = active.offsetLeft - (rail.clientWidth - active.offsetWidth) / 2;
    };

    frame = requestAnimationFrame(zentrieren);

    // Auch nach jeder Breitenaenderung neu ausrichten — beim Zusammenklappen der
    // Navigation oder auf einem schmalen Fenster rutschte der eigene Schritt sonst
    // wieder aus dem Bild.
    const beobachter = scrollRef.current ? new ResizeObserver(zentrieren) : null;
    if (beobachter && scrollRef.current) beobachter.observe(scrollRef.current);

    return () => {
      cancelAnimationFrame(frame);
      beobachter?.disconnect();
    };
  }, [currentIndex, path.length]);

  if (path.length === 0) return null;

  const nextStep = currentIndex >= 0 ? path.slice(currentIndex + 1).find((node) => !isGateway(node)) : undefined;

  return (
    <div
      className={cn(
        'bg-surface border-border shadow-card overflow-hidden rounded-[var(--r-lg)] border',
        className,
      )}
    >
      <div className="flex items-baseline justify-between gap-3 px-[18px] pt-3">
        <span className="text-faint font-mono text-[10.5px] tracking-[0.14em] uppercase">Ablauf</span>
        {currentIndex >= 0 && (
          <span className="text-muted text-[12px]">
            Schritt {currentIndex + 1} von {path.length}
          </span>
        )}
      </div>

      <div
        ref={scrollRef}
        className="relative flex items-center overflow-x-auto px-[18px] py-3 [-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
      >
        {path.map((node, index) => {
          const done = currentIndex >= 0 && index < currentIndex;
          const active = index === currentIndex;
          const gateway = isGateway(node);
          const label = node.name?.trim() || node.id;

          return (
            <div key={node.id} className="flex flex-none items-center">
              {index > 0 && <span className="bg-border mx-2 h-0.5 w-4 flex-none" />}
              <div className="flex flex-none items-center gap-2" title={label}>
                <span
                  className={cn(
                    'grid h-6 w-6 flex-none place-items-center',
                    gateway && !active ? 'rotate-45 rounded-[5px]' : 'rounded-full',
                  )}
                  style={
                    done
                      ? { background: 'var(--done)', color: '#fff' }
                      : active
                        ? { background: 'var(--accent)', color: 'var(--accent-ink)' }
                        : { background: 'var(--surface-2)', color: 'var(--faint)' }
                  }
                >
                  {/* Ein Tor traegt keine eigene Marke — der Punkt selbst ist die Aussage. */}
                  {(!gateway || active) && (
                    <Icon name={done ? 'check' : active ? 'edit' : 'more_horiz'} size={15} />
                  )}
                </span>
                {(!gateway || active) && (
                  <span
                    className={cn(
                      'truncate text-[12.5px]',
                      active
                        ? 'text-text max-w-[260px] font-bold'
                        : done
                          ? 'text-muted max-w-[130px] font-medium'
                          : 'text-faint max-w-[130px] font-medium',
                    )}
                  >
                    {label}
                  </span>
                )}
              </div>
            </div>
          );
        })}
      </div>

      {nextStep && (
        <div className="border-border text-muted mx-[18px] flex items-center gap-2 border-t py-2.5 text-[12.5px]">
          <Icon name="arrow_forward" size={16} className="text-accent flex-none" />
          <span className="truncate">Im Anschluss: {nextStep.name?.trim() || nextStep.id}</span>
        </div>
      )}
    </div>
  );
}
