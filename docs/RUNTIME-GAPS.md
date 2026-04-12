# Laufzeitlücken und aktueller Restbestand

**Stand:** 12. April 2026

Dieses Dokument hält die aktuell noch offenen Laufzeit- und Engine-Lücken fest, damit `next` nicht nur "grün", sondern auch fachlich ehrlich bleibt.

## In diesem Strang bereits geschlossen

### 1. Timer-Catch-Events blockieren die Engine nicht mehr sofort

- `FlowzerIntermediateTimerCatchEvent` wird jetzt wie andere wartende Catch-Events als aktiver Wartezustand behandelt.
- Laufende Instanzen können ihre aktiven Timertermine jetzt über `ICatchHandler.ActiveTimers` offenlegen.
- `timeDuration` wird bei der Fälligkeitsberechnung jetzt genauso berücksichtigt wie `timeCycle` und `timeDate`.

### 2. User-Task-Ergebnisse haben einen stabileren Laufzeitvertrag

- User-Task-Ergebnisse ohne `ProcessInstanceId` laufen nicht mehr in eine rohe `NotImplementedException`.
- Stattdessen kommt ein valider `400 Bad Request` mit einem klaren API-Fehlervertrag zurück.
- Zusätzlich wird jetzt geprüft, ob das übergebene `TokenId` wirklich noch aktiv ist und zum erwarteten `FlowNodeId` gehört.

### 3. Instanzabbruch ist als Best-Effort-Pfad verfügbar

- `InstanceEngine.Cancel()` terminiert jetzt aktive/wartende Tokens der Instanz konsistent.
- Das ersetzt noch **keine vollständige BPMN-Kompensation**, verhindert aber, dass der API-/Runtime-Pfad an einer nackten `NotImplementedException` scheitert.

## Weiterhin bewusst offen

### 1. Timer-Ausführung und Persistenz

Aktuell sichtbar bzw. teilweise vorbereitet:

- Timer-Fälligkeiten für Start- und laufende Instanzen
- Timer-Catch-Events als wartende Zustände

Weiterhin offen:

- persistierte Timer-Subscriptions in Storage/API
- Wiederaufnahme nach Neustart nur auf Basis gespeicherter Timerzustände
- tatsächliches `HandleTime(...)` bzw. Scheduler-Anbindung
- Boundary-Timer-Parsing und -Abarbeitung

### 2. Fehler- und Eskalationspfade

Noch nicht produktionsreif umgesetzt:

- Error Boundary Events
- Escalation Catch/Throw
- fachlich sinnvolle Fehlerpropagation auf BPMN-Ebene statt nur auf Ausnahmepfad

Aktueller Status:

- `GetActiveEscalations()` liefert stabil eine leere Liste statt sofort zu scheitern
- `HandleEscalation(...)` und `HandleError(...)` führen jetzt mindestens in einen kontrollierten Best-Effort-Fehlerzustand statt in eine rohe `NotImplementedException`
- echte Eskalations- und Fehlersemantik bleibt weiterhin ein separates Folgepaket

### 3. Vollständige Kompensation bei Abbruch

`Cancel()` ist aktuell eine **Best-Effort-Terminierung**:

- aktive und wartende Tokens werden beendet
- offene Interaktionen verschwinden aus dem aktiven Zustand

Nicht enthalten:

- Rückabwicklung bereits durchlaufener Activities
- BPMN-Kompensationshandler
- fachliche Undo-Semantik für Seiteneffekte

## Empfohlene nächste Runtime-Schritte

1. Timer-Storage + Scheduler-Pfad ergänzen
2. Boundary-Timer parsen und persistierbar machen
3. Error-/Escalation-Semantik gezielt modellieren und testen
4. `Cancel()` später um echte Kompensationsstrategien erweitern
