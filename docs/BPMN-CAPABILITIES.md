# Versionierter BPMN-Fähigkeitsvertrag

Flowzer führt nur eine bewusst begrenzte BPMN-Teilmenge aus. Der Vertrag
`flowzer.bpmn-capabilities/9` liegt maschinenlesbar unter
`contracts/bpmn-capabilities/v9.json` und unterscheidet je Elementart. Version 1 bis 8
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

Version 9 meldet bewusst den ersten Fehler in deterministischer Dokumentreihenfolge.
Nach der Korrektur kann der identische Endpunkt erneut aufgerufen werden. Eine spätere
Mehrfachdiagnose ist eine additive Vertragsweiterentwicklung, kein Grund, heute Parser-
oder Laufzeittexte als Clientvertrag zu verwenden.

## Ausführbares Profil v9

Offiziell ausführbar sind:

- Plain-, Message-, Signal- und Timer-Start
- Plain-, Terminate-, Error- und Message-Ende
- User-, Worker-Service-, KI-Service-, Receive-, Send-, Manual- und generische Tasks
- exklusive, parallele, inklusive und ereignisbasierte Gateways
- Sequenzflüsse, lokale Subprozesse und Event-Subprozesse
- Message-, Signal-, Timer- und Error-Boundary-Events sowie Message-, Signal- und
  Timer-Intermediate-Catch-Events
- Intermediate-Throw-Events ohne Ereignisdefinition (Meilenstein) und mit
  Nachrichtendefinition
- lokale Aufruf-Aktivitäten (`callActivity`)
- Business-Rule-Tasks (`businessRuleTask`)
- Eskalationspfade: Throw-, Ende- und Boundary-Ereignis sowie Start im Event-Subprozess
- Error- und Escalation-Start-Events **innerhalb** eines Event-Subprozesses

Wie sich dieses Profil gegen eine fremde Messlatte schlägt, hält
[BPMN-MIWG-COVERAGE.md](BPMN-MIWG-COVERAGE.md) je Referenzmodell der BPMN Model Interchange
Working Group fest — gelesen, veröffentlichbar, ausgeführt, jeweils mit dem konkreten Grund.

Insbesondere nicht als ausführbar zugesagt sind Script-Tasks, Complex-Gateways,
Signal-Throw- und Signal-End-Events sowie Kompensation und Transaktions-Subprozesse. Diese
Grenzen werden erweitert, wenn der jeweilige Runtime-Pfad mit Semantik-, Recovery- und
Konkurrenztests belegt ist – nicht bereits dann, wenn der Parser XML lesen kann.

`serviceTask.aiTask` ist modellierbar, parsebar und ausführbar. Beim Deployment bindet
Flowzer die konkrete Verbindungsrevision und das effektive Modell unveränderlich an die
Definition. Seine vollständigen Vertrags-, Lauf- und Sicherheitsregeln stehen in
[AI-TASKS.md](AI-TASKS.md). Der Autorenvertrag kann seit #254 / PR #255 zusätzlich typisierte
Werkzeugreferenzen speichern. Eine solche Referenz blockiert das Deployment noch mit
`bpmn.ai_task.tools_runtime_unavailable`, bis Aktionsjournal und parametergebundene
Freigaben denselben Ausführungsschutz belegen. KI-Tasks ohne Werkzeuge bleiben ausführbar.

## Gateways, Event-Subprozess und Eskalation (Vertrag 9)

Version 9 erweitert Version 8 additiv. Die älteren Vertragsdateien bleiben unverändert und
sagen diese Elemente weiterhin nicht zu.

### Ereignisbasiertes Gateway (`eventBasedGateway`)

Ein ereignisbasiertes Gateway entscheidet nicht selbst — es lässt die Ereignisse entscheiden.
Erreicht ein Token das Gateway, werden **alle** Folge-Ereignisse gleichzeitig scharf: Für jedes
entsteht ein wartendes Token mit genau der Message-, Signal- oder Timer-Subscription, die es
auch einzeln hätte. Trifft eines ein, läuft sein Folgefluss weiter, und die übrigen Tokens
derselben Gruppe (`Token.EventGroupId`) gehen auf `Withdrawn` — damit verschwinden ihre
Subscriptions beim nächsten Speichern von selbst.

Pflichtprüfungen vor dem Speichern und Veröffentlichen:

- `bpmn.event_based_gateway.invalid_target` — ein Ausgang führt nicht zu einem wartenden
  Element. Zulässig sind `intermediateCatchEvent` mit Nachricht, Zeit oder Signal sowie
  `receiveTask`.
- `bpmn.event_based_gateway.outgoing_required` — weniger als zwei Ausgänge; dann gäbe es
  nichts zu entscheiden.
- `bpmn.event_based_gateway.condition_not_allowed` — Bedingung an einem Ausgang oder ein
  Standardfluss. Beides hätte keinen Zeitpunkt, an dem es ausgewertet würde.

**Grenzen.** Die Tokens einer Ereignisgruppe sind eine Einheit. Der Instanzumzug fasst sie in
dieser Stufe nicht an: Eine Instanz, die an einem solchen Token wartet, ist mit dem Problemcode
`EventBasedGatewayWaiting` nicht migrierbar — ein einzelnes Mitglied zu verschieben würde die
Gruppe zerreißen, und die übrigen warteten auf ein Ereignis, das niemanden mehr erreicht. Die
Gruppe als Ganzes umzuziehen bleibt offen.

### Inklusives Gateway (`inclusiveGateway`)

**Split.** Jeder Ausgang mit wahrer Bedingung bekommt ein Token — dieselbe FEEL-Auswertung wie
am exklusiven Gateway, nur nimmt das inklusive alle Treffer statt des ersten. Trifft keine
Bedingung zu, greift der Standardfluss.

**Join.** Der Join wartet auf so viele Tokens, wie der zugehörige Split aktiviert hat. Der
Split schreibt dazu eine Merkzelle an die erzeugten Tokens (`Token.InclusiveForkId` und
`Token.InclusiveForkSize`); sie wandert mit ihnen durch ihren Zweig, und der Join löst sie ein.
Ein Join ohne zugehörigen Split verhält sich wie ein paralleler Join und wartet auf alle seine
Eingänge.

Pflichtprüfungen (analog zum exklusiven Gateway):

- `bpmn.inclusive_gateway.condition_required` — ein nicht-defaultiger Ausgang eines Splits
  ohne `conditionExpression`.
- `bpmn.inclusive_gateway.default.invalid_reference` — der Standardfluss ist kein Ausgang
  dieses Gateways.

**Grenzen.** Zur Laufzeit ohne wahre Bedingung und ohne Standardfluss bricht die Mutation mit
einer `FlowzerRuntimeException` ab — dieselbe Behandlung wie heute am exklusiven Gateway.
Die Merkzelle trägt genau ein Split/Join-Paar: Verschachtelte inklusive Konstrukte verlieren
beim inneren Join den äußeren Bezug, der äußere Join fällt dann auf die parallele Auslegung
zurück. Unsymmetrische Konstrukte — ein aktivierter Zweig, der den Join gar nicht erreicht —
lassen den Join warten; die vollständige BPMN-Semantik („warten, bis kein weiterer Token
diesen Join mehr erreichen kann") ist nicht umgesetzt.

**Nebenbefund.** Parallele und inklusive Gateways lesen ihre Sequenzflüsse jetzt aus dem
Container ihres Tokens statt aus `Process.FlowElements`. Ein paralleles Gateway innerhalb
eines eingebetteten Subprozesses funktioniert damit; zuvor fand es seine Eingänge nicht.

### Event-Subprozess (`subProcess triggeredByEvent="true"`)

Ein Event-Subprozess hängt an keinem Sequenzfluss. Er ist der Ereignisfänger seines Scopes:
Solange der Prozess oder der Subprozess läuft, in dem er steht, ist sein Startereignis scharf —
wie ein Boundary-Event an einer Aktivität. Seine Nachrichten- und Signal-Subscriptions stehen
deshalb in denselben Listen wie die der Boundary-Events; ein Timer-Start erscheint als
`TimerSubscriptionKind.BoundaryEvent` und läuft ab dem Beginn seines Scopes.

- `bpmn.event_subprocess.start_required` — kein oder mehr als ein Startereignis, oder ein
  Startereignis ohne Ereignisdefinition. Zulässig sind Nachricht, Zeit, Signal, Fehler und
  Eskalation.
- `bpmn.start_event.event_subprocess_only` — ein Fehler- oder Eskalationsstart außerhalb eines
  Event-Subprozesses. Dort gibt es keinen Scope, dessen Fehler er fangen könnte.

**Semantik.** `isInterrupting="true"` (der Vorgabewert) zieht alles zurück, was im Scope noch
läuft, und der Event-Subprozess übernimmt; der Scope selbst bleibt als sein Gastgeber bestehen
und endet mit ihm. `isInterrupting="false"` startet den Event-Subprozess zusätzlich, der Scope
läuft daneben weiter, und das Ereignis bleibt scharf — eine Nachricht oder ein Signal darf also
mehrfach auslösen. Die Nutzdaten des auslösenden Ereignisses landen wie bei einem Boundary-
Event im Prozesskontext.

**Error-Start.** Ein Fehler wird jetzt an jedem Scope zuerst dessen Event-Subprozess angeboten
und erst danach dem Boundary-Event, das außen am Subprozess hängt — der Event-Subprozess liegt
innerhalb des Scopes und ist damit näher am Ursprung. Auf Prozessebene ist er der einzige
Fänger: Ohne ihn scheitert die Instanz wie bisher. Ein Fehlerstart ist immer unterbrechend.

**Grenzen.** Ein `timeCycle` an einem nicht unterbrechenden Event-Subprozess löst in dieser
Stufe nur einmal aus; ein Timer-Start gilt nach seinem Lauf als verbraucht. Kompensation,
Transaktions- und Ad-hoc-Subprozesse bleiben offen.

### Eskalation (`escalationEventDefinition`)

Eskalation ist der freundliche Bruder des Fehlers: Sie meldet, dass jemand auf höherer Ebene
entscheiden muss, **ohne** den laufenden Pfad abzubrechen. `bpmn:escalation`-Wurzelelemente
werden mit `id`, `name` und `escalationCode` gelesen; ein `escalationEventDefinition` zeigt
über `escalationRef` darauf.

Ausführbar sind `intermediateThrowEvent`, `endEvent`, `boundaryEvent` (unterbrechend **oder**
nicht unterbrechend) und das Startereignis im Event-Subprozess.

- Ein Wurf ist nicht blockierend: Das werfende Element gilt als abgeschlossen und der Prozess
  läuft weiter. Nur ein unterbrechender Fänger zieht den Pfad mit zurück.
- Gefangen wird von innen nach außen: an jedem Scope zuerst sein Event-Subprozess, dann das
  Eskalations-Boundary an ihm. Ein Fänger mit passendem Code hat Vorrang; einer ohne
  `escalationRef` fängt jede Eskalation.
- Unterbrechend zieht den Subprozess samt allem darin zurück; nicht unterbrechend öffnet den
  Eskalationspfad zusätzlich und lässt den Subprozess weiterlaufen.
- **Ungefangen verfällt die Eskalation.** Anders als beim Fehler scheitert die Instanz dadurch
  ausdrücklich nicht; der Vorgang bleibt als `InstanceEngine.UnhandledEscalations` mit Knoten
  und Code für die Diagnose erhalten.

Die früheren Platzhalter `GetActiveEscalations()` und `HandleEscalation(code, code, body)` sind
entfallen. An ihrer Stelle stehen `ActiveCatchEscalations` — was die Instanz gerade fangen kann
— und `HandleEscalation(escalationCode, data)`, das eine Eskalation von außen in die Instanz
meldet.

**Grenzen.** Es gibt keinen Weg, mit dem ein externer Worker eine Eskalation an seinem Auftrag
meldet; dafür bleibt der BPMN-Fehler. Eine ungefangene Eskalation wird nicht als eigenes
Laufzeitereignis persistiert.

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
gesondert behandeln. Eskalation und Error-Start-Events in Event-Subprozessen sind seit
Vertrag 9 umgesetzt; Kompensation bleibt offen. Der Gliederungseditor kennt
Fehlerereignisse so wenig wie die übrigen Ereignisdefinitionen und meldet sie als Blocker,
statt sie beim Speichern zu verlieren.

## Business-Rule-Task (Vertrag 8)

Version 8 erweitert Version 7 additiv um genau einen Eintrag: `businessRuleTask` ist
ausführbar. Die älteren Vertragsdateien bleiben unverändert und sagen Business-Rule-Tasks
weiterhin nicht zu.

Ein Business-Rule-Task hat wie in Camunda 8 **zwei** Arten, und genau eine davon muss am
Element stehen:

- `zeebe:calledDecision` mit `decisionId` und `resultVariable` — Flowzer wertet die
  Entscheidung selbst aus, lokal und in derselben Transaktion.
- `zeebe:taskDefinition` mit `type` — der Task ist ein Auftrag für einen externen Worker und
  läuft über denselben Weg wie ein Service-Task (`IFlowzerWorkerTask`).

Ist ein Auftragstyp gesetzt, gilt der Worker-Weg. Sonst sind beide Angaben der
`calledDecision` Pflicht:

- `bpmn.business_rule_task.decision_required` — `zeebe:calledDecision/@decisionId` fehlt oder
  ist leer.
- `bpmn.business_rule_task.result_variable_required` — `zeebe:calledDecision/@resultVariable`
  fehlt oder ist leer. Ohne Namen wüsste der Prozess nicht, wo das Ergebnis steht.

Ob die genannte Entscheidung im Katalog existiert, wird beim Deployment ausdrücklich
**nicht** geprüft — genau wie beim aufgerufenen Prozess einer Aufruf-Aktivität: Sie darf
später entstehen, und welche Version gilt, entscheidet der Zeitpunkt des Aufrufs. Eine
fehlende Entscheidung ist der BPMN-Fehler `DECISION_NOT_FOUND` am Task.

Variablenfluss, Fehlercodes, Katalog und die Grenzen dieser Stufe stehen vollständig in
[DMN.md](DMN.md).

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
- Worker-Typ für Service-Tasks und Zeitangabe für Timer
- dieselben Regeln separat innerhalb jedes lokalen Subprozesses

Boundary-Events sind Aktivierungswurzeln und benötigen naturgemäß keinen eingehenden
Sequenzfluss. Container ohne eigenes StartEvent bleiben für die vorhandene eingebettete
Subprozess-Semantik kompatibel.

### Warnungen statt Blockern

Die Prüfung kennt neben Fehlern **Warnungen**: Befunde, die die Veröffentlichung
ausdrücklich nicht verhindern, aber mitgeteilt werden. Sie stehen im Erfolgsfall unter
`warnings` — bei `POST /definition/validate`, `/definition/validate/deployment`,
`POST /definition` und `POST /definition/deploy`. Der Eintrag trägt denselben
elementbezogenen Vertrag wie ein Fehler (`code`, `elementId`, `propertyPath`, `message`)
und zusätzlich `severity: "warning"`, damit eine Modellieransicht denselben Knoten
anspringen kann. Eine abgelehnte Veröffentlichung meldet ihre Fehler, nicht ihre Hinweise.

**Ein User-Task ohne Formularbindung ist veröffentlichbar.** Bis zum 19. September 2026
verlangte Flowzer an jedem `bpmn:userTask` ein `zeebe:formDefinition` mit `formKey` oder
`formId` und lehnte andernfalls mit `bpmn.user_task.form_required` ab. Das lehnte jedes
werkzeugneutrale Modell ab — alle 22 MIWG-Referenzmodelle mit menschlicher Aufgabe und
jeden Camunda-Import. Stattdessen meldet die Prüfung jetzt die Warnung
`bpmn.user_task.form_missing` am Knoten. Die Aufgabe entsteht als gewöhnliche Human Task
und wird ohne Eingaben abgeschlossen; Details in
[Human-Task-Lifecycle](HUMAN-TASK-LIFECYCLE.md). Der alte Code entfällt ersatzlos.

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
