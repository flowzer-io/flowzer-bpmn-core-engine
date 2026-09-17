# Instanzmigration auf die deployte Version

**Stand:** 17. September 2026

Laufende Instanzen bleiben grundsätzlich an die Workflow-Version gebunden, mit der sie
gestartet wurden. Die Instanzmigration ist die **bewusste, einzeln ausgelöste Ausnahme**
davon: Eine Person mit Betriebsrecht hebt eine oder mehrere laufende Instanzen derselben
älteren Version auf die aktuell deployte Version desselben Workflows.

## Entscheidung

Bisher galt ohne Ausnahme: „Laufende Instanzen behalten Definition und gebundene
Formulare." Das schützt vor stillen Umdeutungen, lässt aber keinen Weg, einen Fehler im
Modell für bereits laufende Vorgänge zu beheben — außer abbrechen und neu starten.

Entschieden am 17. September 2026 (Christian Maaß):

| Frage | Entscheidung |
| --- | --- |
| Welche Instanzen? | Nur **deckungsgleiche**: Jeder Knoten, auf dem die Instanz gerade wartet, existiert in der Zielversion mit derselben ID und demselben Typ. Alle anderen bleiben unverändert und werden mit Grund als „nicht migrierbar" ausgewiesen. Kein manuelles Knoten-Mapping. |
| Wohin? | Ausschließlich auf die **aktuell deployte** Version. Keine Rückmigration, keine frei wählbare Zielversion. |
| Offene Benutzeraufgaben? | Die Aufgabe **bleibt**: ID, Übernahme, Zuweisung und Fristen bleiben erhalten. Name und Formular kommen aus der Zielversion. Ein privater Entwurf bleibt nur erhalten, wenn die Formularbindung der Aufgabe in beiden Versionen identisch ist; sonst wird er verworfen — der Assistent zeigt das **vor** der Migration an. |
| Wer? | Nur mit Betriebsrecht (`operator`), wie der Instanzabbruch. |

Es gibt weiterhin **keine automatische Migration**: Ein Deployment verändert keine
laufende Instanz.

## Was „deckungsgleich" ausschließt

Die erste Ausbaustufe migriert nur flache Prozesse im Ruhezustand. Nicht migrierbar ist
eine Instanz, wenn

- sie nicht läuft oder ihr Prozess in der Zielversion eine andere Prozess-ID trägt,
- ein wartender Knoten in der Zielversion fehlt oder einen anderen Typ hat,
- ein wartendes Token in einem Teilprozess oder einer Multi-Instance-Aktivität steht,
- ein wartender Service-Task in der Zielversion einen anderen Auftragstyp hat (ein bereits
  eingereihter Worker-Auftrag trüge sonst den falschen Typ),
- an einem wartenden Knoten bereits ein Boundary-Event ausgelöst hat (die Migration schaltete
  es erneut scharf, ein nicht unterbrechender Timer liefe ein zweites Mal),
- sie an einem KI-Task wartet (dessen Lauf ist an Verbindungsrevision und Modell der
  Quellversion gebunden),
- sie bereits auf der deployten Version läuft.

Bereits durchlaufene Knoten sind Historie und nie ein Hindernis — auch wenn es sie in
der Zielversion nicht mehr gibt.

## Was bei der Migration geschieht

Das Prozessmodell einer laufenden Instanz liegt in ihren Tokens: Das Wurzel-Token trägt
den Prozess, jedes weitere den Knoten, auf dem es steht. Die Engine ersetzt deshalb

1. den Prozess am Wurzel-Token durch den der Zielversion und
2. an jedem wartenden Token den Knoten durch sein Gegenstück aus der Zielversion —
   mit aufgelösten Ausdrücken und neu bestimmten Boundary-Events, genau so, als hätte das
   Token den Knoten in der Zielversion erreicht.

Token-IDs, Variablen, Zeitstempel und alle beendeten Tokens bleiben unverändert. Ab dem
nächsten Schritt folgt die Instanz den Sequenzflüssen der Zielversion.

Im selben Vorgang und derselben Transaktion bindet die API um:

- **Benutzeraufgaben:** Subscription behält ihre ID und alle Bearbeitungsdaten, trägt
  danach die Zielversion; das Formular wird wie immer über Version und Form-Key aufgelöst
  und stammt damit aus der Zielversion.
- **Entwürfe:** bei identischer Formularbindung auf die Zielversion umgebunden, sonst
  verworfen.
- **Worker-Aufträge:** behalten ID, Sperre und Versuche; ein Worker, der gerade arbeitet,
  meldet sein Ergebnis unverändert zurück.
- **Nachrichten, Signale, Timer:** werden wie bei jedem Speichern aus dem Tokenstand neu
  geschrieben. Ein Timer rechnet danach mit der Dauer aus der Zielversion ab dem
  ursprünglichen Beginn des Wartens.
- **Ereignisspur:** Die Instanz merkt sich jede Migration (Quell- und Zielversion,
  Zeitpunkt, auslösende Person). Laufzeitdiagramm und Verlauf zeigen das Diagramm der
  Zielversion und die Ereignisse aller Versionen, auf denen die Instanz gelaufen ist.

Mehrere Instanzen werden **einzeln** migriert: Scheitert eine, bleiben die übrigen
Ergebnisse bestehen, und die Antwort nennt das Ergebnis je Instanz.

## Öffentlicher Vertrag

Beide Wege verlangen das Betriebsrecht.

| Methode | Pfad | Bedeutung |
| --- | --- | --- |
| `POST` | `/instance/migration/preview` | Trockenlauf für `instanceIds`: Quell- und Zielversion, je Instanz `migratable`, `problems` und `notices`. Verändert nichts. |
| `POST` | `/instance/migration` | Migriert die `instanceIds` auf `targetDefinitionId`. Ist inzwischen eine andere Version deployt, antwortet die API mit `409`, ohne etwas zu verändern. |

Alle Instanzen einer Anfrage müssen zum selben Workflow und zur selben Quellversion
gehören; sonst `422`. `problems` und `notices` tragen stabile Codes; die Konsole
übersetzt sie, der englische `message`-Text ist technische Detailauskunft.

## Grenzen

- Kein Knoten-Mapping, keine Teilprozesse, keine Multi-Instance, keine KI-Tasks (siehe oben).
- Variablen werden nicht umgeschrieben. Erwartet die Zielversion andere Variablen, ist
  das vor der Migration fachlich zu prüfen; die API kann es nicht erkennen.
- Eine Migration lässt sich nicht zurücknehmen.
- Die Dateiablage besitzt keine Transaktion; ein Abbruch mitten in der Migration kann dort
  einen Zwischenstand hinterlassen. PostgreSQL ist der Betriebspfad.
