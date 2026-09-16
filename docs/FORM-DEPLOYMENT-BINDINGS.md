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

Ab `flowzer.forms/2` gehören auch die Policy und stabilen Filterreferenzen eines
`flowzerSubject`-Felds zu diesem Snapshot. Das Deployment prüft sie gegen den aktiven
Directory-Stand; Details: [Benutzer-/Gruppenauswahl](FORM-DIRECTORY-FIELD.md).

## Historischer Bestand

Seit Stabilisierung #297 übernimmt der PostgreSQL-Updateschritt fehlende Bindungen
für veröffentlichte historische Definitionen automatisch und transaktional. Eine
explizite Version oder genau eine vorhandene Veröffentlichung ist zulässig;
mehrere mögliche Versionen stoppen die technische Update-Vorprüfung. Es wird
niemals stillschweigend die neueste Fassung gewählt. Vorhandene Snapshots,
Originalformulare und Instanzen bleiben unverändert. Wiederholte Updates sind
idempotent; ein Fehler rollt die gesamte Bindungsübernahme zurück.

Historisch im BPMN eingebettete Formulare werden aus genau ihrer Definition gebunden.
Die bekannten ausgelieferten Urlaubsformularregeln erhalten einen verlustfreien
Leseadapter; unbekanntes JavaScript wird weder ausgeführt noch entfernt. Nutzende
müssen keine Migrationsansicht bearbeiten. Details und Betriebsgrenzen:
[Betriebsanleitung](OPERATIONS.md).

## Grenzen und Tests

Dieser Slice ist noch kein gemeinsamer Formular-Validierungsvertrag. Pflichtwerte,
Geschäftsregeln, Eingabe-/Ausgabefelder und serverseitige Abschlussprüfung ergänzt
der Folgeslice #183 als [begrenztes Prüfprofil](FORM-VALIDATION-PROFILE.md), einschließlich
einer Vertragsprüfung bei Wiederaktivierung. Dateiablage bleibt Entwicklung; keine Mehrprozessgarantie.

TDD prüft Versionswechsel, Umbenennung, später aktivierte Aufgaben, Startformulare,
fehlgeschlagene Bindung ohne Verlust der aktiven Definition, persistierten Snapshot
und explizites Legacy-Verhalten. Externe Reviews sind aktuell ausdrücklich ausgesetzt.
