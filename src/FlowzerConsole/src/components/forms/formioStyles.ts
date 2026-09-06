/**
 * Sammelstelle für alle Stilblätter rund um Form.io.
 *
 * Der Grund ist die Reihenfolge. Renderer und Editor brauchen unterschiedlich viele
 * Blätter; importierte jeder für sich, entschied die Ladereihenfolge der Module, welches
 * am Ende steht. Konkret: Der Renderer lud `formio.form.css` und danach `formio.css`, der
 * Editor zog beim Öffnen `formio.builder.css` nach — also **nach** unseren Anpassungen.
 * Bei gleicher Spezifität gewinnt das zuletzt geladene Blatt, und der Eigenschaftendialog
 * stand wieder in seinem Ausgangsgrau statt in den Farben der Konsole.
 *
 * Hier liegt die Reihenfolge fest: erst Form.io, dann die Symbolschrift, zuletzt unsere
 * Anpassungen. Beide Komponenten importieren nur noch dieses Modul.
 */
import '@formio/js/dist/formio.form.min.css';
import '@formio/js/dist/formio.builder.min.css';
// Die Vorlagen von @formio/bootstrap geben Symbole als <i class="bi bi-…"> aus
// (`defaultIconset: "bi"`). Ohne diese Schrift bleibt jede Schaltfläche des Editors leer.
import 'bootstrap-icons/font/bootstrap-icons.css';
import './formio.css';
