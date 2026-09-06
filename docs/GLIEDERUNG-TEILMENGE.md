# Gliederungsansicht — abgedeckte BPMN-Teilmenge

Status: **Prototyp**. Diese Datei legt fest, welchen Ausschnitt von BPMN die
strukturierte Gliederungsansicht (`/workflows/<id>/gliederung`) lesen und
zurückschreiben kann — und was passiert, wenn ein Modell darüber hinausgeht.

## Wozu die Ansicht da ist

Die Gliederung ist eine **zweite Oberfläche neben dem Diagramm**, kein Ersatz.
Das Diagramm bleibt die Expertenansicht: Es kann alles, was bpmn-js kann,
importiert Modelle aus dem Camunda Modeler unverändert und bleibt der Weg für
alles, was die Gliederung nicht abbildet.

Die Gliederung richtet sich an Fachleute, die einen Workflow **lesen und
nachjustieren**, nicht zeichnen: Schritte untereinander, parallele Blöcke
eingerückt, Tore als Verzweigung mit ihren Bedingungen, Formular, Zuweisung
und Frist direkt am Schritt bearbeitbar.

## Die harte Regel

> Ein Modell, das die Gliederung nicht vollständig abbilden kann, darf sie
> nicht speichern dürfen.

Das ist keine Absichtserklärung, sondern zweifach abgesichert:

1. **Verstandene Bestandteile sind aufgezählt.** Der Leser kennt eine
   Positivliste von Elementen und Attributen. Alles, was nicht darauf steht —
   ein unbekanntes Element, ein unbekanntes Attribut, eine fremde
   Erweiterung — wird als Meldung ausgegeben.
2. **Rückübersetzungsprobe.** Nach dem Lesen wird die Gliederung sofort wieder
   nach BPMN geschrieben und der entstandene Graph mit dem Ausgangsgraphen
   verglichen (Knoten mit Typ, Name und Eigenschaften; Flüsse mit Quelle,
   Ziel, Name, Bedingung und Kennung). Weicht etwas ab, ist das ein Blocker,
   auch wenn die Positivliste zufrieden war.

Solange eine Meldung der Stufe **Blocker** offen ist, zeigt die Oberfläche die
Gliederung nur lesend und bietet weder Speichern noch Deployen an. Der Weg
über das Diagramm bleibt offen.

## Abgedeckt

### Aufbau

| Konstrukt | Bedingung |
|---|---|
| `bpmn:definitions` | genau ein `bpmn:process` mit `isExecutable="true"` |
| `bpmn:startEvent` | genau eines, ohne Ereignisdefinition |
| `bpmn:endEvent` | beliebig viele, ohne Ereignisdefinition |
| `bpmn:sequenceFlow` | mit `name` und `conditionExpression` |
| `bpmndi:BPMNDiagram` | wird gelesen, aber nicht ausgewertet (siehe „Anordnung") |

### Schritte

| Konstrukt | Ausgewertete Angaben |
|---|---|
| `bpmn:userTask` | `zeebe:formDefinition/@formKey` oder `@formId`, `zeebe:assignmentDefinition/@assignee`, `@candidateGroups`, `@candidateUsers`, `zeebe:taskSchedule/@dueDate`, `@followUpDate`, `zeebe:ioMapping` |
| `bpmn:serviceTask` | `zeebe:taskDefinition/@type`, `@retries`, `zeebe:ioMapping` |

### Ablaufstrukturen

| Konstrukt | Bedingung |
|---|---|
| **Folge** | Schritte hintereinander; jeder Schritt hat genau einen Eingang und genau einen Ausgang |
| **Gleichzeitig** (`bpmn:parallelGateway`) | Ein Fork mit *n* Ausgängen, dessen Zweige sich vollständig an genau einem Join mit *n* Eingängen und einem Ausgang wieder treffen |
| **Verzweigung** (`bpmn:exclusiveGateway`) | Ein Tor mit *n* Ausgängen; jeder Ausgang trägt eine Bedingung oder ist über `@default` der Standardweg |
| **Zusammenführung** | Optionales `bpmn:exclusiveGateway` mit *n* Eingängen und einem Ausgang; alternativ treffen sich die Zweige direkt an einem gemeinsamen Folgeschritt |

### Zweige, die enden, und Zweige, die weiterlaufen

Ein Zweig einer Verzweigung hat genau zwei mögliche Ausgänge:

- Er **endet** mit einem eigenen Ende-Ereignis, oder
- er **läuft weiter** an der Stelle, an der die Verzweigung in der
  übergeordneten Folge zu Ende ist.

Damit lässt sich die verbreitete Prüfkette abbilden, bei der mehrere Tore
hintereinander auf denselben gemeinsamen Abschluss zeigen — genau die Form des
Urlaubsantrags:

```
Wenn „Genug Urlaubstage?"
  ja ─ Wenn „Fachlich genehmigt?"
         ja ─ Wenn „Vertretung frei?"
                ja ─ Gleichzeitig: … → Ende „Urlaub genehmigt"
                sonst „Vertretung im Urlaub" ─ weiter mit „Ablehnung mitteilen"
         sonst „abgelehnt" ─ weiter mit „Ablehnung mitteilen"
  sonst „nicht genug Tage" ─ weiter mit „Ablehnung mitteilen"
Ablehnung mitteilen
Ende „Antrag abgelehnt"
```

Der leere Zweig ist im BPMN ein direkter Fluss vom Tor auf den gemeinsamen
Schritt. Die Gliederung zeigt ihn als „weiter mit …", ohne den Schritt zu
verdoppeln.

## Nicht abgedeckt — wird gemeldet

Die folgenden Konstrukte führen zu einem **Blocker**. Sie sind nicht
verboten; sie werden nur im Diagramm bearbeitet.

- Teilprozesse (`subProcess`), Aufruf-Aktivitäten (`callActivity`)
- angeheftete Ereignisse (`boundaryEvent`), Zwischenereignisse
- Ereignisdefinitionen jeder Art an Start und Ende (Timer, Nachricht, Signal,
  Fehler, Eskalation, Abbruch)
- `scriptTask`, `businessRuleTask`, `manualTask`, `receiveTask`, `sendTask`,
  `task`
- `inclusiveGateway`, `eventBasedGateway`, `complexGateway`
- Mehrfachausführung (`multiInstanceLoopCharacteristics`)
- Bahnen und Pools (`laneSet`, `collaboration`, `participant`)
- Datenobjekte, Datenspeicher, Artefakte, Anmerkungen
- mehrere ausführbare Prozesse in einer Datei
- Nachrichten- und Signaldefinitionen auf Wurzelebene
- Rücksprünge und Schleifen (jeder Zyklus im Graphen)
- Graphen, die sich nicht in Folge, Gleichzeitig und Verzweigung zerlegen
  lassen — etwa ein Fork, dessen Zweige sich nicht an einem gemeinsamen Join
  treffen, oder ein Sprung von einem Zweig in einen anderen

## Pflichtangaben am Schritt

Die Engine weist ein Modell zurück, dessen `userTask` kein Formular oder dessen
`serviceTask` keinen `zeebe:taskDefinition/@type` nennt. Die Gliederung prüft das
vor dem Speichern und meldet es als Blocker, statt die Fehlermeldung der API
abzuwarten. Ein vorhandenes Modell mit dieser Lücke bleibt trotzdem lesbar —
sonst könnte man sie in der Gliederung gar nicht erst schließen.

## Anordnung im Diagramm

Die Gliederung kennt keine Koordinaten. Beim Schreiben gilt:

- Ist die **Struktur unverändert** — dieselben Knoten, dieselben Flüsse; nur
  Namen, Formulare, Zuweisungen, Fristen oder Bedingungen bearbeitet —, bleibt
  das vorhandene `bpmndi:BPMNDiagram` unverändert erhalten.
- Wurde die **Struktur geändert**, wird die gesamte Anordnung neu berechnet
  (Ebenen von links nach rechts, Zeilen von oben nach unten). Eine von Hand
  im Diagramm gepflegte Anordnung geht dabei verloren. Die Oberfläche sagt
  das vor dem Speichern an.

## Bekannte Grenzen des Prototyps

- Die Neuberechnung der Anordnung ist brauchbar, aber nicht schön: Kanten
  werden orthogonal in drei Segmenten geführt und nicht auf Kreuzungsfreiheit
  optimiert.
- Verzweigungen ohne gemeinsames Ende und ohne gemeinsamen Folgeschritt
  („jeder Zweig läuft in sein eigenes Ende") sind abgedeckt, verschachtelte
  Sprünge zwischen Zweigen nicht.
- Die Ein- und Ausgangszuordnungen (`zeebe:ioMapping`) werden angezeigt und
  erhalten, aber im Prototyp nicht bearbeitet.
- Die Gliederung bearbeitet immer die neueste gespeicherte Version, genau wie
  der Modeler.

## Wo der Code steht

| Zweck | Datei |
|---|---|
| Datenmodell der Gliederung | `src/FlowzerConsole/src/lib/outline/model.ts` |
| BPMN-XML zu Graph, Positivliste | `src/FlowzerConsole/src/lib/outline/graph.ts` |
| Graph zu Gliederung | `src/FlowzerConsole/src/lib/outline/read.ts` |
| Gliederung zu BPMN-XML | `src/FlowzerConsole/src/lib/outline/write.ts` |
| Anordnung der DI-Koordinaten | `src/FlowzerConsole/src/lib/outline/layout.ts` |
| Bearbeiten (einfügen, verschieben, löschen) | `src/FlowzerConsole/src/lib/outline/edit.ts` |
| Oberfläche | `src/FlowzerConsole/src/pages/OutlinePage.tsx`, `src/FlowzerConsole/src/components/outline/` |
