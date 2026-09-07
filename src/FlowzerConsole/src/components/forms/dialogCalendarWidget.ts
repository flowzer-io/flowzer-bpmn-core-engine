import type * as FormioJs from '@formio/js';

type WidgetRegistry = typeof FormioJs.Widgets;

let registered = false;

/**
 * Sorgt dafür, dass der Kalender eines Datumsfeldes in dem Dialog steht, in dem das Feld steht.
 *
 * Form.io baut Datumsfelder mit flatpickr, und flatpickr hängt seinen Kalender ohne weitere
 * Angabe an `document.body` — also außerhalb eines geöffneten Dialogs. Ein modaler Dialog
 * sperrt dort aber genau das, was der Kalender braucht: Der Body bekommt
 * `pointer-events: none`, bedienbar bleiben nur Overlay und Dialoginhalt. Der Klick auf einen
 * Tag traf deshalb das Overlay, flatpickr wertete ihn als Klick nach draußen und klappte den
 * Kalender wieder zu. Von Hand tippen ging, auswählen nicht. Dieselbe Grenze zieht die
 * Fokusfalle des Dialogs — sie träfe die Zahlenfelder für die Uhrzeit.
 *
 * Die Abhilfe ist flatpickrs `static`: Der Kalender wird dann in eine Hülle direkt neben dem
 * Eingabefeld gehängt, steht also mitten im Dialog. `appendTo` auf den Dialog reicht dafür
 * nicht — flatpickr rechnet die Position in Dokumentkoordinaten und setzt sie absolut, der
 * Dialoginhalt ist aber `fixed` und über `transform` verschoben und wird damit selbst zum
 * Bezugsrahmen; der Kalender landete neben dem Bild.
 *
 * Außerhalb eines Dialogs — auf der Aufgabenseite etwa — bleibt alles unverändert.
 *
 * Die Registrierung tauscht den Eintrag in Form.ios Widget-Registry. `Input.createWidget`
 * liest den Konstruktor bei jedem Aufbau eines Feldes aus genau diesem Objekt.
 */
export function registerDialogCalendarWidget(widgets: WidgetRegistry): void {
  // Ein zweiter Aufruf würde die eigene Unterklasse noch einmal ableiten.
  if (registered) return;
  registered = true;

  const BaseCalendarWidget = widgets.calendar;

  class DialogCalendarWidget extends BaseCalendarWidget {
    attach(input: HTMLElement) {
      if (input?.closest?.('[role="dialog"]')) {
        this.settings.static = true;
      }

      return super.attach(input);
    }

    initFlatpickr(Flatpickr: unknown): void {
      super.initFlatpickr(Flatpickr);

      if (!this.settings.static) return;

      // Der Dialog hat eine eigene Bildlauffläche und schneidet ab, was darüber hinausragt.
      // Der aufgeklappte Kalender steht unter seinem Feld und wäre unten angeschnitten.
      this.calendar?.config?.onOpen?.push(() => {
        // Nicht im selben Zug: flatpickr setzt die Position erst nach diesem Aufruf.
        window.setTimeout(() => {
          this.calendar?.calendarContainer?.scrollIntoView({ block: 'nearest' });
        }, 0);
      });
    }
  }

  widgets.calendar = DialogCalendarWidget;
}
