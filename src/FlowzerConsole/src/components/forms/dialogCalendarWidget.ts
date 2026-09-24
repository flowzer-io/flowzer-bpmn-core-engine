import type * as FormioJs from '@formio/js';

type WidgetRegistry = typeof FormioJs.Widgets;

/** Rechteck in Fensterkoordinaten, wie `getBoundingClientRect` es liefert. */
export interface ViewportRect {
  top: number;
  right: number;
  bottom: number;
  left: number;
}

export interface CalendarPlacement {
  /** Oberkante in Fensterkoordinaten. */
  top: number;
  /** Linke Kante in Fensterkoordinaten. */
  left: number;
  /** Der Kalender steht über dem Feld, weil darunter kein Platz ist. */
  above: boolean;
}

/** Mindestabstand des Kalenders zum Fensterrand, in CSS-Pixeln. */
const VIEWPORT_MARGIN = 8;
/** Abstand zwischen Feld und Kalender — derselbe, den flatpickr selbst lässt. */
const FIELD_GAP = 2;

/** Kennzeichen der eigenen Unterklasse, damit ein zweiter Aufruf sie nicht noch einmal ableitet. */
const DIALOG_AWARE = 'flowzerDialogAware';

/** Die Ebene im Dialog, an der die Kalender hängen. */
export const CALENDAR_LAYER_CLASS = 'flowzer-calendar-layer';

function clamp(value: number, min: number, max: number): number {
  // Passt der Kalender nicht hinein, gewinnt die linke beziehungsweise obere Kante.
  return Math.max(min, Math.min(value, max));
}

/**
 * Wohin der Kalender eines Feldes im Fenster gehört.
 *
 * Senkrecht: unter das Feld; reicht der Platz dort nicht und ist darüber mehr, über das Feld.
 * Notfalls wird er ans Fenster geklemmt. Waagerecht: bündig mit der linken Feldkante; ragte er
 * so über den Dialog hinaus, bündig mit der rechten. Über den Dialog hinaus darf er nur, wenn
 * er breiter ist als der Dialog — aus dem Fenster heraus nie.
 */
export function placeCalendar(
  field: ViewportRect,
  size: { width: number; height: number },
  dialog: ViewportRect,
  viewport: { width: number; height: number },
): CalendarPlacement {
  const spaceBelow = viewport.height - VIEWPORT_MARGIN - (field.bottom + FIELD_GAP);
  const spaceAbove = field.top - FIELD_GAP - VIEWPORT_MARGIN;
  const above = size.height > spaceBelow && spaceAbove > spaceBelow;
  const top = clamp(
    above ? field.top - FIELD_GAP - size.height : field.bottom + FIELD_GAP,
    VIEWPORT_MARGIN,
    viewport.height - VIEWPORT_MARGIN - size.height,
  );

  const windowLeft = VIEWPORT_MARGIN;
  const windowRight = viewport.width - VIEWPORT_MARGIN;
  const dialogLeft = Math.max(dialog.left, windowLeft);
  const dialogRight = Math.min(dialog.right, windowRight);
  const fitsDialog = size.width <= dialogRight - dialogLeft;
  const minLeft = fitsDialog ? dialogLeft : windowLeft;
  const maxRight = fitsDialog ? dialogRight : windowRight;
  const preferred = field.left + size.width <= maxRight ? field.left : field.right - size.width;
  const left = clamp(preferred, minLeft, maxRight - size.width);

  return { top, left, above };
}

/** Der Ausschnitt einer flatpickr-Instanz, den die Positionierung braucht. */
interface FlatpickrInstance {
  isOpen: boolean;
  calendarContainer?: HTMLElement;
  _positionElement?: HTMLElement;
}

/**
 * Ersetzt flatpickrs eigene Positionierung (`config.position` als Funktion).
 *
 * flatpickr rechnet in Dokumentkoordinaten und geht davon aus, dass der Kalender am <body>
 * hängt. Hier hängt er an einer Ebene im Dialog, und der Dialog ist über `translate` zentriert
 * — damit ist er selbst der Bezugsrahmen für alles, was in ihm absolut oder fest steht. Die
 * Lage wird deshalb in Fensterkoordinaten bestimmt und dann auf die Ebene umgerechnet: Deren
 * `getBoundingClientRect` ist genau der Ursprung, auf den sich `top` und `left` des Kalenders
 * beziehen, gleich wie der Dialog verschoben ist.
 */
function positionInViewport(instance: FlatpickrInstance, customElement?: HTMLElement): void {
  const calendar = instance.calendarContainer;
  const layer = calendar?.parentElement;
  const dialog = layer?.parentElement;
  const anchor = customElement ?? instance._positionElement;
  if (!instance.isOpen || !calendar || !layer || !dialog || !anchor) return;

  // Das Eingabefeld steht mit dem Kalendersymbol in einer Gruppe; bündig wird mit der Gruppe.
  const field = (anchor.closest('.input-group') ?? anchor).getBoundingClientRect();
  const viewport = {
    width: document.documentElement.clientWidth || window.innerWidth,
    height: window.innerHeight,
  };
  const placement = placeCalendar(
    field,
    { width: calendar.offsetWidth, height: calendar.offsetHeight },
    dialog.getBoundingClientRect(),
    viewport,
  );

  const origin = layer.getBoundingClientRect();
  calendar.style.top = `${placement.top - origin.top}px`;
  calendar.style.left = `${placement.left - origin.left}px`;
  calendar.style.right = 'auto';
}

function createCalendarLayer(dialog: Element): HTMLElement {
  const layer = dialog.ownerDocument.createElement('div');
  layer.className = CALENDAR_LAYER_CLASS;
  // Nimmt im Dialog keinen Platz ein — auch nicht als Flex-Element — und ist Bezugsrahmen
  // für den absolut gesetzten Kalender.
  layer.style.cssText = 'position:absolute;top:0;left:0;width:0;height:0;';
  dialog.appendChild(layer);
  return layer;
}

/**
 * Sorgt dafür, dass der Kalender eines Datumsfeldes in einem Dialog bedienbar ist und ganz im
 * Bild steht.
 *
 * Form.io baut Datumsfelder mit flatpickr, und flatpickr hängt seinen Kalender ohne weitere
 * Angabe an `document.body` — also außerhalb eines geöffneten Dialogs. Ein modaler Dialog
 * sperrt dort aber genau das, was der Kalender braucht: Der Body bekommt
 * `pointer-events: none`, bedienbar bleiben nur Overlay und Dialoginhalt. Der Klick auf einen
 * Tag traf deshalb das Overlay, flatpickr wertete ihn als Klick nach draußen und klappte den
 * Kalender wieder zu. Dieselbe Grenze zieht die Fokusfalle des Dialogs — sie träfe die
 * Zahlenfelder für die Uhrzeit.
 *
 * Bis Issue #368 half flatpickrs `static`: Der Kalender stand in einer Hülle direkt unter dem
 * Feld, mitten in der Bildlauffläche des Dialogs. Er ist aber gut 300 px breit — breiter als
 * eine halbe Dialogspalte und auf dem Telefon breiter als das ganze Feld. Der Dialog bekam
 * eine waagerechte Bildlaufleiste, rollte beim Aufklappen seitlich mit und schnitt die
 * Beschriftungen links ab.
 *
 * Jetzt hängt der Kalender an einer eigenen Ebene (`appendTo`) direkt im Dialogelement, außerhalb
 * der Bildlauffläche. Er liegt damit weiter innerhalb von `role="dialog"` — Zeigereingaben und
 * Fokusfalle lassen ihn zu —, verbreitert aber nichts mehr. Wo genau er steht, bestimmt
 * `positionInViewport`: unter oder über dem Feld, im Dialog, wo er hineinpasst, und immer im
 * Fenster. Rollt der Dialoginhalt, während der Kalender offen ist, zieht er mit.
 *
 * Außerhalb eines Dialogs — auf der Aufgabenseite etwa — bleibt alles unverändert.
 *
 * Die Registrierung tauscht den Eintrag in Form.ios Widget-Registry. `Input.createWidget`
 * liest den Konstruktor bei jedem Aufbau eines Feldes aus genau diesem Objekt.
 */
export function registerDialogCalendarWidget(widgets: WidgetRegistry): void {
  const BaseCalendarWidget = widgets.calendar;
  if ((BaseCalendarWidget as unknown as Record<string, unknown>)[DIALOG_AWARE]) return;

  class DialogCalendarWidget extends BaseCalendarWidget {
    static readonly [DIALOG_AWARE] = true;

    private calendarLayer: HTMLElement | null = null;
    private stopFollowing: (() => void) | null = null;

    attach(input: HTMLElement) {
      this.removeCalendarLayer();
      const dialog = input?.closest?.('[role="dialog"]');
      if (dialog) this.calendarLayer = createCalendarLayer(dialog);

      return super.attach(input);
    }

    initFlatpickr(Flatpickr: unknown): void {
      const layer = this.calendarLayer;
      if (layer) {
        // Form.io setzt `position` in attach() auf 'auto center'; erst hier gilt der Wert.
        this.settings.appendTo = layer;
        this.settings.position = positionInViewport;
      }

      super.initFlatpickr(Flatpickr);

      const calendar = this.calendar;
      if (!layer || !calendar) return;

      // flatpickr folgt von sich aus nur einer Größenänderung des Fensters. Scroll-Ereignisse
      // steigen nicht auf; die Bildlauffläche des Dialogs erreicht nur ein Zuhörer in der
      // Capture-Phase.
      const follow = () => calendar._positionCalendar?.();
      const stop = () => document.removeEventListener('scroll', follow, true);
      calendar.config?.onOpen?.push(() => document.addEventListener('scroll', follow, true));
      calendar.config?.onClose?.push(stop);
      this.stopFollowing = stop;
    }

    destroy(all?: boolean) {
      this.stopFollowing?.();
      this.stopFollowing = null;
      super.destroy(all);
      this.removeCalendarLayer();
    }

    private removeCalendarLayer() {
      this.calendarLayer?.remove();
      this.calendarLayer = null;
    }
  }

  widgets.calendar = DialogCalendarWidget;
}
