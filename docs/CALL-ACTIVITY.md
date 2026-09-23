# Lokale Call Activity: ein Prozess ruft einen anderen auf

Eine Aufruf-Aktivität (`bpmn:callActivity`) startet einen anderen Prozess **derselben
Installation** und wartet auf dessen Ende. Sie ist damit der Baustein, mit dem sich ein großer
Vorgang aus kleineren, eigenständig modellierten und eigenständig versionierten Prozessen
zusammensetzen lässt.

Dieses Dokument beschreibt Stufe 1 von [#154 „Prozessverbund"](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/154):
den **lokalen** Aufruf. Der Fernaufruf in eine andere Flowzer-Installation setzt darauf auf und
ändert an der hier beschriebenen Semantik nichts — er ergänzt sie nur um eine Verbindungsangabe
am selben BPMN-Element.

## Entscheidung

Flowzer hatte die Call Activity bisher in Modell und Parser, aber ohne jeden Laufzeitpfad; der
Fähigkeitsvertrag lehnte sie deshalb vor der Veröffentlichung ab. Statt für Stufe 1 einen
eigenen Mechanismus zu bauen, folgt der Aufruf genau dem Muster, das für ausgehende Nachrichten
schon steht (siehe `BpmnBusinessLogic.OutgoingMessages.cs`):

> **Die Engine stellt etwas bereit, die Geschäftslogik führt es in derselben Transaktion aus.**

Die Engine kennt bewusst keine anderen Instanzen. Sie legt einen *ausstehenden Aufruf* bereit;
die Kindinstanz zu starten, ist Sache der aufrufenden Schicht. Das hält die Engine frei von
Ablage- und Katalogwissen und macht denselben Code später für den Fernaufruf brauchbar, bei dem
die Gegenseite gar nicht im eigenen Prozess läuft.

## Modellierung

```xml
<bpmn:callActivity id="Aufruf" name="Second-Level-Support">
  <bpmn:extensionElements>
    <zeebe:calledElement processId="second-level-support"
                         propagateAllParentVariables="false"
                         propagateAllChildVariables="false" />
    <zeebe:ioMapping>
      <zeebe:input source="=antragsnummer" target="ticketNummer" />
      <zeebe:output source="=loesung" target="loesungSecondLevel" />
    </zeebe:ioMapping>
  </bpmn:extensionElements>
</bpmn:callActivity>
```

`processId` ist die `bpmn:process/@id` des aufgerufenen Prozesses — **nicht** die Kennung des
Workflows im Katalog.

### Veröffentlichungsvertrag

Vor dem Speichern und Veröffentlichen prüft [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md)
(Vertrag 7):

| Code | Bedeutung |
|---|---|
| `bpmn.call_activity.process_id_required` | `zeebe:calledElement/@processId` fehlt oder ist leer. |
| `bpmn.call_activity.process_id_literal_required` | Die Prozesskennung beginnt mit `=`, ist also ein FEEL-Ausdruck. In dieser Stufe ist nur ein Literal erlaubt. |

Ob der aufgerufene Prozess **existiert**, wird beim Deployment ausdrücklich *nicht* geprüft. Er
darf später entstehen, und welche Version gilt, entscheidet erst der Zeitpunkt des Aufrufs. Ein
fehlender Zielprozess ist deshalb ein Laufzeitfehler (`CALLED_PROCESS_NOT_FOUND`), kein
Veröffentlichungsfehler.

Der Grund für die Literalregel: Nur so lässt sich einem veröffentlichten Workflow ansehen,
welche Prozesse er aufruft. Mit einem Ausdruck stünde das erst zur Laufzeit fest — und für den
Fernaufruf wäre es eine Zusage, die man einem Partner nicht geben könnte.

## Ablauf

1. Ein Token erreicht die Aufruf-Aktivität. Es **wartet** dort, genau wie an einem Service-Task.
   Die Engine legt einen ausstehenden Aufruf bereit: wartendes Token, Prozesskennung,
   Eingabevariablen.
2. Die Geschäftslogik sucht die **aktuell deployte** Version desjenigen Workflows, dessen
   `bpmn:process/@id` der Prozesskennung entspricht, und startet daraus eine **Kindinstanz** — in
   derselben Transaktion und unter derselben Sperre wie das Speichern des Aufrufers.
3. Die Kindinstanz merkt sich ihre Herkunft; das wartende Token merkt sich seine Kindinstanz.
4. Endet die Kindinstanz, läuft das wartende Token des Aufrufers weiter — mit dem Ergebnis oder
   mit einem BPMN-Fehler.

Scheitert ein Schritt, scheitert die ganze Mutation. Ein transaktionaler Adapter verwirft dann
auch den Fortschritt des Aufrufers.

## Variablenfluss

**Hinein** (beim Start der Kindinstanz):

| Modell | Was die Kindinstanz als Prozessvariablen bekommt |
|---|---|
| `propagateAllParentVariables="true"` | den gesamten Prozesskontext des Aufrufers |
| `zeebe:ioMapping`-Eingang | die zugeordneten Werte — sie liegen über dem propagierten Kontext |
| weder noch | nichts; die Kindinstanz startet ohne Variablen |

**Heraus** (beim Ende der Kindinstanz):

| Modell | Was der Aufrufer übernimmt |
|---|---|
| `zeebe:ioMapping`-Ausgang | genau die zugeordneten Werte — unabhängig von `propagateAllChildVariables` |
| nur `propagateAllChildVariables="true"` | den gesamten Variablenstand der Kindinstanz |
| weder noch | nichts |

Die Vorgabe im Parser ist `true` für beide Flags, wenn das Attribut fehlt — dieselbe Vorgabe wie
in der Konsole. Wer Daten bewusst nicht weitergeben will, setzt sie ausdrücklich auf `false` und
ordnet stattdessen zu.

## Fehler- und Abbruchsemantik

Alles, was hier als BPMN-Fehler auftritt, entsteht **an der Aufruf-Aktivität selbst** und kann
deshalb von einem Error-Boundary an ihr gefangen werden — sonst wandert es wie jeder andere
Fehler nach außen und lässt die aufrufende Instanz scheitern.

| Ereignis am Kind | Was am Aufrufer passiert |
|---|---|
| Regulär beendet (`Completed`) | Das Token läuft weiter, mit den Ausgabevariablen. |
| Terminate-End-Event | Wie regulär beendet. Ein Terminate beendet den aufgerufenen Prozess planmäßig. |
| Ungefangener BPMN-Fehler mit Code *X* | Die Aufruf-Aktivität wirft **denselben** Fehler *X*. Das ist die Standardsemantik von BPMN 2.0. |
| Ungefangener BPMN-Fehler ohne Code | `CALLED_PROCESS_FAILED` |
| Abbruch von außen (`POST /instance/{id}/cancel`) | `CALLED_PROCESS_CANCELLED` |
| Prozesskennung nirgends deployt | `CALLED_PROCESS_NOT_FOUND` (der Aufruf kommt gar nicht erst zustande) |
| Aufrufkette tiefer als 10 Ebenen | `CALLED_PROCESS_DEPTH_EXCEEDED` |

Ein Terminate-End-Event und ein Abbruch hinterlassen denselben Instanzzustand `Terminated`.
Unterschieden wird am laufenden Vorgang: Nur ein Abbruch von außen setzt die Abbruchmarke, und
nur er ist für den Aufrufer ein Fehlerfall.

**Abbruch des Aufrufers.** Wird der aufrufende Vorgang abgebrochen, werden alle noch laufenden
Kindinstanzen mit abgebrochen — rekursiv, best effort, in derselben Transaktion. Sie arbeiteten
sonst für einen Vorgang, den es nicht mehr gibt. Eine BPMN-Kompensation findet dabei so wenig
statt wie bei jedem anderen Abbruch.

## Verschachtelung und Rekursion

Ein aufgerufener Prozess darf selbst Aufruf-Aktivitäten enthalten. Die Tiefe wird über die
Elternkette bestimmt; die äußerste Instanz hat die Tiefe 1. Ab der zehnten Ebene wird der
nächste Aufruf mit `CALLED_PROCESS_DEPTH_EXCEEDED` abgebrochen.

Damit endet auch ein Prozess, der sich selbst aufruft — direkt oder über eine Kette. Der Fehler
wandert anschließend Ebene für Ebene nach oben: Jede Ebene wirft ihn an ihrer eigenen
Aufruf-Aktivität erneut, sofern ihn dort kein Boundary fängt. Ein Boundary auf einer beliebigen
Ebene bricht diese Kette also bewusst ab.

Zusätzlich begrenzt Flowzer die Zahl der Aufrufe und Rückmeldungen je Mutation auf 100 — dieselbe
Grenze und derselbe Grund wie bei der Nachrichtenzustellung.

## Herkunft, Rechte und Ansicht

Eine Kindinstanz trägt am Master-Token die Kennung ihres Aufrufers und des wartenden Tokens —
dort, wo auch der Initiator steht, damit die Herkunft jeden Speicher- und Ladevorgang überlebt
und nicht in den Prozessvariablen landet.

**Rechte.** Der Initiator des aufrufenden Vorgangs ist auch Initiator der Kindinstanz. Ohne das
bräche die Sicht auf den eigenen Vorgang genau an der Aufruf-Aktivität ab. Aufgaben innerhalb
der Kindinstanz folgen davon unberührt ihrem eigenen Zuweisungsmodell; siehe
[INSTANCE-ACCESS.md](INSTANCE-ACCESS.md).

**API.**

- `GET /instance/{id}` liefert `parentInstanceId` und `parentTokenId`.
- `GET /instance/{id}/children` liefert die direkten Kindinstanzen mit Workflowname, Version,
  Zustand und der Knoten-Id der Aufruf-Aktivität. Dieselbe Rechteprüfung wie die Instanzansicht;
  ein nicht sichtbarer Vorgang antwortet mit 404.

**Konsole.** Eine Kindinstanz zeigt „Aufgerufen von …" mit Link auf den Aufrufer, ein Aufrufer
den Abschnitt „Aufgerufene Vorgänge". Im Laufzeitdiagramm ist die Aufruf-Aktivität schlicht
`active`, solange die Kindinstanz läuft — ein eigener Zustand ist dafür nicht nötig.

## Instanzmigration

Eine Instanz, die an einer Aufruf-Aktivität auf eine laufende Kindinstanz wartet, ist in dieser
Stufe **nicht migrierbar**. Der Problemcode ist `CallActivityWaiting`; siehe
[INSTANCE-MIGRATION.md](INSTANCE-MIGRATION.md). Der Umzug zieht die Kindinstanz nicht mit, und
sie liefe danach gegen ein Token, das zu einem anderen Modell gehört.

Kindinstanzen selbst sind ganz normal migrierbar: Ihr Bezug zum Aufrufer hängt an Instanz- und
Tokenkennung, nicht an der Version.

## Ablage

Die Herkunft steht im JSON-Dokument der Instanz (`parentInstanceId`, `parentTokenId`); beide
Felder sind bewusst optional, damit Bestandsdokumente unverändert laden. Eine eigene Spalte oder
ein Index in PostgreSQL wurde **nicht** angelegt: Die einzige Abfrage, die danach sucht, ist die
Kindliste einer einzelnen Instanz, und die läuft über denselben vollständig geladenen Bestand
wie die Instanzliste. Sobald dieser Weg nicht mehr trägt, ist eine additive Migration der
richtige nächste Schritt — vorher wäre sie totes Schema.

## Grenzen dieser Stufe

- **Kein Fernaufruf.** Der aufgerufene Prozess liegt in derselben Installation. Stufe 2+ von #154.
- **Kein FEEL-Ausdruck** als Prozesskennung.
- **Keine Versionsbindung.** Aufgerufen wird immer die zur Aufrufzeit deployte Version; ein
  `versionTag` gibt es nicht. Laufende Kindinstanzen bleiben auf ihrer Version.
- **Keine Migration** von Aufrufern mit wartender Aufruf-Aktivität.
- **Kein Multi-Instance** an der Aufruf-Aktivität. Die Schleifenmerkmale werden zwar geparst, der
  Aufrufpfad ist dafür aber nicht belegt.
- **Keine Zwischenstände.** Der Aufrufer erfährt erst vom Ende der Kindinstanz, nicht von ihrem
  Fortschritt. Wer das braucht, modelliert heute eine Nachricht.
- **Keine Kompensation** beim Abbruch, wie überall sonst in Flowzer auch.
- Endet eine Kindinstanz durch eine Nachricht, die im selben Schreibvorgang von einer *anderen*
  Instanz geworfen wurde, läuft ihr Aufrufer in dieser Mutation weiter — aber nur, solange die
  Kette über den Austausch läuft. Der Normalfall (Aufgabe, Auftrag, Timer, eigener Fluss) ist
  davon nicht betroffen.
