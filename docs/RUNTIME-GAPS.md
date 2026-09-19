# Laufzeitlücken und aktueller Restbestand

**Stand:** 19. September 2026

Dieses Dokument hält die aktuell noch offenen Laufzeit- und Engine-Lücken fest, damit `main` nicht nur "grün", sondern auch fachlich ehrlich bleibt.

## Fehlerereignisse: Error End und Error Boundary

Fachliche Fehler laufen jetzt auf BPMN-Ebene weiter, statt die Instanz nur auf `Failed` zu
setzen. Die Semantik steht in [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md) (Vertrag 5), der
Worker-Weg in [SERVICE-TASK-WORKER.md](SERVICE-TASK-WORKER.md). Escalation und Kompensation
bleiben ausdrücklich offen.

## Lokale Call Activity

Ein Prozess kann jetzt einen anderen Prozess derselben Installation aufrufen und auf dessen Ende
warten. Die vollständige Semantik steht in [CALL-ACTIVITY.md](CALL-ACTIVITY.md), der Vertrag in
[BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md) (Vertrag 7).

Vorhanden:

- `callActivity` ist ausführbar; `zeebe:calledElement/@processId` ist Pflicht und muss ein
  Literal sein
- das Token wartet wie an einem Service-Task; die Engine stellt den Aufruf bereit, die
  Geschäftslogik startet die Kindinstanz in derselben Transaktion
- Variablen hinein nach `propagateAllParentVariables` und `zeebe:ioMapping`-Eingang, heraus nach
  `propagateAllChildVariables` und `zeebe:ioMapping`-Ausgang
- Ende, Terminate, ungefangener BPMN-Fehler, Abbruch und fehlender Zielprozess sind als
  BPMN-Fehler an der Aufruf-Aktivität fangbar
- Abbruch des Aufrufers bricht laufende Kindinstanzen rekursiv mit ab
- `GET /instance/{id}/children` und `parentInstanceId` machen den Verbund in der Konsole sichtbar

Weiterhin offen:

- Fernaufruf in eine andere Flowzer-Installation (#154, Stufe 2+)
- FEEL-Ausdruck als Prozesskennung und Bindung an eine feste Version (`versionTag`)
- Migration eines Aufrufers mit wartender Aufruf-Aktivität (`CallActivityWaiting`)
- Multi-Instance an der Aufruf-Aktivität
- Zwischenstände vor dem Ende der Kindinstanz
- Kompensation beim Abbruch
- Rekursion bricht ab Tiefe 10 ab; eine echte Zyklenerkennung gibt es nicht

## Entscheidungen: Business-Rule-Task und DMN-Katalog

Ein Prozess kann jetzt eine Entscheidungstabelle auswerten, statt jede Regel in Gateways zu
schnitzen. Die vollständige Semantik steht in [DMN.md](DMN.md), der Vertrag in
[BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md) (Vertrag 8).

Vorhanden:

- `businessRuleTask` ist ausführbar, in beiden Arten: `zeebe:calledDecision` wertet Flowzer
  selbst aus, `zeebe:taskDefinition` bleibt ein Auftrag für einen externen Worker
- das Token wartet wie an einem Service-Task; die Engine stellt die Entscheidung bereit, die
  Geschäftslogik rechnet sie in derselben Transaktion — dasselbe Muster wie bei der
  Aufruf-Aktivität
- die produktive FEEL-Brücke liegt in `src/core-engine/Dmn/`; dieselbe FEEL-Auswertung, die
  Prozessausdrücke rechnet, rechnet auch die Tabelle
- Entscheidungskatalog mit Versionen über `/decision`, dazu ein Trockenlauf-Endpunkt, der
  eine Entscheidung durchrechnet, ohne eine Instanz anzufassen
- `DECISION_NOT_FOUND`, `DECISION_AMBIGUOUS` und `DECISION_EVALUATION_FAILED` sind als
  BPMN-Fehler am Task fangbar
- Konsole: Seite „Entscheidungen" mit dmn-js (DRD, Entscheidungstabelle, Literal-Expression),
  Trockenlauf-Dialog und ein Abschnitt „Entscheidung" am Business-Rule-Task

Weiterhin offen:

- keine Entwürfe im Entscheidungskatalog: Speichern heißt immer neue Version, und die jüngste
  Version gilt sofort
- keine Versionsbindung am Task (`versionTag`); es gilt stets die jüngste Version
- keine Ordner und keine Rechte je Entscheidungsdatei
- keine DRD-Auswertung über Dateigrenzen hinweg (`import`); `requiredDecision` muss in
  derselben Datei auflösbar sein
- kein Import aus Camunda 7 (`camunda:decisionRef`)
- ohne FEEL-fähigen Ausdrucks-Handler (also ohne V8) ist DMN nicht benutzbar; die Engine
  läuft weiter, der Zugriff auf die Entscheidung meldet `FlowzerDmnUnavailableException`
- die offizielle DMN TCK ist weiterhin nicht angebunden
- Multi-Instance am Business-Rule-Task ist ungeprüft

## Nachrichten senden: Message-Throw, Message-Ende und Send-Task

Prozesse können einander jetzt etwas mitteilen, statt Nachrichten nur von außen über
`POST /message` zu empfangen. Die vollständige Semantik steht in
[BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md) (Vertrag 6).

Vorhanden:

- `intermediateThrowEvent` mit Nachrichtendefinition, `endEvent` mit Nachrichtendefinition und
  `sendTask` sind ausführbar; ein Throw-Event ohne Ereignisdefinition läuft als Meilenstein durch
- die Engine sammelt ausgehende Nachrichten mit ausgewertetem Korrelationsschlüssel und den
  Eingabewerten nach `zeebe:ioMapping`; ohne Zuordnung geht bewusst nichts mit
- die Geschäftslogik stellt sie nach dem Speichern der Instanz in derselben Transaktion über
  denselben Weg zu wie `POST /message` — an eine wartende Instanz (auch die sendende selbst)
  oder über ein Message-Start-Event an eine neue
- ohne Empfänger verfällt die Nachricht; das sendende Element gilt trotzdem als abgeschlossen
- mit `zeebe:taskDefinition/@type` wird stattdessen ein Auftrag für einen externen Worker
  angelegt — derselbe Auftragspfad wie am Service-Task, samt Complete, Fail und Throw-Error
- ein Message-Catch-Event schreibt die empfangenen Werte wie eine Empfangsaufgabe in seinen
  Prozesskontext; zuvor gingen sie verloren

Weiterhin offen:

- Signal-Throw und Signal-Ende; ein Signalwurf wird als nicht unterstützte Ereignisdefinition
  abgelehnt
- Pufferung und Time-to-live: Eine Nachricht ohne Empfänger verfällt sofort
- Nachrichten über Installationsgrenzen hinweg (#154)
- Escalation-Throw (siehe Abschnitt 2 unten)
- Eine Nachricht erreicht genau einen Empfänger; warten mehrere Instanzen auf denselben Namen
  und Schlüssel, ist die Auswahl nicht weiter festgelegt
- Die Zustellung läuft in der Transaktion des Aufrufers. Bei einem Fehler scheitert die ganze
  Mutation; die nichttransaktionale Dateiablage kann dabei einen Zwischenstand zurücklassen —
  dieselbe bekannte Grenze wie bei jedem anderen Schreibvorgang dort.
- Gegenseitiges Antworten ohne Ende bricht nach 100 Zustellungen je Mutation ab; eine echte
  Zyklenerkennung gibt es nicht.

## Korrektur #310: Manual Tasks und Timerdiagnose

Manual Tasks durchlaufen wie generische Tasks ohne Wartezustand den Sequenzfluss.
Die Fähigkeitsmatrix v4 erlaubt sie bei neuer Veröffentlichung; historische Verträge
und gespeicherte Definitionen bleiben unverändert. Regressionen prüfen normalen
Start und einmaliges Nachholen eines überfälligen Timer-Starts nach Neustart.

Fehler einzelner Timer führen nun zu einem fehlgeschlagenen Scheduler-Tick. Nur
solche klassifizierten Einzelfehler werden beim Hochlauf toleriert, damit die API
für Diagnose erreichbar bleibt. Wiederherstellungs-/Commitfehler bleiben fatal.

**Mehrprozessschutz der Timer ist geschlossen.** Ein Scheduler-Durchgang übernimmt die
fälligen Start-Timer jetzt exklusiv (`FOR UPDATE SKIP LOCKED` in derselben Transaktion,
`IMessageSubscriptionStorage.ClaimDueTimerSubscriptions`); vorher überführten zwei API-Prozesse
dieselbe Fälligkeit in zwei Instanzen. Instanztimer bleiben bewusst ohne Zeilensperre: Sie
laufen über den Advisory-Lock der Instanz, den jeder Engine-Schreiber vor weiteren
Zeilensperren nimmt — eine zusätzliche Zeilensperre davor drehte die Sperrreihenfolge um.
Belegt in `src/WebApiEngine.Tests/MultiProcessConcurrencyTest.Lifecycle.cs`; die
Betriebsbedingungen stehen unter [Mehrprozessbetrieb](OPERATIONS.md#mehrprozessbetrieb).
Die Dateiablage bleibt Einzelprozess.

Offen aus #93 bleiben die transaktionsweise Isolation einzelner Timer innerhalb eines
Durchgangs (ein fehlgeschlagener Timer rollt den ganzen Durchgang zurück) und das begrenzte
Nachholen wiederkehrender Timer.

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

- Instanzen lassen sich über `POST /instance/{id}/cancel` abbrechen (Best-Effort-Terminierung), aber nicht kompensieren.
- ~~Eine laufende Instanz lässt sich nicht an eine andere Stelle setzen.~~ Erledigt mit den
  Instanzeingriffen: `POST /instance/{id}/modification` zieht einen wartenden Schritt zurück
  und lässt ihn am gewählten Knoten derselben Version neu beginnen, auf Wunsch mit korrigierten
  Variablen. Siehe `docs/INSTANCE-MODIFICATION.md`. Offen bleibt dabei:
  - Tokens in Teilprozessen und Multi-Instance-Aktivitäten lassen sich nicht verschieben; ein
    Token dort zurückzuziehen ließe einen Scope zurück, den niemand mehr abschließt.
  - Neue Schritte ohne Quelle gibt es nicht — es wird nur verschoben, nie erzeugt. Ein zweiter
    paralleler Zweig lässt sich damit nicht eröffnen.
  - Variablen werden nur als ganze Werte der obersten Ebene geschrieben und entfernt; Pfade
    und Indexe sind abgelehnt statt halb verstanden.
  - Ein Eingriff lässt sich nicht zurücknehmen, und die Aufgaben-Kennung der verlassenen
    Stelle kommt nicht wieder.
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
- ~~Fälligkeiten (`dueDate`, `followUpDate`) werden geliefert, aber nicht ausgewertet.~~
  Erledigt: Fristen werden beim Erreichen der Aufgabe an absolute Zeitpunkte gebunden,
  überwacht und gemeldet. Siehe `docs/HUMAN-TASK-DEADLINES.md`.
- ~~Zuweisungen (`assignee`, `candidateGroups`, `candidateUsers`) werden geparst, aber nicht ausgewertet.~~
  Erledigt: Modellzuweisung und tatsächliche Bearbeitung (Claim/Release/Assign/Delegate)
  sind getrennt und werden serverseitig geprüft. Siehe `docs/HUMAN-TASK-LIFECYCLE.md`.
- Es gibt keine Aufbewahrungsregel: Beendete Instanzen samt Historie, Aufgaben und
  Aufträgen bleiben unbegrenzt erhalten. Siehe M7 in `docs/PRODUCT-ROADMAP-2026-09.md`.


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
