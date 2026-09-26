import type * as FormioJs from '@formio/js';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  CALENDAR_LAYER_CLASS,
  isOutside,
  placeCalendar,
  registerDialogCalendarWidget,
} from './dialogCalendarWidget';

type WidgetRegistry = typeof FormioJs.Widgets;
type Hook = () => void;

function hooks(value: unknown): Hook[] {
  if (Array.isArray(value)) return [...(value as Hook[])];
  return typeof value === 'function' ? [value as Hook] : [];
}

/**
 * Nachbau des Teils von flatpickr, den Form.io und das Widget benutzen: Der Kalender hängt an
 * `appendTo` oder am <body>, Haken aus der Konfiguration werden wie bei flatpickr zu Listen,
 * `open()` ruft erst die onOpen-Haken und dann die Positionierung.
 */
class FakeFlatpickr {
  static created: FakeFlatpickr[] = [];

  config: Record<string, unknown> & { onOpen: Hook[]; onClose: Hook[] };
  calendarContainer = document.createElement('div');
  isOpen = false;
  _positionElement: HTMLElement;
  positionCalls = 0;

  constructor(input: HTMLElement, config: Record<string, unknown>) {
    this.config = { ...config, onOpen: hooks(config.onOpen), onClose: hooks(config.onClose) };
    this._positionElement = input;
    this.calendarContainer.className = 'flatpickr-calendar';
    const appendTo = config.appendTo as HTMLElement | undefined;
    (appendTo ?? document.body).appendChild(this.calendarContainer);
    FakeFlatpickr.created.push(this);
  }

  _positionCalendar = () => {
    this.positionCalls += 1;
    const { position } = this.config;
    if (typeof position === 'function') position(this);
  };

  open() {
    this.isOpen = true;
    this.config.onOpen.forEach((hook) => hook());
    this._positionCalendar();
  }

  close() {
    this.isOpen = false;
    this.config.onClose.forEach((hook) => hook());
  }

  destroy() {
    this.calendarContainer.remove();
  }
}

/** Nachbau von Form.ios CalendarWidget, soweit das Widget ihn überschreibt. */
class FakeCalendarWidget {
  settings: Record<string, unknown> = {};
  calendar: FakeFlatpickr | null = null;
  _input: HTMLElement | null = null;
  formioHooks = { open: vi.fn(), close: vi.fn() };

  attach(input: HTMLElement) {
    this._input = input;
    // Wie Form.io: Lage und eigene Haken setzt attach(), nicht der Konstruktor.
    this.settings.position = 'auto center';
    this.settings.onOpen = this.formioHooks.open;
    this.settings.onClose = this.formioHooks.close;
    return Promise.resolve();
  }

  initFlatpickr(Flatpickr: typeof FakeFlatpickr) {
    this.calendar = new Flatpickr(this._input as HTMLElement, { ...this.settings });
  }

  destroy() {
    this.calendar?.destroy();
  }
}

interface TestWidget {
  settings: Record<string, unknown>;
  calendar: FakeFlatpickr | null;
  formioHooks: { open: ReturnType<typeof vi.fn>; close: ReturnType<typeof vi.fn> };
  attach(input: HTMLElement): Promise<unknown>;
  initFlatpickr(Flatpickr: typeof FakeFlatpickr): void;
  destroy(all?: boolean): void;
}

function registry() {
  const widgets = { calendar: FakeCalendarWidget } as unknown as WidgetRegistry;
  registerDialogCalendarWidget(widgets);
  return widgets;
}

async function attachedWidget(input: HTMLElement, widgets = registry()): Promise<TestWidget & { calendar: FakeFlatpickr }> {
  const Widget = widgets.calendar as unknown as new () => TestWidget;
  const widget = new Widget();
  await widget.attach(input);
  widget.initFlatpickr(FakeFlatpickr);
  return widget as TestWidget & { calendar: FakeFlatpickr };
}

function rect(left: number, top: number, width: number, height: number): DOMRect {
  return { left, top, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON: () => ({}) };
}

/**
 * Das eigene Modal mit Bildlauffläche und darin Datumsfeldern in Eingabegruppen — wie im
 * Startdialog: Modal > Inhaltsbereich (overflow: auto) > Formular > Eingabegruppe > Feld.
 */
function modalWithFields(count = 1) {
  const dialog = document.createElement('div');
  dialog.setAttribute('role', 'dialog');
  dialog.setAttribute('data-flowzer-modal', '');
  const scroller = document.createElement('div');
  scroller.style.overflowX = 'auto';
  scroller.style.overflowY = 'auto';
  const form = document.createElement('div');
  form.className = 'formio-form';
  const fields = Array.from({ length: count }, () => {
    const group = document.createElement('div');
    group.className = 'input-group';
    const input = document.createElement('input');
    group.appendChild(input);
    form.appendChild(group);
    return { group, input };
  });
  scroller.appendChild(form);
  dialog.appendChild(scroller);
  document.body.appendChild(dialog);
  const { group, input } = fields[0]!;
  return { dialog, scroller, form, fields, group, input };
}

function size(element: HTMLElement, width: number, height: number) {
  Object.defineProperty(element, 'offsetWidth', { configurable: true, value: width });
  Object.defineProperty(element, 'offsetHeight', { configurable: true, value: height });
}

function layersIn(dialog: Element) {
  return dialog.querySelectorAll(`.${CALENDAR_LAYER_CLASS}`);
}

/** Ein Desktopfenster mit dem Startdialog in der Mitte und einem Feld im Inhaltsbereich. */
function desktopGeometry(dialog: HTMLElement, scroller: HTMLElement, group: HTMLElement, layer: Element) {
  vi.spyOn(window, 'innerWidth', 'get').mockReturnValue(1280);
  vi.spyOn(window, 'innerHeight', 'get').mockReturnValue(800);
  vi.spyOn(dialog, 'getBoundingClientRect').mockReturnValue(rect(380, 100, 520, 600));
  vi.spyOn(layer, 'getBoundingClientRect').mockReturnValue(rect(380, 100, 520, 600));
  vi.spyOn(scroller, 'getBoundingClientRect').mockReturnValue(rect(380, 180, 520, 440));
  return vi.spyOn(group, 'getBoundingClientRect').mockReturnValue(rect(400, 200, 400, 36));
}

beforeEach(() => {
  FakeFlatpickr.created = [];
});

afterEach(() => {
  document.body.replaceChildren();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('registerDialogCalendarWidget', () => {
  // Testzweck: Ein zweiter Aufruf leitet die eigene Unterklasse nicht noch einmal ab — sonst
  // hinge jeder Kalender an zwei Ebenen und die äußere bliebe leer im Dialog stehen.
  it('registriert sich je Registry nur einmal', () => {
    const widgets = registry();
    const first = widgets.calendar;

    registerDialogCalendarWidget(widgets);

    expect(widgets.calendar).toBe(first);
    expect(widgets.calendar).not.toBe(FakeCalendarWidget);
  });

  // Testzweck: Im eigenen Modal hängt der Kalender an einer eigenen Ebene des Dialogelements —
  // innerhalb von role="dialog" (Zeigereingaben, Fokusfalle), aber nicht mehr als statischer
  // Block im Dialoginhalt, der die Bildlauffläche verbreitert (Issue #368). Die Ebene fängt
  // selbst keine Klicks ab; der Kalender nimmt sie an.
  it('hängt den Kalender im Modal an eine eigene Ebene statt statisch ins Formular', async () => {
    const { dialog, input } = modalWithFields();
    const widget = await attachedWidget(input);

    const layer = dialog.querySelector<HTMLElement>(`.${CALENDAR_LAYER_CLASS}`);
    expect(layer).not.toBeNull();
    expect(layer?.parentElement).toBe(dialog);
    expect(layer?.style.pointerEvents).toBe('none');
    expect(widget.settings.static).toBeUndefined();
    expect(widget.settings.appendTo).toBe(layer);
    expect(typeof widget.settings.position).toBe('function');
    expect(widget.calendar.calendarContainer.parentElement).toBe(layer);
    expect(widget.calendar.calendarContainer.style.pointerEvents).toBe('auto');
  });

  // Testzweck: Außerhalb eines Dialogs — etwa auf der Aufgabenseite — bleibt flatpickr bei
  // seinem Standard: Kalender am <body>, Lage nach Form.ios Vorgabe.
  it('lässt Kalender außerhalb von Dialogen unverändert', async () => {
    const input = document.createElement('input');
    document.body.appendChild(input);
    const widget = await attachedWidget(input);

    expect(document.querySelector(`.${CALENDAR_LAYER_CLASS}`)).toBeNull();
    expect(widget.settings.appendTo).toBeUndefined();
    expect(widget.settings.position).toBe('auto center');
    expect(widget.calendar.calendarContainer.parentElement).toBe(document.body);
  });

  // Testzweck: Form.ios eigener Einstellungsdialog trägt auch role="dialog", ist aber selbst eine
  // Bildlauffläche ohne Zeiger- und Fokussperre. Eine Ebene dort schnitte den Kalender ab; es
  // bleibt beim Standard am <body>, dessen z-index über dem Form.io-Dialog liegt.
  it('lässt Kalender in Form.ios Einstellungsdialog beim Standard', async () => {
    const formioDialog = document.createElement('div');
    formioDialog.className = 'formio-dialog-content';
    formioDialog.setAttribute('role', 'dialog');
    const input = document.createElement('input');
    formioDialog.appendChild(input);
    document.body.appendChild(formioDialog);

    const widget = await attachedWidget(input);

    expect(layersIn(formioDialog)).toHaveLength(0);
    expect(widget.settings.appendTo).toBeUndefined();
    expect(widget.settings.position).toBe('auto center');
    expect(widget.calendar.calendarContainer.parentElement).toBe(document.body);
  });

  // Testzweck: Beim Öffnen steht der Kalender unter dem Feld und im Dialog; rechts bündig mit
  // der Eingabegruppe, wenn er links bündig über den Dialog hinausragte. Die Werte sind auf die
  // Ebene umgerechnet, die über den verschobenen Dialog der Bezugsrahmen ist.
  it('setzt den Kalender beim Öffnen unter das Feld und in den Dialog', async () => {
    vi.spyOn(window, 'innerWidth', 'get').mockReturnValue(1280);
    vi.spyOn(window, 'innerHeight', 'get').mockReturnValue(800);
    const { dialog, group, input } = modalWithFields();
    const widget = await attachedWidget(input);

    const layer = dialog.querySelector(`.${CALENDAR_LAYER_CLASS}`) as HTMLElement;
    vi.spyOn(dialog, 'getBoundingClientRect').mockReturnValue(rect(380, 200, 520, 400));
    vi.spyOn(layer, 'getBoundingClientRect').mockReturnValue(rect(381, 201, 518, 398));
    vi.spyOn(group, 'getBoundingClientRect').mockReturnValue(rect(650, 300, 228, 36));
    size(widget.calendar.calendarContainer, 308, 300);

    widget.calendar.open();

    // Links bündig endete er bei 958 px, der Dialog bei 900 px: also rechts bündig (878 - 308).
    expect(widget.calendar.calendarContainer.style.left).toBe(`${570 - 381}px`);
    expect(widget.calendar.calendarContainer.style.top).toBe(`${338 - 201}px`);
  });

  // Testzweck: Form.io hängt eigene onOpen-/onClose-Haken an den Kalender (Ereignisse, Übernahme
  // getippter Werte). Das Mitlaufen des Widgets kommt hinzu und ersetzt sie nicht.
  it('behält Form.ios eigene Haken neben dem Mitlaufen', async () => {
    const { input } = modalWithFields();
    const widget = await attachedWidget(input);

    widget.calendar.open();
    widget.calendar.close();

    expect(widget.formioHooks.open).toHaveBeenCalledOnce();
    expect(widget.formioHooks.close).toHaveBeenCalledOnce();
    expect(widget.calendar.config.onOpen).toHaveLength(2);
    expect(widget.calendar.config.onClose).toHaveLength(2);
  });

  // Testzweck: Rollt der Dialoginhalt bei offenem Kalender, zieht der Kalender mit; nach dem
  // Schließen hört das Widget wieder auf zu lauschen.
  it('folgt dem Feld beim Rollen, solange der Kalender offen ist', async () => {
    const { dialog, scroller, group, input } = modalWithFields();
    const widget = await attachedWidget(input);
    const layer = dialog.querySelector(`.${CALENDAR_LAYER_CLASS}`) as HTMLElement;
    const fieldRect = desktopGeometry(dialog, scroller, group, layer);
    size(widget.calendar.calendarContainer, 308, 300);

    widget.calendar.open();
    expect(widget.calendar.calendarContainer.style.top).toBe(`${238 - 100}px`);

    fieldRect.mockReturnValue(rect(400, 190, 400, 36));
    scroller.dispatchEvent(new Event('scroll'));
    expect(widget.calendar.calendarContainer.style.top).toBe(`${228 - 100}px`);

    widget.calendar.close();
    fieldRect.mockReturnValue(rect(400, 185, 400, 36));
    scroller.dispatchEvent(new Event('scroll'));
    expect(widget.calendar.calendarContainer.style.top).toBe(`${228 - 100}px`);
  });

  // Testzweck: Rollt das Feld ganz aus dem sichtbaren Inhaltsbereich, klappt der Kalender zu —
  // sonst stünde er ohne Bezug über Kopf oder Fuß des Dialogs.
  it('schließt den Kalender, wenn das Feld aus dem sichtbaren Inhalt rollt', async () => {
    const { dialog, scroller, group, input } = modalWithFields();
    const widget = await attachedWidget(input);
    const layer = dialog.querySelector(`.${CALENDAR_LAYER_CLASS}`) as HTMLElement;
    const fieldRect = desktopGeometry(dialog, scroller, group, layer);
    size(widget.calendar.calendarContainer, 308, 300);
    widget.calendar.open();

    // Angeschnitten unter dem Kopf: noch sichtbar, der Kalender bleibt.
    fieldRect.mockReturnValue(rect(400, 160, 400, 36));
    scroller.dispatchEvent(new Event('scroll'));
    expect(widget.calendar.isOpen).toBe(true);

    // Ganz unter dem Kopf verschwunden (Inhaltsbereich beginnt bei 180 px).
    fieldRect.mockReturnValue(rect(400, 140, 400, 36));
    scroller.dispatchEvent(new Event('scroll'));
    expect(widget.calendar.isOpen).toBe(false);
  });

  // Testzweck: Verschiebt sich das Feld ohne Rollen — eine Prüfmeldung oder ein bedingtes Feld
  // erscheint darüber —, meldet das der ResizeObserver auf Dialog und Formular; der Kalender
  // folgt. Beim Schließen wird der Beobachter getrennt.
  it('folgt Größenänderungen im Dialog über einen ResizeObserver', async () => {
    const observers: Array<{ callback: () => void; observed: Element[]; disconnect: ReturnType<typeof vi.fn> }> = [];
    vi.stubGlobal('ResizeObserver', class {
      observed: Element[] = [];
      disconnect = vi.fn();
      callback: () => void;
      constructor(callback: () => void) {
        this.callback = callback;
        observers.push(this);
      }
      observe(element: Element) {
        this.observed.push(element);
      }
    });
    const { dialog, scroller, form, group, input } = modalWithFields();
    const widget = await attachedWidget(input);
    const layer = dialog.querySelector(`.${CALENDAR_LAYER_CLASS}`) as HTMLElement;
    const fieldRect = desktopGeometry(dialog, scroller, group, layer);
    size(widget.calendar.calendarContainer, 308, 300);

    widget.calendar.open();
    expect(observers).toHaveLength(1);
    expect(observers[0]?.observed).toEqual([dialog, form]);

    fieldRect.mockReturnValue(rect(400, 240, 400, 36));
    observers[0]?.callback();
    expect(widget.calendar.calendarContainer.style.top).toBe(`${278 - 100}px`);

    widget.calendar.close();
    expect(observers[0]?.disconnect).toHaveBeenCalled();
  });

  // Testzweck: Wird das Feld bei offenem Kalender abgebaut — Form.io zeichnet Felder neu,
  // der Dialog schließt —, endet auch das Lauschen am Dokument; nichts ruft danach noch die
  // Positionierung eines abgebauten Kalenders auf.
  it('entfernt den Scroll-Zuhörer beim Abbau bei offenem Kalender', async () => {
    const { dialog, scroller, group, input } = modalWithFields();
    const widget = await attachedWidget(input);
    const layer = dialog.querySelector(`.${CALENDAR_LAYER_CLASS}`) as HTMLElement;
    desktopGeometry(dialog, scroller, group, layer);
    const removeListener = vi.spyOn(document, 'removeEventListener');
    widget.calendar.open();
    const calls = widget.calendar.positionCalls;

    widget.destroy();
    scroller.dispatchEvent(new Event('scroll'));

    expect(removeListener).toHaveBeenCalledWith('scroll', expect.any(Function), true);
    expect(widget.calendar.positionCalls).toBe(calls);
    expect(layersIn(dialog)).toHaveLength(0);
  });

  // Testzweck: Form.io lädt flatpickr nach und ruft initFlatpickr auch dann noch auf, wenn das
  // Feld schon abgebaut ist. Dann darf kein verwaister Kalender am <body> entstehen.
  it('baut nach dem Abbau keinen Kalender mehr auf', async () => {
    const { dialog, input } = modalWithFields();
    const Widget = registry().calendar as unknown as new () => TestWidget;
    const widget = new Widget();
    await widget.attach(input);

    widget.destroy();
    widget.initFlatpickr(FakeFlatpickr);

    expect(FakeFlatpickr.created).toHaveLength(0);
    expect(widget.calendar).toBeNull();
    expect(document.querySelector('.flatpickr-calendar')).toBeNull();
    expect(layersIn(dialog)).toHaveLength(0);
  });

  // Testzweck: Ein erneutes attach() räumt Ebene und Zuhörer des vorigen Aufbaus weg, statt
  // eine zweite Ebene anzulegen und den alten Kalender weiter mitlaufen zu lassen.
  it('räumt bei erneutem attach() Ebene und Zuhörer des vorigen Aufbaus weg', async () => {
    const { dialog, scroller, group, input } = modalWithFields();
    const widget = await attachedWidget(input);
    const layer = dialog.querySelector(`.${CALENDAR_LAYER_CLASS}`) as HTMLElement;
    desktopGeometry(dialog, scroller, group, layer);
    widget.calendar.open();
    const oldCalendar = widget.calendar;
    const calls = oldCalendar.positionCalls;

    await widget.attach(input);
    scroller.dispatchEvent(new Event('scroll'));

    expect(oldCalendar.positionCalls).toBe(calls);
    expect(layersIn(dialog)).toHaveLength(1);
    expect(layersIn(dialog)[0]).not.toBe(layer);
  });

  // Testzweck: Zwei Datumsfelder im selben Dialog (erster und letzter Urlaubstag) haben je eine
  // eigene Ebene. Das Öffnen des einen setzt nur dessen Kalender; der Abbau des einen lässt den
  // anderen stehen.
  it('hält zwei Datumsfelder im selben Dialog auseinander', async () => {
    const { dialog, scroller, fields } = modalWithFields(2);
    const widgets = registry();
    const first = await attachedWidget(fields[0]!.input, widgets);
    const second = await attachedWidget(fields[1]!.input, widgets);
    const layers = layersIn(dialog);
    expect(layers).toHaveLength(2);
    expect(first.calendar.calendarContainer.parentElement).toBe(layers[0]);
    expect(second.calendar.calendarContainer.parentElement).toBe(layers[1]);

    desktopGeometry(dialog, scroller, fields[0]!.group, layers[0]!);
    vi.spyOn(layers[1]!, 'getBoundingClientRect').mockReturnValue(rect(380, 100, 520, 600));
    vi.spyOn(fields[1]!.group, 'getBoundingClientRect').mockReturnValue(rect(400, 260, 400, 36));
    second.calendar.open();

    expect(second.calendar.calendarContainer.style.top).toBe(`${298 - 100}px`);
    expect(first.calendar.calendarContainer.style.top).toBe('');

    first.destroy();
    expect(layersIn(dialog)).toHaveLength(1);
    expect(second.calendar.calendarContainer.isConnected).toBe(true);
  });

  // Testzweck: Beim Abbau des Feldes verschwindet auch die Ebene im Dialog — Form.io baut Felder
  // bei jedem Neuzeichnen neu auf, sonst sammelten sich leere Ebenen an.
  it('räumt die Ebene beim Abbau weg', async () => {
    const { dialog, input } = modalWithFields();
    const widget = await attachedWidget(input);

    widget.destroy();

    expect(layersIn(dialog)).toHaveLength(0);
  });
});

describe('placeCalendar', () => {
  const viewport = { width: 1280, height: 800 };
  const dialog = { left: 380, right: 900, top: 150, bottom: 650 };
  const calendar = { width: 308, height: 300 };

  // Testzweck: Mit Platz steht der Kalender links bündig direkt unter dem Feld.
  it('setzt den Kalender links bündig unter das Feld', () => {
    const field = { left: 402, right: 878, top: 300, bottom: 336 };
    expect(placeCalendar(field, calendar, dialog, viewport)).toEqual({ top: 338, left: 402, above: false });
  });

  // Testzweck: Reicht der Platz unter dem Feld nicht, steht der Kalender darüber.
  it('weicht nach oben aus, wenn unten kein Platz ist', () => {
    const field = { left: 402, right: 878, top: 600, bottom: 636 };
    expect(placeCalendar(field, calendar, dialog, viewport)).toEqual({ top: 298, left: 402, above: true });
  });

  // Testzweck: Auf Telefonbreite ist der Dialog schmaler als der Kalender. Er darf dann über den
  // Dialog hinaus, bleibt aber mit Abstand im Fenster.
  it('bleibt auf Telefonbreite im Fenster, auch wenn der Dialog schmaler ist', () => {
    const phone = { width: 375, height: 812 };
    const narrowDialog = { left: 16, right: 316, top: 100, bottom: 700 };
    const field = { left: 200, right: 300, top: 300, bottom: 336 };

    const placement = placeCalendar(field, calendar, narrowDialog, phone);

    expect(placement.left).toBeGreaterThanOrEqual(8);
    expect(placement.left + calendar.width).toBeLessThanOrEqual(phone.width - 8);
  });

  // Testzweck: Ist oben mehr Platz als unten, reicht er aber auch dort nicht, steht der Kalender
  // über dem Feld und wird an den oberen Fensterrand geklemmt — er überdeckt dann das Feld
  // teilweise, ragt aber nicht aus dem Bild.
  it('klemmt den Kalender oben an, wenn er auch über dem Feld nicht passt', () => {
    const low = { width: 1280, height: 400 };
    const field = { left: 402, right: 878, top: 250, bottom: 286 };

    expect(placeCalendar(field, calendar, dialog, low)).toEqual({ top: 8, left: 402, above: true });
  });

  // Testzweck: Ist der Kalender höher als das Fenster, gewinnt die Oberkante: Monat und
  // Blätterpfeile bleiben sichtbar, abgeschnitten wird unten.
  it('hält bei zu niedrigem Fenster die Oberkante des Kalenders im Bild', () => {
    const tiny = { width: 1280, height: 250 };
    const field = { left: 402, right: 878, top: 100, bottom: 136 };

    expect(placeCalendar(field, calendar, dialog, tiny).top).toBe(8);
  });

  // Testzweck: Ist das Fenster schmaler als der Kalender, gewinnt die linke Kante — der Anfang
  // der Wochentagszeile bleibt sichtbar.
  it('hält bei zu schmalem Fenster die linke Kante des Kalenders im Bild', () => {
    const narrow = { width: 300, height: 800 };
    const field = { left: 150, right: 280, top: 100, bottom: 136 };

    expect(placeCalendar(field, calendar, { left: 8, right: 292, top: 50, bottom: 700 }, narrow).left).toBe(8);
  });
});

describe('isOutside', () => {
  const area = { top: 180, left: 380, right: 900, bottom: 620 };

  // Testzweck: Nur ein ganz verschwundenes Feld gilt als außerhalb; ein angeschnittenes nicht.
  it('unterscheidet angeschnittene von verschwundenen Feldern', () => {
    expect(isOutside({ top: 160, bottom: 196, left: 400, right: 800 }, area)).toBe(false);
    expect(isOutside({ top: 140, bottom: 176, left: 400, right: 800 }, area)).toBe(true);
    expect(isOutside({ top: 620, bottom: 656, left: 400, right: 800 }, area)).toBe(true);
  });
});
