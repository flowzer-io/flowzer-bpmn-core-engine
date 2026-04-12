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

### 3. Timer-Subscriptions sind jetzt als Runtime-Vertrag persistiert

- Timer-Subscriptions werden jetzt analog zu anderen Runtime-Subscriptions in Storage und Web-API abgelegt.
- `BpmnBusinessLogic` speichert aktive Start- und Intermediate-Timer jetzt als `TimerSubscription`.
- `GET /timer` und `GET /instance/{instanceId}/subscription/timers` machen die aktiven Timer im API-Pfad sichtbar.

### 4. Ein kleiner Scheduler-/Polling-Pfad ist jetzt vorhanden

- Die Web-API startet jetzt einen Hintergrunddienst, der fällige Timer regelmäßig über `HandleTime(...)` verarbeitet.
- Beim Start werden persistierte Instanz-Timer erneut aus den gespeicherten Tokenzuständen synchronisiert.
- Das Poll-Intervall ist über `TimerScheduler:PollIntervalSeconds` konfigurierbar.

### 5. Boundary-Timer laufen jetzt über denselben Timer-Subscription-Pfad

- `ModelParser` erkennt Boundary-Timer jetzt als eigenes Flowzer-Ereignis.
- aktive Boundary-Timer werden über `ICatchHandler.ActiveTimerSubscriptions` sichtbar.
- fällige Boundary-Timer werden im `HandleTime(...)`-Pfad genau einmal ausgelöst.
- interrupting Boundary-Timer ziehen die Aktivität zurück, non-interrupting Boundary-Timer starten einen parallelen Pfad.

### 6. Wiederkehrende Start-Timer und Start-Timer-Recovery sind jetzt als Runtime-Pfad vorhanden

- begrenzte `timeCycle`-Definitionen wie `R3/PT2S` werden jetzt mit `RemainingOccurrences` als Teil der Timer-Subscription persistiert.
- `ProcessEngine.HandleTime(...)` zieht überfällige Start-Timer jetzt mehrfach nach, bis wieder ein zukünftiger Fälligkeitszeitpunkt erreicht ist.
- `BpmnBusinessLogic.HandleTime(...)` reschedult wiederkehrende Start-Timer im Storage-Pfad konsistent weiter.
- `BpmnBusinessLogic.Load()` kann überfällige Start-Timer nach einem Neustart direkt wieder anstoßen und auf den nächsten Due-Zeitpunkt vorschieben.

### 7. User-Task-Ergebnisse haben einen stabileren Laufzeitvertrag

- User-Task-Ergebnisse ohne `ProcessInstanceId` laufen nicht mehr in eine rohe `NotImplementedException`.
- Stattdessen kommt ein valider `400 Bad Request` mit einem klaren API-Fehlervertrag zurück.
- Zusätzlich wird jetzt geprüft, ob das übergebene `TokenId` wirklich noch aktiv ist und zum erwarteten `FlowNodeId` gehört.

### 8. Instanzabbruch ist als Best-Effort-Pfad verfügbar

- `InstanceEngine.Cancel()` terminiert jetzt aktive/wartende Tokens der Instanz konsistent.
- Das ersetzt noch **keine vollständige BPMN-Kompensation**, verhindert aber, dass der API-/Runtime-Pfad an einer nackten `NotImplementedException` scheitert.

## Weiterhin bewusst offen

### 1. Timer-Ausführung und Persistenz

Aktuell vorhanden:

- Timer-Fälligkeiten für Start- und laufende Instanzen
- Timer-Catch-Events als wartende Zustände
- einmaliger Engine-Kernpfad für fällige Timer-Starts und Intermediate-Timer
- persistierte Timer-Subscriptions in Storage und Web-API
- kleiner Scheduler-/Polling-Pfad rund um `HandleTime(...)`
- wiederkehrende Start-Timer mit Catch-up und verbleibenden Wiederholungen
- Startup-Recovery für überfällige Start-Timer

Weiterhin offen:

- Recovery-Strategie für bereits persistierte Boundary- oder Spezialtimer über harte Neustarts hinweg weiter schärfen
- weitergehende Wiederholungsstrategie für Spezialfälle jenseits des aktuellen Start-Timer-Pfads
- Sonderfälle wie konkurrierende Timer oder weitergehende Boundary-Timer-Recovery nur bei echtem Bedarf vertiefen

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

1. Recovery- und Wiederholungsstrategie nur noch für Boundary- und Spezialtimer weiter härten
2. Error-/Escalation-Semantik gezielt modellieren und testen
3. `Cancel()` später um echte Kompensationsstrategien erweitern
4. Boundary-Timer nur noch bei echten Randfällen weiter vertiefen
