# Unveränderliche Formularbindungen

M0/M2-Teilpaket #180, aufbauend auf PR #179.

## Vertrag

Beim Workflow-Deployment werden die referenzierten Start- und Aufgabenformulare
auf konkrete Formularstände aufgelöst. Ein Snapshot aus Inhalt, Formular-ID und
Versions-ID gehört anschließend unveränderlich zur Definitionsversion. Die
ursprünglichen Form-Keys bleiben für Modellierung und Darstellung erhalten.
Später aktivierte Aufgaben verwenden denselben Stand, nicht den dann neuesten.

Alle Bindungen müssen vor dem Austausch der bisherigen Start-Subscriptions
vollständig erfolgreich sein. Eine neue Formularversion wird erst durch eine neue
Workflow-Version wirksam. Eine erneute Aktivierung derselben Definitionsversion
ändert ihre Bindungen nicht.

## Historischer Bestand

Ein externes Formular ohne historisch gespeicherten Stand lässt sich nicht
rückwirkend beweisbar rekonstruieren. Solche Laufzeitreferenzen müssen einen
verständlichen Klärungsbedarf anzeigen, statt automatisch die heutige Fassung
anzunehmen. Neue Instanzen erhalten einen neu deployten Workflow; laufende
Altinstanzen benötigen eine ausdrücklich geprüfte Zuordnung in einem späteren
Migrationspaket. Es erfolgt hier keine produktive Migration.

Historisch im BPMN eingebettete Formulare bleiben an ihre konkrete Definition
gebunden und müssen nicht gegen einen externen Formularbestand aufgelöst werden.

## Grenzen und Tests

Dieser Slice ist noch kein gemeinsamer Formular-Validierungsvertrag. Pflichtwerte,
Geschäftsregeln, Eingabe-/Ausgabefelder und serverseitige Abschlussprüfung ergänzt
der Folgeslice #183 als [begrenztes Prüfprofil](FORM-VALIDATION-PROFILE.md), einschließlich
einer Vertragsprüfung bei Wiederaktivierung. Dateiablage bleibt Entwicklung; keine Mehrprozessgarantie.

TDD prüft Versionswechsel, Umbenennung, später aktivierte Aufgaben, Startformulare,
fehlgeschlagene Bindung ohne Verlust der aktiven Definition, persistierten Snapshot
und explizites Legacy-Verhalten. Externe Reviews sind aktuell ausdrücklich ausgesetzt.
