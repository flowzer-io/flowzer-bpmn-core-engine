# Instanzmigration auf die deployte Version

**Stand:** 18. September 2026

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
| Welche Instanzen? | **Deckungsgleiche** ohne weiteres Zutun: Jeder Knoten, auf dem die Instanz wartet, existiert in der Zielversion mit derselben ID und demselben Typ. Fehlt ein Knoten dort, lässt sich sein Ziel **von Hand zuordnen** (siehe „Knoten von Hand zuordnen"). Wer nichts zuordnet, migriert die betroffenen Instanzen nicht; sie bleiben unverändert und werden mit Grund ausgewiesen. |
| Wohin? | Ausschließlich auf die **aktuell deployte** Version. Keine Rückmigration, keine frei wählbare Zielversion. |
| Offene Benutzeraufgaben? | Die Aufgabe **bleibt**: ID, Übernahme, Zuweisung und Fristen bleiben erhalten. Name und Formular kommen aus der Zielversion. Ein privater Entwurf bleibt nur erhalten, wenn die Formularbindung der Aufgabe in beiden Versionen identisch ist; sonst wird er verworfen — der Assistent zeigt das **vor** der Migration an. |
| Wer? | Nur mit Betriebsrecht (`operator`), wie der Instanzabbruch. |

Es gibt weiterhin **keine automatische Migration**: Ein Deployment verändert keine
laufende Instanz.

**Für dieselbe Version: [Instanzeingriffe](INSTANCE-MODIFICATION.md).** Wenn nicht die Version
das Problem ist, sondern der Vorgang — ein Schritt wurde versehentlich abgeschlossen, ein
Worker hängt an einem Knoten, eine Variable trägt einen falschen Wert —, setzt der
Instanzeingriff die Instanz innerhalb ihrer Version an eine andere Stelle. Er zieht den Token
dabei zurück und legt am Ziel einen neuen an; die Aufgabe am verlassenen Knoten verschwindet
also samt Kennung, statt wie hier mitzuziehen.

## Was „deckungsgleich" ausschließt

Die erste Ausbaustufe migriert nur flache Prozesse im Ruhezustand. Nicht migrierbar ist
eine Instanz, wenn

- sie nicht läuft oder ihr Prozess in der Zielversion eine andere Prozess-ID trägt,
- ein wartender Knoten in der Zielversion fehlt und ihm kein Ziel zugeordnet wurde,
- ein wartender Knoten in der Zielversion einen anderen Typ hat (auch nach einer Zuordnung),
- ein wartendes Token in einem Teilprozess oder einer Multi-Instance-Aktivität steht,
- ein wartender Service-Task in der Zielversion einen anderen Auftragstyp hat (ein bereits
  eingereihter Worker-Auftrag trüge sonst den falschen Typ),
- an einem wartenden Knoten bereits ein Boundary-Event ausgelöst hat (die Migration schaltete
  es erneut scharf, ein nicht unterbrechender Timer liefe ein zweites Mal),
- sie an einem KI-Task wartet (dessen Lauf ist an Verbindungsrevision und Modell der
  Quellversion gebunden),
- sie an einer Aufruf-Aktivität auf einen laufenden Kindvorgang wartet (Problemcode
  `CallActivityWaiting`): Der Umzug zieht die Kindinstanz nicht mit, und sie liefe danach gegen
  ein Token, das zu einem anderen Modell gehört. Die Kindinstanz selbst ist normal migrierbar —
  ihr Bezug zum Aufrufer hängt an Instanz- und Tokenkennung, nicht an der Version. Siehe
  [CALL-ACTIVITY.md](CALL-ACTIVITY.md).
- sie an einem der Ereignisse eines ereignisbasierten Gateways wartet (Problemcode
  `EventBasedGatewayWaiting`): Diese Tokens warten als Gruppe, von der genau eines gewinnt.
  Eines davon allein umzuziehen zerrisse sie; die übrigen warteten auf ein Ereignis, das
  niemanden mehr erreicht. Siehe [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md) (Vertrag 9).
- sie bereits auf der deployten Version läuft.

Bereits durchlaufene Knoten sind Historie und nie ein Hindernis — auch wenn es sie in
der Zielversion nicht mehr gibt.

## Knoten von Hand zuordnen

Ein Modell ändert sich nicht nur additiv: Eine Aufgabe wird umbenannt, ersetzt oder durch
zwei andere abgelöst. Für die Instanzen, die genau dort warten, muss jemand entscheiden, wo
sie weiterlaufen sollen. Der Assistent fragt das, statt die Instanz stillschweigend liegen
zu lassen.

- Der Trockenlauf nennt jeden wartenden Knoten, den es in der Zielversion nicht mehr gibt,
  zusammen mit den Knoten der Zielversion, die als Ziel in Frage kommen.
- Eine Zuordnung gilt für **alle** Instanzen der Anfrage: Sie laufen auf derselben
  Quellversion und teilen deshalb dieselben Knoten.
- Ziel und Quelle müssen denselben Elementtyp haben. Eine Aufgabe auf ein Gateway zu
  schieben ergäbe einen Zustand, den das Zielmodell nicht kennt.
- Zuordnen ist freiwillig. Ohne Zuordnung bleiben die betroffenen Instanzen unverändert;
  die übrigen migrieren trotzdem.
- Die Zuordnung verschiebt nur den Punkt, an dem die Instanz steht. Variablen, Aufgaben-ID,
  Übernahme und Fristen bleiben; das Formular kommt aus der Zielversion.
- Zeigt eine Zuordnung auf einen Knoten, den die Zielversion nicht kennt, meldet der
  Trockenlauf `MappingTargetMissing` und fragt erneut nach.

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
  ursprünglichen Beginn des Wartens; der Trockenlauf weist jeden betroffenen Knoten mit
  `TimerRecalculated` aus.
- **Ereignisspur:** Die Instanz merkt sich jede Migration (Quell- und Zielversion,
  Zeitpunkt, auslösende Person). Laufzeitdiagramm und Verlauf zeigen das Diagramm der
  Zielversion und die Ereignisse aller Versionen, auf denen die Instanz gelaufen ist. Ein
  durch eine Zuordnung verlassener Knoten gilt danach als durchlaufen: Wo eine Instanz
  steht, sagt ihr Tokenstand, nicht der jüngste Eintrag der Spur.

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

Je Instanz kommen zu den Befunden der Engine diese Codes hinzu:

| Code | Art | Bedeutung |
| --- | --- | --- |
| `AlreadyOnTargetVersion` | Problem | Die Instanz läuft bereits auf der deployten Version. |
| `TargetVersionChanged` | Problem | Während des Stapels wurde eine andere Version deployt; diese Instanz blieb unverändert. Jede Instanz prüft die Zielversion in ihrer eigenen Transaktion erneut, weil ein zweiter API-Prozess dazwischen deployen kann. |
| `DraftStorageNotSupported` | Problem | Die Ablage kennt den Entwurfsvertrag nicht und könnte die Entwürfe nicht mitnehmen; die Instanz bleibt unverändert, statt halb umgezogen liegen zu bleiben. |
| `MigrationFailed` | Problem | Der Umzug dieser Instanz ist unerwartet gescheitert; sie blieb unverändert. |
| `UserTaskFormChanged` | Hinweis | Die Aufgabe trägt in der Zielversion ein anderes Formular. |
| `UserTaskDraftDiscarded` | Hinweis | Wegen des anderen Formulars wird mindestens ein privater Entwurf verworfen. |
| `ServiceTaskJobInProgress` | Hinweis | Ein Worker arbeitet gerade an einem Auftrag dieser Instanz. |
| `TimerRecalculated` | Hinweis | Am genannten Knoten hängt in der Zielversion ein Timer (Catch-Event oder Boundary-Timer). |

## Grenzen

- Keine Teilprozesse, keine Multi-Instance, keine KI-Tasks und keine Aufrufer mit wartender
  Aufruf-Aktivität (siehe oben). Die Zuordnung führt nur auf Knoten der obersten Ebene und nur
  auf denselben Elementtyp.
- Variablen werden nicht umgeschrieben. Erwartet die Zielversion andere Variablen, ist
  das vor der Migration fachlich zu prüfen; die API kann es nicht erkennen.
- Timer werden nicht umgerechnet: Nach dem Umzug gilt die Dauer der Zielversion ab dem
  ursprünglichen Beginn des Wartens, und ein in der Zielversion neu angehefteter
  Boundary-Timer steht sofort scharf — eine seit Tagen wartende Aufgabe kann dadurch beim
  nächsten Timerlauf unmittelbar fällig werden. Der Trockenlauf kündigt das je Knoten an.
- Eine Migration lässt sich nicht zurücknehmen.
- Die Dateiablage besitzt keine Transaktion; ein Abbruch mitten in der Migration kann dort
  einen Zwischenstand hinterlassen. PostgreSQL ist der Betriebspfad.
