# Versionierter BPMN-Fähigkeitsvertrag

Flowzer führt nur eine bewusst begrenzte BPMN-Teilmenge aus. Der Vertrag
`flowzer.bpmn-capabilities/2` liegt maschinenlesbar unter
`contracts/bpmn-capabilities/v2.json` und unterscheidet je Elementart. Version 1 bleibt
unverändert als historischer Vertrag erhalten. Der aktuelle Vertrag unterscheidet:

- **modelable:** Der BPMN-Modeler kann das Element darstellen beziehungsweise erzeugen.
- **parsable:** Der bestehende Parser kann das Element lesen, etwa für historische Modelle.
- **executable:** Neue Workflow-Versionen dürfen das Element tatsächlich ausführen.

`parsable` ist ausdrücklich kein Ausführungsversprechen. Beispielsweise bleiben Script-
Tasks und Call Activities für Bestandsanalyse lesbar, werden aber vor Save oder Deploy als
nicht ausführbar abgelehnt. Die einzige vorübergehende, explizite Ausnahme ist der vollständig
geprüfte KI-Autorenvertrag aus #242: Er darf als Entwurf gespeichert werden, bleibt bis zur
Runtime jedoch nicht deploybar. Flowzer errät keine Fähigkeiten aus einer konsumierenden
Anwendung; der Vertrag ist vollständig hostneutral.

## Öffentliche API

- `GET /definition/capabilities` liefert den aktuellen Vertrag.
- `POST /definition/validate?deployment=false` prüft einen speicherbaren Autorenstand,
  `deployment=true` prüft die strengere ausführbare Teilmenge.
- `POST /definition` und `POST /definition/deploy` erzwingen die jeweils passende Prüfung innerhalb
  ihres serverseitigen Anwendungsfalls. Eine Browser-Vorprüfung ist daher keine
  Sicherheitsgrenze.

Ein Modellfehler antwortet mit `422 application/problem+json`, dem Hauptcode
`bpmn.model.invalid`, der Vertragsversion sowie `issues`. Jeder Befund enthält einen
stabilen Code, Schweregrad, Nachricht und – soweit möglich – `elementId` und
`propertyPath`. Die Konsole zeigt diese Befunde dauerhaft, markiert den BPMN-Knoten und
macht ihn per Tastatur beziehungsweise Klick anwählbar. Die Gliederung kann zum selben
Knoten im Diagramm wechseln.

Version 2 meldet bewusst den ersten Fehler in deterministischer Dokumentreihenfolge.
Nach der Korrektur kann der identische Endpunkt erneut aufgerufen werden. Eine spätere
Mehrfachdiagnose ist eine additive Vertragsweiterentwicklung, kein Grund, heute Parser-
oder Laufzeittexte als Clientvertrag zu verwenden.

## Ausführbares Profil v2

Offiziell ausführbar sind:

- Plain-, Message-, Signal- und Timer-Start
- Plain- und Terminate-Ende
- User-, Service-, Receive- und generische Tasks
- exklusive und parallele Gateways
- Sequenzflüsse und lokale Subprozesse
- Message-, Signal- und Timer-Intermediate-Catch-/Boundary-Events

Insbesondere nicht als ausführbar zugesagt sind Script-/Manual-Tasks, Call Activities,
Inclusive-/Complex-Gateways, Intermediate-Throw-Events, Message-/Signal-End-Events sowie
Error-/Escalation-Pfade. Diese Grenzen werden erweitert, wenn der jeweilige Runtime-Pfad
mit Semantik-, Recovery- und Konkurrenztests belegt ist – nicht bereits dann, wenn der
Parser XML lesen kann.

`serviceTask.aiTask` ist bereits modellierbar und parsebar, aber noch nicht ausführbar.
Seine vollständigen Vertrags- und Bindungsregeln stehen in [AI-TASKS.md](AI-TASKS.md).

## Statische Prüfungen

Neben der Elementmatrix prüft Version 2 vor Save und Deploy:

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

Der OpenAPI-Snapshot und die generierten TypeScript-Schemas werden gemeinsam aktualisiert.
Konkrete Hosts können die generische API oder die öffentlichen Pakete verwenden, werden
aber weder im Flowzer-Modell noch in Flowzers Laufzeit referenziert.
