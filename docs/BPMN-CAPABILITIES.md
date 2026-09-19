# Versionierter BPMN-Fähigkeitsvertrag

Flowzer führt nur eine bewusst begrenzte BPMN-Teilmenge aus. Der Vertrag
`flowzer.bpmn-capabilities/7` liegt maschinenlesbar unter
`contracts/bpmn-capabilities/v7.json` und unterscheidet je Elementart. Version 1 bis 6
bleiben unverändert als historische Verträge erhalten. Der aktuelle Vertrag unterscheidet:

- **modelable:** Der BPMN-Modeler kann das Element darstellen beziehungsweise erzeugen.
- **parsable:** Der bestehende Parser kann das Element lesen, etwa für historische Modelle.
- **executable:** Neue Workflow-Versionen dürfen das Element tatsächlich ausführen.

`parsable` ist ausdrücklich kein Ausführungsversprechen. Beispielsweise bleiben Script-
Tasks für Bestandsanalyse lesbar, werden aber vor Save oder Deploy als nicht ausführbar
abgelehnt. KI-Service-Tasks sind seit #252 / PR #253 ausführbar, weil Deployment,
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

Version 7 meldet bewusst den ersten Fehler in deterministischer Dokumentreihenfolge.
Nach der Korrektur kann der identische Endpunkt erneut aufgerufen werden. Eine spätere
Mehrfachdiagnose ist eine additive Vertragsweiterentwicklung, kein Grund, heute Parser-
oder Laufzeittexte als Clientvertrag zu verwenden.

## Ausführbares Profil v7

Offiziell ausführbar sind:

- Plain-, Message-, Signal- und Timer-Start
- Plain-, Terminate-, Error- und Message-Ende
- User-, Worker-Service-, KI-Service-, Receive-, Send-, Manual- und generische Tasks
- exklusive und parallele Gateways
- Sequenzflüsse und lokale Subprozesse
- Message-, Signal-, Timer- und Error-Boundary-Events sowie Message-, Signal- und
  Timer-Intermediate-Catch-Events
- Intermediate-Throw-Events ohne Ereignisdefinition (Meilenstein) und mit
  Nachrichtendefinition
- lokale Aufruf-Aktivitäten (`callActivity`)

Wie sich dieses Profil gegen eine fremde Messlatte schlägt, hält
[BPMN-MIWG-COVERAGE.md](BPMN-MIWG-COVERAGE.md) je Referenzmodell der BPMN Model Interchange
Working Group fest — gelesen, veröffentlichbar, ausgeführt, jeweils mit dem konkreten Grund.

Insbesondere nicht als ausführbar zugesagt sind Script-Tasks,
Inclusive-/Complex-Gateways, Signal-Throw- und Signal-End-Events sowie Escalation-Pfade und
Kompensation. Diese Grenzen werden erweitert, wenn der jeweilige Runtime-Pfad mit Semantik-,
Recovery- und Konkurrenztests belegt ist – nicht bereits dann, wenn der Parser XML lesen kann.

`serviceTask.aiTask` ist modellierbar, parsebar und ausführbar. Beim Deployment bindet
Flowzer die konkrete Verbindungsrevision und das effektive Modell unveränderlich an die
Definition. Seine vollständigen Vertrags-, Lauf- und Sicherheitsregeln stehen in
[AI-TASKS.md](AI-TASKS.md). Der Autorenvertrag kann seit #254 / PR #255 zusätzlich typisierte
Werkzeugreferenzen speichern. Eine solche Referenz blockiert das Deployment noch mit
`bpmn.ai_task.tools_runtime_unavailable`, bis Aktionsjournal und parametergebundene
Freigaben denselben Ausführungsschutz belegen. KI-Tasks ohne Werkzeuge bleiben ausführbar.

## Fehlerereignisse (Vertrag 5)

Version 5 erweiterte Version 4 additiv um `endEvent.errorEventDefinition` und
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
Event-Subprozessen bleiben offen. Der Gliederungseditor kennt
Fehlerereignisse so wenig wie die übrigen Ereignisdefinitionen und meldet sie als Blocker,
statt sie beim Speichern zu verlieren.

## Lokale Aufruf-Aktivität (Vertrag 7)

Version 7 erweitert Version 6 additiv um genau einen Eintrag: `callActivity` ist ausführbar.
Die älteren Vertragsdateien bleiben unverändert und sagen Aufruf-Aktivitäten weiterhin nicht zu.

Eine Aufruf-Aktivität startet einen anderen Prozess derselben Installation und wartet auf dessen
Ende. Zwei Pflichtprüfungen vor dem Speichern und Veröffentlichen:

- `bpmn.call_activity.process_id_required` — `zeebe:calledElement/@processId` fehlt oder ist leer.
- `bpmn.call_activity.process_id_literal_required` — die Prozesskennung beginnt mit `=`, ist also
  ein FEEL-Ausdruck. In dieser Stufe ist nur ein Literal erlaubt; sonst stünde erst zur Laufzeit
  fest, welche Prozesse ein veröffentlichter Workflow überhaupt aufruft.

Ob der aufgerufene Prozess existiert, wird beim Deployment ausdrücklich **nicht** geprüft: Er
darf später entstehen, und welche Version gilt, entscheidet der Zeitpunkt des Aufrufs. Ein
fehlender Zielprozess ist der BPMN-Fehler `CALLED_PROCESS_NOT_FOUND` an der Aufruf-Aktivität.

Variablenfluss, Fehler- und Abbruchsemantik, Rekursionsgrenze, Rechte und die Grenzen dieser
Stufe stehen vollständig in [CALL-ACTIVITY.md](CALL-ACTIVITY.md). Der Fernaufruf in eine andere
Installation ist Stufe 2+ von #154 und noch nicht Teil dieses Vertrags.

## Nachrichten senden (Vertrag 6)

Version 6 erweitert Version 5 additiv um `intermediateThrowEvent.plain`,
`intermediateThrowEvent.messageEventDefinition`, `endEvent.messageEventDefinition` und
`sendTask`. Signal-Throw und Signal-Ende bleiben außen vor. Die älteren Vertragsdateien
bleiben unverändert und sagen sendende Nachrichtenelemente weiterhin nicht zu.

Bis Vertrag 5 konnten Prozesse Nachrichten nur empfangen, und zwar nur von außen über
`POST /message`. Ein Prozess, der einem anderen etwas mitteilt, war nicht modellierbar.

### Dieselbe Nachricht auf beiden Seiten

Ein sendendes Element trägt denselben Verweis wie die fangende Seite: `messageRef` auf ein
`bpmn:message` (dessen `name` die Nachricht benennt) und optional
`zeebe:subscription/@correlationKey` als FEEL-Ausdruck. Der Schlüssel wird beim Erreichen des
Elements gegen die Prozessvariablen ausgewertet — auf demselben Weg, über den auch ein
Catch-Event zu seinem Schlüssel kommt. Am Send-Task steht `messageRef` am Element selbst, an
einem Ereignis an seiner `messageEventDefinition`.

### Zwei Ausführungsarten

**Intern korrelieren** (der Standard, ohne `zeebe:taskDefinition`). Beim Erreichen des
Elements stellt die Engine eine ausgehende Nachricht aus Name, ausgewertetem
Korrelationsschlüssel und Variablen bereit. Sie kennt keine anderen Instanzen und sammelt die
Nachrichten nur; die Geschäftslogik arbeitet sie nach dem Speichern der Instanz **in derselben
Transaktion** über denselben Zustellweg ab wie `POST /message`:

1. an ein wartendes Catch-Event, einen Receive-Task oder ein Message-Boundary mit passendem
   Namen und Schlüssel — einer anderen **oder derselben** Instanz,
2. sonst an ein Message-Start-Event, das eine neue Instanz beginnt. Ein Message-Start trägt
   keinen Korrelationsschlüssel; er wird dort deshalb nicht verglichen.

**Keine Pufferung.** Findet sich kein Empfänger, verfällt die Nachricht — so sieht BPMN es vor,
und Flowzer hebt sie bewusst nicht auf: Ein später gestarteter Empfänger bekäme sonst eine
Nachricht aus einem längst abgeschlossenen Vorgang. Das sendende Element gilt trotzdem als
abgeschlossen, und der Prozess läuft sofort weiter. Werfen ist nicht blockierend; auf eine
Antwort wartet erst ein eigenes Catch-Element.

**Datensparsamkeit.** Mitgegeben werden ausschließlich die Eingabewerte des Elements nach
`zeebe:ioMapping`-Input. Ohne Zuordnung ist die Nachricht **leer** — ausdrücklich nicht der
ganze Prozesskontext. Ein Empfänger ist ein fremder Vorgang; was er sehen soll, muss im Modell
stehen. Umgekehrt schreibt ein Message-Catch-Event die empfangenen Werte wie eine
Empfangsaufgabe in seinen Prozesskontext, mit `zeebe:ioMapping`-Output gezielt und ohne
Zuordnung vollständig.

**Als Worker-Auftrag** (mit `zeebe:taskDefinition/@type`). Send-Task, Message-Throw-Event und
Message-End-Event verhalten sich dann wie ein Service-Task: Die Engine legt einen Auftrag an,
ein externer Worker holt ihn und meldet Ergebnis oder Fehler zurück — der übliche Weg für einen
Versand nach draußen, etwa per E-Mail. Der Auftragstyp **ersetzt** die interne Zustellung; es
geschieht nicht beides. Auch hier sieht der Worker nur die gemappten Eingabewerte, sonst die
Prozessvariablen — dieselbe Regel wie am Service-Task
([SERVICE-TASK-WORKER.md](SERVICE-TASK-WORKER.md)).

Ein Message-End-Event verhält sich wie ein Throw und beendet danach seinen Pfad wie ein
gewöhnliches Ende. Ein Intermediate-Throw-Event ohne Ereignisdefinition ist ein reiner
Meilenstein: Es läuft durch, sendet nichts und bleibt im Laufzeitverlauf als erreichter Knoten
sichtbar.

### Grenzen

- Ein sendendes Element ohne `messageRef` **und** ohne Auftragstyp hätte kein Ziel und wird vor
  Speichern und Veröffentlichen mit `bpmn.message_throw.target_required` am betroffenen Knoten
  abgelehnt.
- Eine Nachricht erreicht genau einen Empfänger. Warten mehrere Instanzen auf denselben Namen
  und Schlüssel, bekommt eine davon die Nachricht.
- Nachrichten über Installationsgrenzen hinweg (#154) bleiben offen; zugestellt wird nur
  innerhalb derselben Flowzer-Installation.
- Eine Zustellung läuft in der Transaktion und unter der Sperre des auslösenden Aufrufs.
  Scheitert sie, scheitert die ganze Mutation; ein transaktionaler Adapter verwirft damit auch
  den Fortschritt des Senders. Die Dateiablage kennt keine Transaktion und kann einen
  Zwischenstand zurücklassen — sie bleibt auf Entwicklung begrenzt.
- Antworten sich zwei Prozesse gegenseitig ohne Ende, bricht die Mutation nach 100
  Zustellungen ab, statt den Aufrufer hängen zu lassen.
- Die Zustellung an eine andere Instanz nimmt deren Zeilensperre, während die des Senders
  noch gehalten wird. Senden sich zwei Instanzen in zwei API-Prozessen gleichzeitig
  gegenseitig etwas, erkennt PostgreSQL die Verklemmung und bricht eine der beiden
  Transaktionen ab; der Aufrufer bekommt einen Fehler und wiederholt. Ein Sperren in fester
  Reihenfolge gibt es nicht, weil der Empfänger erst während der Zustellung bekannt wird.

## Was überlesen wird

Ein BPMN-Dokument aus einem fremden Werkzeug trägt fast immer Bestandteile, die laut
BPMN 2.0 erlaubt sind, aber keine Ausführungssemantik haben. Flowzer **überliest** sie —
im Parser und in der Veröffentlichungsprüfung, ohne Ausnahme und ohne Fähigkeitsfehler:

- Gliederung: `laneSet`, `lane`
- Beschriftung und Gruppierung: `textAnnotation`, `association`, `group`, `category`,
  `documentation`
- Datenbeiwerk an Prozess und Aktivität: `dataObject`, `dataObjectReference`,
  `dataStoreReference`, `ioSpecification`, `dataInput`, `dataOutput`,
  `dataInputAssociation`, `dataOutputAssociation`, `property`
- auf Dokumentebene: `bpmn:collaboration` mit `participant` und `messageFlow`. Der
  ausführbare Prozess wird weiterhin über `bpmn:process` gefunden.

**Überlesen heißt ausdrücklich nicht ausgeführt.** Eine Lane weist keine Arbeit zu, ein
Datenobjekt trägt keine Variablen, ein Message-Flow stellt nichts zu. Wer das braucht,
modelliert es als zugesagtes Element: Zuständigkeit über die Aufgabenzuweisung am
User-Task, Daten über `zeebe:ioMapping`, Nachrichten über `messageRef`.

Die Toleranz betrifft **Lesen und Prüfen, nicht die Ausführbarkeit**; der Vertrag
`flowzer.bpmn-capabilities/7` bleibt unverändert. Ein Element mit echter
Ausführungssemantik, das der Vertrag nicht führt — etwa `eventBasedGateway`,
`businessRuleTask`, `transaction` oder `adHocSubProcess` — wird weiterhin abgelehnt, vor
dem Veröffentlichen mit `bpmn.element.unsupported` am betroffenen Knoten.

Zwei Angaben sind in BPMN optional und werden deshalb auch optional gelesen: der `name`
einer `bpmn:message` und einer `bpmn:signal`. Ein Element, das auf eine **namenlose**
Nachricht zeigt, könnte allerdings nie korrelieren — Name und Korrelationsschlüssel sind
der ganze Vertrag zwischen Sender und Empfänger. Das wird vor Speichern und
Veröffentlichen mit `bpmn.message.name_required` am verweisenden Knoten abgelehnt, nicht
an der Nachricht.

Eine fehlende `id` bleibt ein Modellfehler: Die Engine verweist über sie auf jeden Knoten.
Sie wird aber als benannter Fehler mit der Elementart gemeldet — im Parser als
`ModelValidationException`, vor der Veröffentlichung als `bpmn.element.id_required` —, nie
als unbehandelte `NullReferenceException`.

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
