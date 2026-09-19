# Laufzeitlücken und aktueller Restbestand

**Stand:** 19. September 2026

Dieses Dokument hält die aktuell noch offenen Laufzeit- und Engine-Lücken fest, damit `main` nicht nur "grün", sondern auch fachlich ehrlich bleibt.

## Fehlerereignisse: Error End und Error Boundary

Fachliche Fehler laufen jetzt auf BPMN-Ebene weiter, statt die Instanz nur auf `Failed` zu
setzen. Die Semantik steht in [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md) (Vertrag 5), der
Worker-Weg in [SERVICE-TASK-WORKER.md](SERVICE-TASK-WORKER.md). Escalation und Kompensation
bleiben ausdrücklich offen.

## Korrektur #310: Manual Tasks und Timerdiagnose

Manual Tasks durchlaufen wie generische Tasks ohne Wartezustand den Sequenzfluss.
Die Fähigkeitsmatrix v4 erlaubt sie bei neuer Veröffentlichung; historische Verträge
und gespeicherte Definitionen bleiben unverändert. Regressionen prüfen normalen
Start und einmaliges Nachholen eines überfälligen Timer-Starts nach Neustart.

Fehler einzelner Timer führen nun zu einem fehlgeschlagenen Scheduler-Tick. Nur
solche klassifizierten Einzelfehler werden beim Hochlauf toleriert, damit die API
für Diagnose erreichbar bleibt. Wiederherstellungs-/Commitfehler bleiben fatal.
Mehrprozessschutz, transaktionsweise Isolation einzelner Timer und begrenztes
Nachholen wiederkehrender Timer bleiben offene Arbeiten aus #93.

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

### 9. Standardflüsse an exklusiven Gateways funktionieren

- `ExclusiveGateway` implementiert `IHasDefault`; der Parser überträgt das `default`-Attribut auf den Sequenzfluss.
- Der Standardfluss greift nur, wenn keine Bedingung zutrifft.

### 10. Engine-Mutationen laufen serialisiert

- `BpmnBusinessLogic` serialisiert Deploy, Start, User-Task, Message und Timer über eine Sperre.
- Die Dateiablage schreibt atomar und toleriert beim Lesen parallel gelöschte Dateien.

## Weiterhin bewusst offen

### 0. Betriebsfähigkeit

- Instanzen lassen sich über `POST /instance/{id}/cancel` abbrechen (Best-Effort-Terminierung), aber nicht zurücksetzen oder kompensieren.
- ~~Service-Tasks haben keinen Worker-Vertrag.~~ Erledigt: Abholen mit Sperre, atomare
  Lease-Verlängerung, Ergebnis- und Fehlermeldung sowie optionale Benachrichtigung per
  Webhook. Siehe `docs/SERVICE-TASK-WORKER.md`.
- ~~Ein Auftrag ohne verbleibende Versuche lässt sich nicht erneut freigeben.~~ Erledigt mit
  dem Störungszentrum: `GET /operations/incidents` führt liegen gebliebene Aufträge und
  gescheiterte Instanzen an einer Stelle zusammen, `POST /job/{jobId}/retry` gibt einen
  Auftrag mit korrigierten Eingaben wieder frei. Siehe `docs/OPERATIONS.md`, Abschnitt
  „Störungen". Offen bleibt dabei:
  - Die Spur der Freigaben (`retryHistory`) hängt am Auftrag und verschwindet mit ihm, sobald
    er abgeschlossen ist; dauerhaft bleibt nur der Logeintrag. Eine instanzgebundene
    Störungshistorie braucht einen eigenen Ereignistyp mit eigener Aufbewahrungsregel.
  - Verbrauchte Versuche werden nicht gezählt — nur die verbleibenden und die Freigaben von Hand.
  - Eine gescheiterte Instanz bleibt gescheitert; eine Neu-Ausführung gibt es weiterhin nicht.
  - KI-Läufe erscheinen nicht als Störung; sie haben einen eigenen Lauf- und Freigabevertrag.
- Fälligkeiten (`dueDate`, `followUpDate`) werden geliefert, aber nicht ausgewertet.
- Zuweisungen (`assignee`, `candidateGroups`, `candidateUsers`) werden geparst, aber nicht ausgewertet.


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

Fehlerpfade sind umgesetzt, Eskalation und Kompensation nicht.

Vorhanden (Fähigkeitsvertrag 5):

- `bpmn:error`-Wurzelelemente, Error-End-Events und Error-Boundary-Events werden geparst und ausgeführt
- ein Fehler wandert vom Ursprung nach außen, bis ein Error-Boundary mit passendem Code oder ohne `errorRef` ihn fängt
- Fangen ist immer unterbrechend: der gefangene Scope und alles darin wird zurückgezogen, samt seiner Message-, Signal- und Timer-Subscriptions
- ohne Fänger endet die Instanz als `Failed` mit einer Begründung an `ProcessInstanceInfo.FailureReason`
- ein externer Worker wirft einen fachlichen Fehler über `POST /job/{jobId}/throw-error`
- eine gescheiterte Instanz erscheint mit ihrer Begründung in der Störungsliste
  (`GET /operations/incidents`) und in der Instanzansicht der Konsole
- `cancelActivity="false"` an einem Error-Boundary wird vor Speichern und Veröffentlichen abgelehnt

Weiterhin offen:

- Escalation Catch/Throw; `GetActiveEscalations()` liefert weiterhin nur eine leere Liste und `HandleEscalation(...)` führt weiterhin nur in einen Best-Effort-Fehlerzustand
- Kompensation (siehe Abschnitt 3)
- Error-Start-Events in Event-Subprozessen und Fehlerpfade über Call Activities
- ein Fehler in einem Multi-Instance-Körper unterbricht die ganze Multi-Instance-Aktivität; eine einzelne Ausprägung lässt sich nicht gesondert behandeln
- `ProcessInstanceInfo.FailureReason` wird beim Scheitern geschrieben und nicht aus der Ablage zurückgelesen: Ein späterer Schreibvorgang an derselben Instanz würde ihn leeren. Eine gescheiterte Instanz wird heute nicht mehr geschrieben, ein Wiederaufsetzen müsste die Begründung mitführen.

### 3. Vollständige Kompensation bei Abbruch

`Cancel()` ist aktuell eine **Best-Effort-Terminierung**:

- aktive und wartende Tokens werden beendet
- offene Interaktionen verschwinden aus dem aktiven Zustand

Nicht enthalten:

- Rückabwicklung bereits durchlaufener Activities
- BPMN-Kompensationshandler
- fachliche Undo-Semantik für Seiteneffekte

### 4. KI-Ausführung und Werkzeuge

Vorhanden sind die besitzergebundene Worker-Lease-Verlängerung sowie revisionsgeschützte
KI-Verbindungsmetadaten mit installationsweiten Cloud-/Lokal-Grenzen und serverseitiger
Secret-Store-Abstraktion. Diese Basis führt bewusst noch keinen Modellaufruf aus.

Weiterhin offen:

- Provideradapter und explizite Modellfähigkeitsprüfung,
- versionierter KI-Task-Vertrag mit Eingaben, Ergebnisschema und Ausgängen,
- dauerhafte, nach Neustart fortsetzbare Läufe,
- typisierte Werkzeugregistry, parametergebundene Freigaben und Ausführungsjournal,
- erneute Ziel-/DNS-Prüfung unmittelbar vor jedem Provideraufruf.

## Empfohlene nächste Runtime-Schritte

1. Recovery- und Wiederholungsstrategie nur noch für Boundary- und Spezialtimer weiter härten
2. Error-/Escalation-Semantik gezielt modellieren und testen
3. KI-Provider erst hinter dauerhaftem Lauf-, Freigabe- und Zielprüfvertrag anbinden
4. `Cancel()` später um echte Kompensationsstrategien erweitern
5. Boundary-Timer nur noch bei echten Randfällen weiter vertiefen
