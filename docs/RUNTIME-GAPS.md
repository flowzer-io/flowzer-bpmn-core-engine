# Laufzeitlücken und aktueller Restbestand

**Stand:** 12. April 2026

Dieses Dokument hält die aktuell noch offenen Laufzeit- und Engine-Lücken fest, damit `next` nicht nur "grün", sondern auch fachlich ehrlich bleibt.

## In diesem Strang bereits geschlossen

### 1. Timer-Catch-Events blockieren die Engine nicht mehr sofort

- `FlowzerIntermediateTimerCatchEvent` wird jetzt wie andere wartende Catch-Events als aktiver Wartezustand behandelt.
- Laufende Instanzen können ihre aktiven Timertermine jetzt über `ICatchHandler.ActiveTimers` offenlegen.
- `timeDuration` wird bei der Fälligkeitsberechnung jetzt genauso berücksichtigt wie `timeCycle` und `timeDate`.

### 2. Timer können im Engine-Kern jetzt fälligkeitsbasiert weiterlaufen

- `ProcessEngine.HandleTime(...)` kann fällige Timer-Start-Events jetzt einmalig in neue Instanzen überführen.
- `InstanceEngine.HandleTime(...)` kann fällige `FlowzerIntermediateTimerCatchEvent`-Tokens jetzt weiterführen.
- Die zugehörigen Engine-Regressionstests decken Start- und Intermediate-Timer jetzt explizit ab.

### 3. User-Task-Ergebnisse haben einen stabileren Laufzeitvertrag

- User-Task-Ergebnisse ohne `ProcessInstanceId` laufen nicht mehr in eine rohe `NotImplementedException`.
- Stattdessen kommt ein valider `400 Bad Request` mit einem klaren API-Fehlervertrag zurück.
- Zusätzlich wird jetzt geprüft, ob das übergebene `TokenId` wirklich noch aktiv ist und zum erwarteten `FlowNodeId` gehört.

### 4. Instanzabbruch ist als Best-Effort-Pfad verfügbar

- `InstanceEngine.Cancel()` terminiert jetzt aktive/wartende Tokens der Instanz konsistent.
- Das ersetzt noch **keine vollständige BPMN-Kompensation**, verhindert aber, dass der API-/Runtime-Pfad an einer nackten `NotImplementedException` scheitert.

## Weiterhin bewusst offen

### 1. Timer-Ausführung und Persistenz

Aktuell vorhanden:

- Timer-Fälligkeiten für Start- und laufende Instanzen
- Timer-Catch-Events als wartende Zustände
- einmaliger Engine-Kernpfad für fällige Timer-Starts und Intermediate-Timer

Weiterhin offen:

- persistierte Timer-Subscriptions in Storage/API
- Wiederaufnahme nach Neustart nur auf Basis gespeicherter Timerzustände
- Scheduler-/API-Anbindung rund um `HandleTime(...)`
- Boundary-Timer-Parsing und -Abarbeitung
- vollständige Wiederholungsstrategie für zyklische Start-Timer über den ersten Due-Zeitpunkt hinaus

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
