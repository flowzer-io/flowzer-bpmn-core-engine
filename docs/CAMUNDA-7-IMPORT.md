# Camunda-7-Modelle nach Flowzer übernehmen

Camunda 7 Community ist abgekündigt. Wer weiterziehen will, hat in aller Regel dasselbe
Problem: Die Prozesse sind gezeichnet, gelebt und dokumentiert — sie noch einmal zu malen,
kann sich niemand leisten. Flowzer kann eine Camunda-7-BPMN-Datei deshalb **importieren
und übersetzen**.

Flowzer spricht Camunda-8-Vokabular (`zeebe:*`, Namensraum
`http://camunda.org/schema/zeebe/1.0`). Camunda-7-Modelle tragen `camunda:*`
(`http://camunda.org/schema/1.0/bpmn`). Der Import übersetzt, was eine Entsprechung hat,
entfernt, was keine hat, und **sagt beides**. Was verloren geht, steht im Bericht, bevor
der Workflow angelegt wird.

Der Import ist kein Versprechen, dass der Prozess danach läuft. Er ist ein Versprechen,
dass niemand raten muss, woran es liegt.

## Vorgehen

1. In der Konsole **Workflows → BPMN-Datei importieren** (verlangt die Rolle fürs
   Modellieren beziehungsweise die Bearbeitungsberechtigung für den offenen Ordner).
2. Die `.bpmn`-Datei wählen. Trägt sie den Camunda-7-Namensraum, wird sie übersetzt.
   Eine gewöhnliche BPMN-Datei läuft durch denselben Weg, nur ohne Übersetzung.
3. Den **Bericht** lesen. Drei Gruppen, je Element eine Zeile:
   - **Übernommen** — die Angabe wirkt in Flowzer weiter.
   - **Nacharbeit nötig** — hier geht Verhalten verloren, oder die Veröffentlichung wird
     den Schritt ablehnen.
   - **Entfallen** — entfernt, weil Flowzer dafür keine Entsprechung hat.
4. Den Namen prüfen (vorbelegt aus `bpmn:process/@name`, sonst dem Dateinamen) und
   **Als neuen Workflow anlegen**. Das Modell wird als erste Version gespeichert,
   **nicht veröffentlicht** — was der Bericht als nachzuarbeiten meldet, würde die
   Veröffentlichung ohnehin ablehnen.
5. Im Modellierer nacharbeiten: Formulare anlegen, Korrelationsschlüssel ergänzen,
   nicht ausführbare Schritte ersetzen. Dann veröffentlichen.

Der Bericht wird **nicht gespeichert**. Er steht im Dialog, bis jemand ihn schließt. Wer
ihn später noch braucht, importiert die Datei erneut — die Übersetzung ist reproduzierbar
und der zweite Lauf über ein bereits übersetztes Modell ändert nichts mehr.

## Mapping

### Service-Tasks

| Camunda 7 | Flowzer | Bericht |
|---|---|---|
| `camunda:type="external"` + `camunda:topic="X"` | `zeebe:taskDefinition type="X"` | übernommen |
| `camunda:class="de.example.LagerBuchenDelegate"` | `zeebe:taskDefinition type="LagerBuchenDelegate"` | übernommen **und** Nacharbeit |
| `camunda:delegateExpression="${lagerDelegate}"` | `zeebe:taskDefinition type="lagerDelegate"` | übernommen **und** Nacharbeit |
| `camunda:expression="${bean.tuEtwas()}"` | `zeebe:taskDefinition type="bean.tuEtwas()"` | übernommen **und** Nacharbeit |
| `camunda:type` mit anderem Wert | — | Nacharbeit |

Der Auftragstyp aus einem Delegate ist ein **Vorschlag**, kein Ersatz: Der Java-Code läuft
in Flowzer nicht. Für den genannten Typ muss ein Worker entstehen (siehe unten). Der Name
ist bewusst der aus dem Modell abgeleitete, damit sich Modell und neuer Dienst noch
zuordnen lassen.

### Menschliche Aufgaben

| Camunda 7 | Flowzer | Bericht |
|---|---|---|
| `camunda:assignee` | `zeebe:assignmentDefinition/@assignee` | übernommen |
| `camunda:candidateUsers` | `zeebe:assignmentDefinition/@candidateUsers` | übernommen |
| `camunda:candidateGroups` | `zeebe:assignmentDefinition/@candidateGroups` | übernommen |
| `camunda:dueDate` | `zeebe:taskSchedule/@dueDate` | übernommen |
| `camunda:followUpDate` | `zeebe:taskSchedule/@followUpDate` | übernommen |
| `camunda:formKey`, `camunda:formRef` | entfernt | Nacharbeit |
| `camunda:priority` | — | entfallen |

Zuweisungen werden als **Freitext** übernommen — genau wie sie in Camunda 7 standen.
Flowzers stabile Verzeichnisreferenzen (`flowzer:taskAssignment`) entstehen erst, wenn im
Eigenschaften-Panel eine Identität ausgewählt wird. Ein automatisch geratener
Verzeichnistreffer wäre eine Behauptung über Personen, die niemand geprüft hat.

### Zuordnungen

`camunda:inputOutput` wird zu `zeebe:ioMapping`. Camunda 7 nennt im `name` die
Zielvariable und im Text den Ausdruck — dieselbe Aufteilung wie Zeebes `target`/`source`.

```xml
<!-- Camunda 7 -->
<camunda:inputParameter name="kundennummer">${bestellung.kundennummer}</camunda:inputParameter>
<!-- Flowzer -->
<zeebe:input source="=bestellung.kundennummer" target="kundennummer" />
```

Ein Parameter mit `camunda:script`, `camunda:list` oder `camunda:map` wird entfernt und
gemeldet: Flowzer kennt an dieser Stelle nur einen Ausdruck, und ein auf die Hälfte
eingedampfter Parameter wäre schlimmer als gar keiner.

### Ausdrücke: JUEL nach FEEL

| JUEL | FEEL |
|---|---|
| `${x}` | `=x` |
| `&&` | `and` |
| `\|\|` | `or` |
| `==` | `=` |
| `!=` | `!=` (unverändert) |
| `!bezeichner` | `not(bezeichner)` |
| `execution.getVariable("x")` | `x` |
| reiner Literaltext | unverändert |

Dieselbe Regel gilt für `conditionExpression` an Sequenzflüssen und für die
`completionCondition` einer Mehrfachausführung.

Nicht übersetzt und deshalb gemeldet:

- **Methodenaufrufe** (`bestellung.istEilig()`). FEEL kennt sie nicht; der Ausdruck bleibt
  stehen, bis jemand entscheidet, was an seine Stelle tritt.
- **Mehrdeutige Verneinung** (`!(a && b)`). Ein `!` vor einer Klammer wird nicht geraten.
- **Gemischter Text** (`Hallo ${name}`). Weder Literal noch Ausdruck — er bleibt
  unverändert, statt stillschweigend die Bedeutung zu wechseln.

Der führende Gleichheitsstrich ist in Zeebes Schreibweise der Unterschied zwischen einem
Ausdruck und einem Festwert. Ein Literal bleibt deshalb absichtlich ohne ihn.

### Mehrfachausführung

| Camunda 7 | Flowzer |
|---|---|
| `camunda:collection="${positionen}"` | `zeebe:loopCharacteristics/@inputCollection="=positionen"` |
| `camunda:elementVariable="position"` | `zeebe:loopCharacteristics/@inputElement="position"` |

Camunda 7 nimmt an der Sammlung auch einen bloßen Variablennamen an; in FEEL ist beides
ein Ausdruck und bekommt deshalb das führende `=`.

### Ohne Entsprechung

Ersatzlos entfernt, als **entfallen** gemeldet:

`camunda:asyncBefore`, `camunda:asyncAfter`, `camunda:exclusive`, `camunda:jobPriority`,
`camunda:historyTimeToLive`, `camunda:versionTag`, `camunda:candidateStarterGroups`,
`camunda:candidateStarterUsers`.

Diese Angaben steuern in Camunda 7 die Ausführung. Flowzer entscheidet über Haltepunkte,
Priorisierung, Aufbewahrung und Startberechtigung an anderer Stelle — im Betrieb
beziehungsweise über Rollen und Ordner.

Entfernt **und** zusätzlich zur Nacharbeit gemeldet, weil mit ihnen Verhalten verschwindet:

| Camunda 7 | Was zu tun ist |
|---|---|
| `camunda:executionListener` | Als eigenen Schritt modellieren — Service-Task mit Worker |
| `camunda:taskListener` | Dito; die Wirkung muss im Ablauf sichtbar werden |
| `camunda:properties` | Werden nicht ausgewertet; Fachdaten gehören in Variablen |
| `camunda:failedJobRetryTimeCycle` | Über `zeebe:taskDefinition/@retries` und die Rückmeldung des Workers |
| `camunda:connector` | Der Aufruf gehört in einen eigenen Worker |

Eine `camunda:*`-Angabe, die der Importer **nicht** kennt, wird nicht entfernt. Sie bleibt
im Modell stehen und wird gemeldet. Nur wenn kein Rest bleibt, verschwindet auch die
Namensraumdeklaration `xmlns:camunda`.

### Was Flowzer nicht ausführt

Der Importer meldet vorab, was der [Fähigkeitsvertrag](BPMN-CAPABILITIES.md) als nicht
ausführbar führt — die Veröffentlichung lehnt es später ohnehin ab, aber dann ist die
Migration schon halb gemacht:

Skript-Tasks, Aufruf-Aktivitäten, Entscheidungsaufgaben (DMN), einschließende, komplexe
und ereignisbasierte Tore, aussendende Zwischenereignisse sowie Fehler-, Eskalations- und
Kompensationspfade.

`camunda:decisionRef` einer Entscheidungsaufgabe wird entfernt; der Name der Entscheidung
steht im Bericht, damit die Regel nachgebaut werden kann. **DMN wird nicht importiert.**

### Nachrichten

Nachrichten-, Signal- und Zeitdefinitionen, Sequenzflüsse, Tore, Subprozesse und
Boundary-Events bleiben unverändert; ebenso `isExecutable` und der gesamte Diagrammteil
(`bpmndi`, `dc`) — die Zeichnung ist der halbe Wert eines Bestandsmodells.

Eine Sache fehlt aber und kann nicht aus der Datei kommen: der **Korrelationsschlüssel**.
Camunda 7 korreliert Nachrichten über die Laufzeit-API (`correlateMessage` mit
Geschäftsschlüssel oder Variablen). In Flowzer gehört der Schlüssel ins Modell:

```xml
<bpmn:message id="Message_Zahlung" name="Zahlungseingang">
  <bpmn:extensionElements>
    <zeebe:subscription correlationKey="=bestellnummer" />
  </bpmn:extensionElements>
</bpmn:message>
```

Der Importer meldet das an jedem wartenden Nachrichtenereignis (Zwischenereignis,
Boundary-Event, Receive-Task). Ohne den Schlüssel weiß Flowzer nicht, auf welche Instanz
eine eintreffende Nachricht gehört.

## External Tasks ↔ Flowzer-Aufträge

Der External-Task-Vertrag von Camunda 7 ist der Teil, der sich am besten überträgt: Beide
Seiten holen Arbeit ab, halten sie für eine Frist und melden das Ergebnis zurück.
Ausführlich steht das in [SERVICE-TASK-WORKER.md](SERVICE-TASK-WORKER.md); hier die
Gegenüberstellung.

| Camunda 7 | Flowzer |
|---|---|
| `POST /external-task/fetchAndLock` | `POST /job/fetch` |
| `POST /external-task/{id}/complete` | `POST /job/{jobId}/complete` |
| `POST /external-task/{id}/failure` | `POST /job/{jobId}/fail` |
| `POST /external-task/{id}/extendLock` | `POST /job/{jobId}/lease` |
| `POST /external-task/{id}/bpmnError` | — (Flowzer setzt Error-Semantik noch nicht um) |
| `POST /external-task/{id}/unlock` | — (die Frist läuft ab und gibt den Auftrag frei) |
| `topicName` | `type` (aus `zeebe:taskDefinition/@type`) |
| `workerId` | `workerId` |
| `lockDuration` (Millisekunden) | `lockSeconds` (1 bis 3.600) |
| `maxTasks` | `maxJobs` |
| `retries`, `retryTimeout` im `failure` | `retryBackoffSeconds` im `fail`; Versuche aus `zeebe:taskDefinition/@retries` |
| `variables` beim Abholen einschränken | `zeebe:ioMapping`-Eingaben am Task |
| Long-Polling (`asyncResponseTimeout`) | Webhook-Anmeldung (`POST /job/webhook`) |

```http
POST /job/fetch
{ "type": "bonitaet-pruefen", "workerId": "bonitaet-1", "maxJobs": 10, "lockSeconds": 300 }

POST /job/{jobId}/complete
{ "workerId": "bonitaet-1", "variables": { "bonitaetScore": 72 } }

POST /job/{jobId}/fail
{ "workerId": "bonitaet-1", "errorMessage": "Auskunftei nicht erreichbar", "retryBackoffSeconds": 30 }
```

Drei Unterschiede, die in der Migration wirklich auffallen:

- **Die Rolle.** Alle `/job`-Endpunkte verlangen die Rolle `worker`. Ein Auftrag enthält
  Prozessdaten; wer nur Aufgaben bearbeitet, soll sie nicht lesen können.
- **Der Umfang der Daten.** Ohne `zeebe:ioMapping` bekommt der Worker **alle**
  Prozessvariablen. Für eine Anbindung an ein Fremdsystem ist die Deklaration der bessere
  Weg — sie ist am Modell ablesbar und begrenzt, was das Haus verlässt.
- **Die Sperre gehört der Person samt Worker-Kennung**, nicht der Kennung allein. Eine
  geratene `workerId` genügt nicht, um fremde Aufträge zurückzumelden.

Ein Java-Delegate ist kein External Task. Aus ihm wird beim Import ein Auftragstyp, aber
der Code dahinter muss als eigener Dienst neu entstehen. Das ist der aufwendigste Teil
jeder Migration und der Grund, warum der Bericht ihn einzeln je Aufgabe nennt.

## Formulare

Camunda-7-Formulare kommen **nicht** mit. Weder eingebettetes HTML
(`embedded:app:forms/…`), noch Camunda Forms (`camunda-forms:deployment:…`), noch ein
Verweis auf eine fremde Anwendung, noch `camunda:formRef` auf den Formularbestand.

Flowzer hat einen eigenen Formulareditor (Form.io). Formulare werden dort neu erstellt und
beim Veröffentlichen unveränderlich an die Definitionsversion gebunden — Details in
[FORM-DEPLOYMENT-BINDINGS.md](FORM-DEPLOYMENT-BINDINGS.md) und
[FORM-AUTHORING.md](FORM-AUTHORING.md). Der Importer entfernt den alten Verweis deshalb
und meldet ihn samt seinem bisherigen Wert: Bliebe er stehen, stünde im Modell eine
Bindung, die beim Veröffentlichen ins Leere liefe.

Praktisch heißt das: Die Feldlisten der alten Formulare sind die Vorlage, der Nachbau ist
Handarbeit. Wer viele Formulare hat, sollte damit anfangen — nicht mit dem BPMN.

## Historie und laufende Instanzen

**Beides wird nicht importiert.** Der Importer nimmt eine Modelldatei, sonst nichts.

- **Laufende Instanzen** bleiben in Camunda 7. Es gibt keinen Weg, einen Token aus einer
  fremden Engine zu übernehmen: Variablen, Jobs, Sperren, Timer und Zuweisungen hängen an
  der jeweiligen Laufzeit. Der übliche Weg ist, in Camunda 7 keine neuen Instanzen mehr zu
  starten, die laufenden dort auslaufen zu lassen und neue in Flowzer zu beginnen.
  Flowzers [Instanzmigration](INSTANCE-MIGRATION.md) hebt laufende Instanzen zwischen
  *Flowzer*-Versionen — sie ist kein Weg aus Camunda 7 heraus.
- **Historie** bleibt ebenfalls dort. Flowzers Vorgangshistorie ist bewusst klein und
  beginnt mit der ersten Instanz in Flowzer; sie beschreibt, was diese Engine getan hat
  (siehe [PROCESS-HISTORY.md](PROCESS-HISTORY.md)). Eine importierte Fremdhistorie wäre
  eine Behauptung über Vorgänge, die Flowzer nie ausgeführt hat. Wer die alten Vorgänge
  nachweisen muss, hält die Camunda-7-Datenbank lesend vor oder exportiert sie vorher.

## Grenzen

- **Kein DMN-Import.** Entscheidungstabellen müssen als Service-Task oder als Tor
  nachgebaut werden.
- **Kein Camunda-8-Import.** `zeebe:*`-Modelle liest Flowzer nativ; sie gehen denselben
  Weg durch den Import, ohne übersetzt zu werden.
- **Kein Import von Skripten.** Skript-Tasks bleiben im Modell, sind aber nicht
  ausführbar.
- **Zuweisungen bleiben Freitext**, bis jemand sie im Panel auf Verzeichnisidentitäten
  umstellt.
- Scheitert das Speichern des Modells nach dem Anlegen, bleibt der leere Katalogeintrag
  stehen. Er ist sichtbar und lässt sich löschen; still aufgeräumt wird er nicht, weil das
  die einzige Spur des Versuchs beseitigte.

## Wo das steht

| Datei | Aufgabe |
|---|---|
| `src/FlowzerConsole/src/lib/modeling/camunda7Import.ts` | Die Übersetzung. Reiner Text, kein React, keine bpmn-js-Instanz |
| `src/FlowzerConsole/src/lib/modeling/bpmnImport.ts` | Prozessname und Definitionskennung einer importierten Datei |
| `src/FlowzerConsole/src/components/workflows/ImportWorkflowDialog.tsx` | Dateiwahl, Bericht, Anlegen |

Die Zielnamen der Übersetzung folgen dem, was Flowzer wirklich liest:
`src/core-engine/ModelParser.cs` für die Laufzeit und
`src/FlowzerConsole/src/components/bpmn/elementProperties.ts` für das Panel. Ein Test liest
das Übersetzungsergebnis mit genau diesen Lesefunktionen zurück — eine Angabe, die keiner
von beiden findet, fällt dort auf und nicht erst im Betrieb.
