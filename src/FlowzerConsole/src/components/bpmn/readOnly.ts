/**
 * Macht den bpmn-js-Modeler zur reinen Ansicht.
 *
 * Ein `NavigatedViewer` haette dieselbe Wirkung, brauchte aber zwei Bauarten desselben
 * Bauteils: Der Griff nach aussen (`undo`, `redo`) haengt am `commandStack`, und
 * `bpmnEditor` holt sich `modeling` und `bpmnFactory` aus dem Injector — beides gibt es in
 * einem Viewer nicht. Der Modeler bleibt deshalb stehen und verliert stattdessen alles,
 * womit sich das Modell aendern liesse.
 *
 * Die Entscheidung faellt weiterhin die API: Speichern und Deployen verlangen dort die
 * Modelliererrolle. Diese Sperre sorgt nur dafuer, dass die Oberflaeche gar nicht erst zu
 * Aenderungen einlaedt, die sie anschliessend nicht loswird.
 */

interface EventBusLike {
  on: (event: string, callback: () => unknown) => void;
}

/**
 * Hält das Kontextpad geschlossen.
 *
 * Ohne Anbieter bliebe es zwar leer, diagram-js öffnete aber trotzdem eine leere
 * Sprechblase am gewählten Element — `ContextPad._updateAndOpen` prüft nicht, ob
 * überhaupt Einträge zusammengekommen sind.
 */
function keepContextPadClosed(eventBus: EventBusLike): void {
  eventBus.on('contextPad.open.allowed', () => false);
}

keepContextPadClosed.$inject = ['eventBus'];

/**
 * Zusatzmodul für den Modeler, das jede Änderung am Diagramm abstellt. Auswahl, Zoom und
 * Verschieben der Zeichenfläche bleiben erhalten — nur so ist ein großes Diagramm lesbar.
 */
export const READ_ONLY_MODULE: Record<string, unknown> = {
  __init__: ['flowzerReadOnly'],
  flowzerReadOnly: ['type', keepContextPadClosed],

  /**
   * Verschieben, Anlegen, Verbinden, Löschen, Größe ändern, Stützpunkte ziehen: Jede
   * dieser Aktionen fragt zuerst `rules.allowed`. Eine Regelquelle, die nichts erlaubt,
   * schaltet sie deshalb an einer einzigen Stelle gemeinsam ab — und mit ihnen die
   * Griffe, die diagram-js nur zeigt, solange die Aktion erlaubt wäre.
   */
  rules: ['value', { allowed: () => false }],

  /**
   * Ohne Anbieter zeichnet diagram-js die Palette gar nicht erst: `Palette._rebuild`
   * bricht ab, solange sich kein Anbieter gemeldet hat. Es bleibt also keine leere
   * Leiste stehen.
   */
  paletteProvider: ['value', null],

  /** Kein Anbieter, kein Beschriftungseditor beim Doppelklick auf ein Element. */
  labelEditingProvider: ['value', null],
};
