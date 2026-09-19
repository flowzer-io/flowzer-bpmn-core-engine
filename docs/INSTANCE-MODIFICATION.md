# Instanzeingriffe: Schritte verschieben und Variablen korrigieren

**Stand:** 19. September 2026

Die [Instanzmigration](INSTANCE-MIGRATION.md) hebt eine laufende Instanz auf eine **andere
Version**. Der Instanzeingriff ist das andere Werkzeug: Er setzt eine laufende Instanz
**innerhalb derselben Version** an eine andere Stelle und korrigiert ihre Variablen. In
Camunda heißt das „Process Instance Modification".

Gebraucht wird er, wenn das Modell stimmt und der Vorgang nicht: Ein Schritt wurde
versehentlich abgeschlossen und muss noch einmal laufen. Ein Worker hängt an einem Knoten, den
niemand mehr brauchen kann. Eine Variable trägt einen falschen Wert, mit dem das nächste
Gateway falsch entscheiden würde. Bisher blieb dafür nur: abbrechen und neu starten.

## Entscheidung

| Frage | Entscheidung |
| --- | --- |
| Was lässt sich verschieben? | Nur ein **wartender** Schritt der **obersten Prozessebene**. Teilprozesse und Multi-Instance bleiben dieser Stufe verschlossen (`TokenNotMovable`). |
| Wohin? | Auf jeden Flow-Knoten der obersten Ebene **außer** Start-Events und Boundary-Events. Beide werden nie von einem Sequenzfluss erreicht; als Ziel ergäben sie einen Zustand, den das Modell nicht kennt (`TargetNotAllowed`). |
| Neue Schritte ohne Quelle? | Nein. Es wird nur **verschoben**, nie aus dem Nichts erzeugt. Ein Token hat immer einen Ursprung. |
| Wie viele Instanzen? | Genau eine je Anfrage. Ein Eingriff ist eine Einzelfallentscheidung, kein Stapel. |
| Wer? | Nur mit Betriebsrecht (`operator`), wie Instanzabbruch und Migration. |

## Der Unterschied zur Migration: neue Aufgaben-IDs

Das ist der Punkt, an dem sich beide Werkzeuge unterscheiden, und er ist keine Nebenwirkung,
sondern die Semantik.

Die **Migration** tauscht am Token das mitgeführte Modell aus. Der Token bleibt derselbe, also
bleibt auch die Aufgabe dieselbe: Kennung, Übernahme, Zuweisung und Fristen überstehen den
Umzug, ein privater Entwurf unter Umständen ebenfalls.

Der **Eingriff** zieht den Token zurück — genau wie die Engine es tut, wenn ein
Error-Boundary-Event einen BPMN-Fehler fängt — und legt am Ziel einen **neuen** Token an, so
als hätte ihn der Sequenzfluss gerade erreicht. Daraus folgt zwingend:

- Die Aufgabe am verlassenen Knoten **verschwindet**, samt Kennung, Übernahme, Zuweisung,
  Fristen und jedem privaten Entwurf.
- Am Zielknoten entsteht gegebenenfalls eine **neue** Aufgabe mit **neuer** Kennung. Ein Link
  auf die alte Aufgabe läuft danach ins Leere.
- Der Auftrag eines verlassenen Service-Tasks **verfällt**. Ein Worker, der gerade daran
  arbeitet, meldet sein Ergebnis ins Leere.
- Ein Timer am Zielknoten rechnet **ab dem Eingriff** neu, nicht ab dem ursprünglichen Beginn
  des Wartens.

Der Trockenlauf kündigt jede dieser Folgen einzeln an, bevor irgendetwas passiert.

Dass es so und nicht anders läuft, ist kein Sonderweg: Das Speichern einer Instanz richtet
Aufgaben und Aufträge ohnehin am Tokenstand aus. Ein Token, der nicht mehr wartet, verliert
seine Subscription und seinen Auftrag; ein neu wartender Token bekommt beide frisch. Der
Eingriff bringt dafür **keine eigene Umbindelogik** mit — er verändert den Tokenstand, alles
Weitere ist der gewöhnliche Speicherpfad.

## Was beim Eingriff geschieht

Die Reihenfolge ist Absicht:

1. **Variablen zuerst.** `set` schreibt auf die Ebene, auf der die Variable heute liegt; kennt
   sie dort niemand, entsteht sie auf der Prozessebene (Master-Token). Die Prozessebene hat
   dabei Vorrang — was dort steht, lesen alle Schritte. `remove` entfernt die Variable überall
   auf der Prozessebene, auch an einem wartenden Schritt, an den eine Eingabeabbildung sie
   kopiert hat. Nennt eine Anfrage denselben Namen in beiden Abschnitten, gewinnt `remove`.
2. **Dann die Verschiebungen.** Jeder Quell-Token wird auf `Withdrawn` gesetzt, am Zielknoten
   entsteht ein Token im Zustand `Ready` — mit aufgelösten Ausdrücken und scharf geschalteten
   Boundary-Events des Ziels.
3. **Dann ein einziger Lauf.** Die Engine läuft weiter: Ein User-Task wartet am Ziel neu, ein
   Service-Task erzeugt einen neuen Auftrag, ein Gateway wird ausgewertet, ein End-Event
   beendet die Instanz.

Weil die Variablen vor dem Lauf stehen, entscheidet ein Gateway, auf das gerade verschoben
wurde, nach dem **korrigierten** Wert. Weil alle Verschiebungen vor dem Lauf stehen, läuft die
Instanz nie mit einem halben Stand weiter.

Ein Ziel, das dem aktuellen Knoten entspricht, ist ausdrücklich erlaubt: Das ist der Neustart
eines Schritts. Die alte Aufgabe verschwindet auch dann, sonst stünde die Instanz doppelt an
derselben Stelle.

### Ereignisspur und Laufzeitdiagramm

Die Engine schreibt für den zurückgezogenen und den neuen Token die gewöhnlichen
`RuntimeNodeEvent`s. Im [Laufzeitdiagramm](RUNTIME-DIAGRAM.md) erscheint der verlassene Knoten
danach als „cancelled", der Zielknoten als „active". Maßgeblich ist wie immer der Tokenstand,
nicht der jüngste Eintrag der Spur.

Die Instanz merkt sich jeden Eingriff wie jede Migration: Zeitpunkt, auslösende Person, die
Verschiebungen als `{tokenId, from, to}` und die **Namen** der gesetzten und entfernten
Variablen — **keine Werte**. Die Spur ist für jeden lesbar, der die Instanz inspizieren darf,
und darf keine Fachdaten verewigen.

## Öffentlicher Vertrag

Beide Wege verlangen das Betriebsrecht.

| Methode | Pfad | Bedeutung |
| --- | --- | --- |
| `POST` | `/instance/{id}/modification/preview` | Trockenlauf: `applicable`, `problems`, `notices` sowie `steps` und `targets`. Verändert nichts. |
| `POST` | `/instance/{id}/modification` | Führt den Eingriff aus, unter Instanz- und Engine-Sperre in einer Transaktion. |

Beide nehmen denselben Rumpf:

```json
{
  "moves": [{ "tokenId": "…", "targetFlowNodeId": "Approve" }],
  "variables": { "set": { "Betrag": 99 }, "remove": ["Vermerk"] }
}
```

Eine **leere** Anfrage ist nur für den Trockenlauf gültig. Sie beantwortet die Frage, die die
Oberfläche zuerst stellt: welche Schritte warten (`steps`) und welche Knoten als Ziel in Frage
kommen (`targets`). Als Eingriff ist sie `NothingToDo` — ein Klick ohne Auswahl soll keine
leere Spur schreiben.

Statuscodes: `404` für eine unbekannte Instanz, `409` für eine nicht mehr laufende, `422` für
alles Übrige. Der 422-Rumpf des Eingriffs trägt die Befunde mit denselben stabilen Codes wie
der Trockenlauf:

```json
{ "status": 422, "title": "…", "detail": "…",
  "problems": [{ "code": "TargetNotAllowed", "tokenId": null, "flowNodeId": "Start", "message": "…" }] }
```

Der englische `message`-Text ist technische Detailauskunft; die Konsole übersetzt den Code.

### Hindernisse

| Code | Bedeutung |
| --- | --- |
| `InstanceNotRunning` | Die Instanz läuft nicht mehr. |
| `TokenMissing` | Die genannte Token-Kennung gehört nicht zu dieser Instanz. |
| `TokenNotMovable` | Der Schritt wartet nicht oder steht nicht auf der obersten Ebene (Teilprozess, Multi-Instance, bereits abgeschlossen). |
| `DuplicateMove` | Derselbe Schritt wurde mit zwei Zielen genannt. Ein Schritt kann nur an eine Stelle. |
| `TargetMissing` | Den Zielknoten gibt es auf der obersten Ebene dieses Modells nicht. |
| `TargetNotAllowed` | Das Ziel ist ein Start- oder Boundary-Event. |
| `VariableNameInvalid` | Der Variablenname ist leer oder benennt einen Pfad (`a.b`, `a[0]`). |
| `NothingToDo` | Die Anfrage trägt weder eine Verschiebung noch eine Variablenänderung. |
| `RequestTooLarge` | Mehr als 100 Verschiebungen oder 200 Variablen in einer Anfrage. |
| `ModificationFailed` | Der Eingriff ist unerwartet gescheitert; die Instanz blieb unverändert. |

### Hinweise

| Code | Bedeutung |
| --- | --- |
| `UserTaskCancelled` | Die Aufgabe am verlassenen Knoten verschwindet samt Kennung, Übernahme und Fristen. |
| `UserTaskDraftDiscarded` | Zu dieser Aufgabe gibt es mindestens einen privaten Entwurf; er geht mit ihr verloren. |
| `ServiceTaskJobCancelled` | Der Worker-Auftrag des verlassenen Service-Tasks verfällt. |
| `ServiceTaskJobInProgress` | An diesem Auftrag arbeitet gerade ein Worker; seine Rückmeldung läuft danach ins Leere. |
| `TimerRecalculated` | Am Zielknoten hängt ein Timer; er beginnt mit dem Eingriff von vorn. |
| `VariableNotFound` | Eine zu entfernende Variable gibt es nicht. Kein Fehler — es passiert nur nichts. |

## In der Konsole

In der Instanzansicht führt bei laufender Instanz „Instanz anpassen …" in einen Dialog. Er
listet die wartenden Schritte; je Schritt lässt sich „belassen" oder ein Zielknoten wählen
(gruppiert nach Elementtyp). Der Abschnitt „Variablen" ist mit den aktuellen Prozessvariablen
vorbelegt und schickt nur geänderte oder neue Felder mit; daneben steht eine Checkliste für zu
entfernende Variablen. Beim Öffnen des Bestätigungsschritts läuft der Trockenlauf; seine
Hindernisse und Hinweise erscheinen in deutscher Sprache. Ausgeführt wird erst nach
ausdrücklicher Bestätigung.

## Grenzen

- **Keine Teilprozesse und keine Multi-Instance.** Ein Token dort zu verschieben ließe einen
  Scope zurück, den niemand mehr abschließt.
- **Keine neuen Schritte ohne Quelle.** Es wird nur verschoben. Ein zweiter paralleler Zweig
  lässt sich damit nicht eröffnen.
- **Kein Stapel.** Eine Anfrage, eine Instanz.
- **Nur ganze Variablen der obersten Ebene.** Pfade (`Adresse.Ort`) und Indexe werden
  abgelehnt, statt halb verstanden zu werden.
- **Ein Eingriff lässt sich nicht zurücknehmen.** Die alte Aufgaben-Kennung ist danach fort und
  kommt nicht wieder.
- **Die Dateiablage besitzt keine Transaktion**; ein Abbruch mitten im Eingriff kann dort einen
  Zwischenstand hinterlassen. PostgreSQL ist der Betriebspfad.
