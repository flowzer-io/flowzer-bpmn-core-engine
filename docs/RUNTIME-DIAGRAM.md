# Objektberechtigtes Laufzeitdiagramm

**Stand:** 9. September 2026  
**Issue/PR:** #232 / #233

## Zweck und Sicherheitsgrenze

`GET /instance/{instanceId}/runtime-diagram` liefert dem ausdrücklich berechtigten
Betrieb eine gemeinsame Projektion aus:

- der exakt beim Instanzstart gebundenen Definitions-ID und Process-ID,
- einem für die Darstellung bereinigten BPMN-Dokument,
- dem aktuellen, nach Knoten verdichteten Laufzeitstatus und
- einer append-only gespeicherten Engine-Ereignisspur.

Der Endpunkt verwendet dieselbe objektbezogene Operatorprüfung wie die technischen
Subscription-Wege. Fremde, fehlende oder inkonsistente Ressourcen antworten
einheitlich mit `404` und Problem Details. Antragsteller, aktuelle Bearbeiter,
Modellierer und Worker erhalten dadurch nicht automatisch technische Diagnose-
rechte.

Die öffentliche Projektion enthält keine Token- oder Korrelations-IDs, Akteure,
Prozessvariablen, Formularwerte oder Worker-Ergebnisse. Aus dem BPMN-Dokument werden
Erweiterungselemente, Dokumentation, Bedingungen, Skripte, Textanmerkungsinhalte,
Datenobjekte und herstellerspezifische Attribute entfernt. Bei einer später
deployed neuen Version gibt es keinen Fallback auf `latest`.

## Persistenz

Jede Engine-Persistenzgrenze schreibt für die zu diesem Zeitpunkt tatsächlich
sichtbaren Flow-Node-Tokenzustände einen datensparsamen Fakt. Kurzlebige interne
Zwischenzustände zwischen zwei Persistenzgrenzen werden nicht rekonstruiert oder
erfunden. Eine deterministisch aus Instanz, Definition, Token, Knoten, Zustand und
persistiertem Zeitpunkt abgeleitete Ereignis-ID macht Wiederholungen idempotent.

PostgreSQL speichert Instanz, Aufgaben/Jobs und Laufzeitereignisse in derselben
Transaktion. Die Migration `012_runtime_node_events.sql` legt den append-only
Index nach Instanz und Zeit an. Die Dateiablage bewahrt Idempotenz innerhalb ihres
dokumentierten Einzelprozess-Entwicklungsbetriebs, bleibt aber kein freigegebener
Mehrprozesspfad.

## Statusprojektion

Für jeden Token und Knoten zählt der jüngste gespeicherte Zustand. Parallele oder
mehrfache Ausführungen desselben Knotens werden mit Tokenanzahl verdichtet. Die
Anzeigepriorität ist:

1. gestört,
2. aktiv,
3. abgebrochen,
4. abgeschlossen.

Die Konsole zeigt diese Zustände im BPMN-Diagramm, in einer tastaturbedienbaren
semantischen Knotenliste und in der echten Ereigniszeitleiste. Farbe ist nicht das
einzige Signal. Eine lineare Anzeige „Schritt x von y“ wurde entfernt, weil offene
Verzweigungen und Schleifen keinen belastbaren Nenner besitzen.

## Öffentliche Nutzung

`@flowzer/sdk` stellt `instances.runtimeDiagram(...)` bereit. `@flowzer/react`
ergänzt den fail-closed Hook `useInstanceRuntimeDiagram` und einen darstellungsfreien
Render-Prop-Controller. Installation und Sitzung bleiben explizite Cache-Scopes;
bei einem Rechteentzug verschwindet die Projektion aus Observer und Cache.

Die Verträge sind vollständig hostneutral. Flowzer enthält keine Kenntnis einer
konkreten konsumierenden Anwendung; externe Produkte binden ausschließlich die
öffentliche API beziehungsweise die optionalen Pakete von außen ein.

## Bewusste Grenzen

- Die Engine-Spur ist keine Event-Sourcing-Grundlage und ersetzt den gespeicherten
  Instanzzustand nicht.
- Frühere Instanzen besitzen vor ihrem ersten erneuten Persistieren noch keine
  rückwirkend erfundene Ereignisspur.
- Pagination, langfristige Aufbewahrung und Ereignisarchivierung folgen erst mit
  realen Volumenmessungen.
- Fachliche Human-Task-Aktionen bleiben als getrennte, datensparsame Historie unter
  `/instance/{id}/history` erhalten.
