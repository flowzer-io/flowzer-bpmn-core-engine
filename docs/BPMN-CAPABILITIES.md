# Versionierter BPMN-Fähigkeitsvertrag

Flowzer führt nur eine bewusst begrenzte BPMN-Teilmenge aus. Der Vertrag
`flowzer.bpmn-capabilities/5` liegt maschinenlesbar unter
`contracts/bpmn-capabilities/v5.json` und unterscheidet je Elementart. Version 1 bis 4
bleiben unverändert als historische Verträge erhalten. Der aktuelle Vertrag unterscheidet:

- **modelable:** Der BPMN-Modeler kann das Element darstellen beziehungsweise erzeugen.
- **parsable:** Der bestehende Parser kann das Element lesen, etwa für historische Modelle.
- **executable:** Neue Workflow-Versionen dürfen das Element tatsächlich ausführen.

`parsable` ist ausdrücklich kein Ausführungsversprechen. Beispielsweise bleiben Script-
Tasks und Call Activities für Bestandsanalyse lesbar, werden aber vor Save oder Deploy als
nicht ausführbar abgelehnt. KI-Service-Tasks sind seit #252 / PR #253 ausführbar, weil Deployment,
persistenter Lauf, Recovery und Engine-Fortschritt nun denselben geprüften Vertrag verwenden.

## Öffentliche API

- `GET /definition/capabilities` liefert den aktuellen Vertrag.
- `POST /definition/validate` prüft einen speicherbaren Autorenstand,
- `POST /definition/validate/deployment` prüft denselben Stand für eine Veröffentlichung.
- `POST /definition` und `POST /definition/deploy` erzwingen die jeweils passende Prüfung innerhalb
  ihres serverseitigen Anwendungsfalls. Eine Browser-Vorprüfung ist daher keine
  Sicherheitsgrenze.

Ein Modellfehler antwortet mit `422 application/problem+json`, dem Hauptcode
`bpmn.model.invalid`, der Vertragsversion sowie `issues`. Jeder Befund enthält einen
stabilen Code, Schweregrad, Nachricht und – soweit möglich – `elementId` und
`propertyPath`. Die Konsole zeigt diese Befunde dauerhaft, markiert den BPMN-Knoten und
macht ihn per Tastatur beziehungsweise Klick anwählbar. Die Gliederung kann zum selben
Knoten im Diagramm wechseln.

Version 5 meldet bewusst den ersten Fehler in deterministischer Dokumentreihenfolge.
Nach der Korrektur kann der identische Endpunkt erneut aufgerufen werden. Eine spätere
Mehrfachdiagnose ist eine additive Vertragsweiterentwicklung, kein Grund, heute Parser-
oder Laufzeittexte als Clientvertrag zu verwenden.

## Ausführbares Profil v5

Offiziell ausführbar sind:

- Plain-, Message-, Signal- und Timer-Start
- Plain-, Terminate- und Error-Ende
- User-, Worker-Service-, KI-Service-, Receive-, Manual- und generische Tasks
- exklusive und parallele Gateways
- Sequenzflüsse und lokale Subprozesse
- Message-, Signal-, Timer- und Error-Boundary-Events sowie Message-, Signal- und
  Timer-Intermediate-Catch-Events

Insbesondere nicht als ausführbar zugesagt sind Script-Tasks, Call Activities,
Inclusive-/Complex-Gateways, Intermediate-Throw-Events, Message-/Signal-End-Events sowie
Escalation-Pfade und Kompensation. Diese Grenzen werden erweitert, wenn der jeweilige
Runtime-Pfad mit Semantik-, Recovery- und Konkurrenztests belegt ist – nicht bereits dann,
wenn der Parser XML lesen kann.

`serviceTask.aiTask` ist modellierbar, parsebar und ausführbar. Beim Deployment bindet
Flowzer die konkrete Verbindungsrevision und das effektive Modell unveränderlich an die
Definition. Seine vollständigen Vertrags-, Lauf- und Sicherheitsregeln stehen in
[AI-TASKS.md](AI-TASKS.md). Der Autorenvertrag kann seit #254 / PR #255 zusätzlich typisierte
Werkzeugreferenzen speichern. Eine solche Referenz blockiert das Deployment noch mit
`bpmn.ai_task.tools_runtime_unavailable`, bis Aktionsjournal und parametergebundene
Freigaben denselben Ausführungsschutz belegen. KI-Tasks ohne Werkzeuge bleiben ausführbar.

## Fehlerereignisse (Vertrag 5)

Version 5 erweitert Version 4 additiv um `endEvent.errorEventDefinition` und
`boundaryEvent.errorEventDefinition`. Die älteren Vertragsdateien bleiben unverändert und
sagen Fehlerpfade weiterhin nicht zu.

`bpmn:error`-Wurzelelemente werden mit `id`, `name` und `errorCode` gelesen; ein
`errorEventDefinition` zeigt über `errorRef` darauf. Die Semantik:

- Erreicht ein Token ein Error-End-Event, löst es einen Fehler mit dem `errorCode` des
  referenzierten `bpmn:error` aus. Ohne `errorRef` trägt der Fehler keinen Code.
- Gefangen wird am nächsten umschließenden Scope: erst ein Error-Boundary-Event mit
  demselben Code, sonst eines ohne `errorRef` — das fängt jeden Fehler. Findet sich keines,
  wandert der Fehler an den nächstäußeren Scope.
- Fangen ist immer **unterbrechend**: Der gefangene Scope und alles, was darin noch läuft,
  werden zurückgezogen; damit verschwinden auch seine Message-, Signal- und Timer-Boundary-
  Subscriptions. Das Boundary-Token folgt seinem ausgehenden Sequenzfluss.
- `cancelActivity="false"` an einem Error-Boundary ist laut BPMN 2.0 ungültig und wird vor
  dem Speichern und Veröffentlichen mit `bpmn.error_boundary.cancel_activity_invalid` am
  betroffenen Knoten abgelehnt.
- Erreicht der Fehler die Prozessebene ungefangen, endet die Instanz als `Failed`. Die
  Begründung steht als `failureReason` an der Instanz, etwa
  `Unhandled BPMN error 'ANTRAG_UNVOLLSTAENDIG' at 'ErrorEnd_1'.`; sie ist Teil der
  Diagnosesicht und damit an die Betriebsrolle gebunden. Das Error-End-Event bleibt im
  Laufzeitverlauf als erreichter Knoten sichtbar.
- Ein externer Worker wirft denselben Fehler über `POST /job/{jobId}/throw-error`; siehe
  [SERVICE-TASK-WORKER.md](SERVICE-TASK-WORKER.md).

**Grenzen.** Ein Fehler innerhalb einer Multi-Instance-Aktivität unterbricht die ganze
Aktivität und wird an deren Boundary aufgelöst — eine einzelne Ausprägung lässt sich nicht
gesondert behandeln. Escalation-Ereignisse, Kompensation, Error-Start-Events in
Event-Subprozessen und Call Activities bleiben offen. Der Gliederungseditor kennt
Fehlerereignisse so wenig wie die übrigen Ereignisdefinitionen und meldet sie als Blocker,
statt sie beim Speichern zu verlieren.

## Statische Prüfungen

Neben der Elementmatrix prüft Version 3 vor Save und Deploy:

- mindestens einen ausführbaren Prozess
- nichtleere und innerhalb eines Containers eindeutige Element-IDs
- gültige Source-/Target-Referenzen von Sequenzflüssen
- Erreichbarkeit von Flow-Nodes ab Start- beziehungsweise Boundary-Aktivierung
- gültigen Default-Ausgang und Bedingungen nicht-defaultiger Ausgänge an exklusiven Splits
- Formbindung für User-Tasks, Worker-Typ für Service-Tasks und Zeitangabe für Timer
- dieselben Regeln separat innerhalb jedes lokalen Subprozesses

Boundary-Events sind Aktivierungswurzeln und benötigen naturgemäß keinen eingehenden
Sequenzfluss. Container ohne eigenes StartEvent bleiben für die vorhandene eingebettete
Subprozess-Semantik kompatibel.

## Versionierung und Kompatibilität

Der JSON-Vertrag wird als Ressource in die Engine eingebettet und über OpenAPI
veröffentlicht. Eine Änderung der zugesagten Semantik benötigt eine neue Vertragsversion
und Regressionstests. Laufende Instanzen werden nicht nachträglich gegen eine neuere
Matrix validiert; ihre gebundene Definitions- und Formularversion bleibt maßgeblich.

Version 3 erweitert Version 2 ausschließlich um die belegte KI-Service-Task-Runtime. Die
historischen Dateien werden nicht umgeschrieben, damit gespeicherte Vertragsstände und
generierte Clients nachvollziehbar bleiben.

Der OpenAPI-Snapshot und die generierten TypeScript-Schemas werden gemeinsam aktualisiert.
Konkrete Hosts können die generische API oder die öffentlichen Pakete verwenden, werden
aber weder im Flowzer-Modell noch in Flowzers Laufzeit referenziert.

## Manual Tasks und generische Tasks (Vertrag 4)

Beide Elementarten durchlaufen die Engine ohne Wartezustand und folgen ihren
Sequenzflüssen. Eine Manual Task erzeugt keine Benutzeraufgabe; ihre reale Arbeit
findet außerhalb der Engine statt. Eine nachweislich zu bestätigende Tätigkeit
muss als User Task modelliert werden. Unbekannte Spezialtypen oder unvollständige
Service-/User-/KI-Tasks erhalten ausdrücklich keinen solchen Fallback.

Auch bereits veröffentlichte Definitionen mit Manual Tasks werden ohne
Neuinterpretation der gespeicherten BPMN-Datei ausführbar. Überfällige Einmaltimer
werden beim nächsten Scheduler-Tick einmal verarbeitet; vor einem Rollout ist
bei wiederkehrenden Timern die bestehende Nachholsemantik zu berücksichtigen.

Einzelfehler bei der Timerverarbeitung werden nach dem Tick an die
Schedulerdiagnose weitergereicht: erfolgreiche Verarbeitung anderer Timer
versteckt einen gescheiterten Timer nicht mehr hinter dem Status Healthy.
Die Änderung ersetzt nicht die weiter erforderlichen Mehrprozess-/Transaktions-
und Wiederholungsgrenzen aus #93.
