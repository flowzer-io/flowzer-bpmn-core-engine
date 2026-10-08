import flatpickr from 'flatpickr-formio';
import { German } from 'flatpickr-formio/dist/l10n/flatpickr-de';
import 'flatpickr-formio/dist/flatpickr.min.css';
import moment from 'moment-timezone';

/** Nur die SDK-Oberfläche, die der offline eingebettete Renderer tatsächlich benötigt. */
export interface OpaqueFormioRuntime {
  Evaluator: { noeval: boolean };
  getUser: (options?: unknown) => unknown;
  getToken: (options?: unknown) => unknown;
  libraries: Record<string, { ready: Promise<unknown> }>;
  requireLibrary: (name: string, ...arguments_: unknown[]) => Promise<unknown>;
}

/**
 * Initialisiert ausschließlich das separate opaque-origin-Einbettungsbundle.
 *
 * Form.io liest auch bei rein lokalen Formularen seine Browseridentität und fällt
 * von gesperrtem localStorage auf document.cookie zurück. Im Frame gibt es bewusst
 * keine solche Identität: Der authentifizierte Host vermittelt jede Operation.
 * Wir entfernen diese unnötigen SDK-Lesezugriffe, statt Cookiezugriff, Storage oder
 * allow-same-origin zu erlauben. Nicht im normalen Console-Einstieg verwenden.
 */
export function initializeOpaqueFormioRuntime(runtime: OpaqueFormioRuntime): void {
  if (self.origin !== 'null') {
    throw new Error('Der Einbettungsrenderer benötigt eine isolierte Sandbox.');
  }
  runtime.Evaluator.noeval = true;
  runtime.getUser = () => null;
  runtime.getToken = () => '';
  // Dieselbe feste Version, die Form.io ansonsten vom CDN laden würde. Die
  // veröffentlichte Library-Registry verhindert externe JS-/CSS-Nachladungen;
  // Deutsch und Englisch sind die einzigen Sprachen des Flowzer-Renderers.
  flatpickr.l10ns.de = German;
  runtime.libraries.flatpickr = { ready: Promise.resolve(flatpickr) };
  runtime.libraries['flatpickr-css'] = { ready: Promise.resolve() };
  runtime.libraries['flatpickr-de'] = { ready: Promise.resolve(German) };
  const bundled = new Set(['flatpickr', 'flatpickr-css', 'flatpickr-de']);
  runtime.requireLibrary = (name: string) => bundled.has(name)
    ? runtime.libraries[name]!.ready
    : Promise.reject(new Error('Die angeforderte Formularbibliothek ist nicht gebündelt.'));
  // Der vollständige gepinnte Datensatz steckt schon im lokalen Moment-Bundle.
  // Form.io kennt diesen Ladezustand sonst nicht und fetchte trotz vorhandenem
  // Datensatz vom CDN. Keine entfernte "latest"-Zeitzonendatei im Frame.
  (moment as unknown as { zonesLoaded: boolean }).zonesLoaded = true;
}
