# Beispielprozess: Urlaubsantrag

Ein Demonstrationsprozess für Formulare, parallele Zweige,
menschliche Entscheidungen, automatische Prüfungen und Anbindungen an andere Systeme.

```
Urlaubsantrag stellen   ◀ Startformular
      │
      ├─ Urlaubstage prüfen    (Lohnbuchhaltung, Formular) ─▶ genug Tage?    ──nein──┐
      ├─ Fachlich entscheiden  (Vorgesetzte, Formular)     ─▶ freigegeben?   ──nein──┤
      └─ Vertretung prüfen     (Service-Task)              ─▶ Vertretung da? ──nein──┤
      │ alle drei ja                                                                 │
      ├─ Antragsteller benachrichtigen   (Service-Task)                              ▼
      ├─ Urlaub in LexOffice eintragen   (Lohnbuchhaltung, Formular)      Ablehnung mitteilen
      └─ Urlaub in TickyTask eintragen   (Service-Task)                              │
      │                                                                              ▼
   Urlaub genehmigt                                                       Antrag abgelehnt ⊗
```

Jede Prüfung entscheidet in ihrem eigenen Zweig, gleich nachdem sie fertig ist. Sagt eine
„nein", geht der Antrag sofort zur Ablehnung, und das abbrechende Ende (⊗) beendet den
ganzen Vorgang: Die beiden anderen Prüfungen werden beendet, ihre offenen Aufgaben
verschwinden aus den Aufgabenlisten. Niemand arbeitet noch an einem Antrag, der schon
abgelehnt ist.

## Einspielen

```bash
node examples/urlaubsantrag/import.mjs http://localhost:5182
```

Gegen eine abgesicherte Instanz zusätzlich ein Zugangstoken mit der Modelliererrolle:

```bash
FLOWZER_TOKEN=<access-token> node examples/urlaubsantrag/import.mjs https://flowzer.example
```

Das Skript legt fünf Formulare (vier wiederverwendbare und den Urlaubsantrag), den Katalogeintrag und die erste deployte Version an. Es
ist wiederholbar: Vorhandenes bleibt stehen, es kommt nur eine neue Version dazu.

## Ausprobieren

Der Antrag wird **beim Start** ausgefüllt: In der Konsole öffnet „Starten" das Formular
`Urlaubsantrag`, und die Eingaben werden die Startvariablen des Vorgangs. Einen eigenen
ersten Schritt gibt es dafür nicht mehr.

Von außen geht dasselbe über die API. `GET /definition/meta/flowzer-urlaubsantrag/start-form`
liefert das Formular, gestartet wird mit den ausgefüllten Werten:

```bash
curl -X POST http://localhost:5182/definition/meta/flowzer-urlaubsantrag/instance \
  -H 'Content-Type: application/json' \
  -d '{
        "variables": {
          "mitarbeiter": "Christian Maaß",
          "art": "erholung",
          "von": "2026-10-05",
          "bis": "2026-10-16",
          "arbeitstage": 10,
          "vertretung": "Melli",
          "bemerkung": ""
        }
      }'
```

Weil der Workflow ein Startformular trägt, verlangt die API das `variables`-Objekt; ohne
Rumpf antwortet sie mit 400. Pflichtfelder, Typen, Auswahlwerte und `bis >= von`
werden serverseitig geprüft; ungültige Eingaben liefern feldbezogene Fehler mit 422.
`vorgang` berechnet ausschließlich der Server mittels `join.v1`. Externe Clients
lassen das Feld weg; sie dürfen keine abweichende Zusammenfassung vorgeben.
Siehe [Prüfprofil](../../docs/FORM-VALIDATION-PROFILE.md).

Die drei Service-Tasks brauchen Worker. Zum Durchspielen genügt der mitgelieferte:

```bash
node examples/urlaubsantrag/demo-worker.mjs http://localhost:5182
```

Er ersetzt keine Anbindung — die Vertretungsprüfung sagt immer ja, benachrichtigt wird auf
der Konsole, und TickyTask bekommt eine erfundene Vorgangsnummer. Als Vorlage für die
echten Worker taugt er trotzdem; der Vertrag steht in
[docs/SERVICE-TASK-WORKER.md](../../docs/SERVICE-TASK-WORKER.md).

## Formulare

Nur der Antrag hat ein eigenes Formular; es hängt als **Startformular** am Startereignis. Die
drei Aufgaben im Ablauf benutzen die
[wiederverwendbaren Formulare](../formulare-generisch/README.md), die auch jeder andere
Prozess benutzt.

| Aufgabe | Formular (= Form-Key) | Wer | Landet im Prozess als |
| --- | --- | --- | --- |
| Start des Workflows (Startformular) | `Urlaubsantrag` (eigenes) | antragstellende Person | `mitarbeiter`, `art`, `von`, `bis`, `arbeitstage`, `vertretung`, `bemerkung`, `vorgang` |
| Urlaubstage prüfen | `Prüfung` | Lohnbuchhaltung | `tageAusreichend`, `resttage`, `lohnbuchhaltungKommentar` |
| Urlaub fachlich entscheiden | `Freigabe` | Vorgesetzte | `fachlicheEntscheidung`, `fachlicheBegruendung` |
| Urlaub in LexOffice eintragen | `Erledigung bestätigen` | Lohnbuchhaltung | `lexofficeEingetragen`, `lexofficeReferenz`, `lexofficeAnmerkung` |

Die allgemeinen Formulare antworten mit allgemeinen Namen (`entscheidung`, `pruefwert`, …).
Was das *hier* bedeutet, steht als `zeebe:ioMapping` am jeweiligen Task — deshalb die
rechte Spalte. Ohne diese Zuordnung schrieben beide Entscheidungen in dieselbe Variable.

Der Form-Key im BPMN ist der **Name** des Formulars. Namen müssen deshalb eindeutig sein;
mit `Name:1.0` lässt sich eine feste Version binden.

Der Server bildet das versteckte Feld `vorgang` aus den deklarierten Quellen,
beispielsweise „Christian Maaß · erholung · 2026-10-05 · 2026-10-16 · 10 · Melli“.
Datum und Auswahlcode bleiben absichtlich unverändert. Die allgemeinen Formulare
zeigen diesen Kontext oben an; die alte clientseitige JavaScript-Berechnung entfällt.
`vertretung` bleibt in diesem Beispiel vorerst Text. Eine Verzeichniswahl und echte
externe Abgleiche sind separate Erweiterungen, keine bereits verfügbare Personalverwaltung.

## Service-Tasks

| Typ | Aufgabe | Erwartete Rückmeldung |
| --- | --- | --- |
| `urlaub-vertretung-pruefen` | Hat die genannte Vertretung im Zeitraum selbst genehmigten Urlaub? | `vertretungFrei`: `"ja"` oder `"nein"` |
| `urlaub-genehmigung-mitteilen` | Nachricht an die antragstellende Person | frei |
| `urlaub-ablehnung-mitteilen` | Nachricht mit dem Ablehnungsgrund | frei |
| `urlaub-tickytask-eintragen` | Abwesenheit in TickyTask anlegen | frei, z. B. `tickytaskVorgang` |

Jeder Service-Task sagt am Modell, was sein Worker zu sehen bekommt — die
Vertretungsprüfung etwa nur `vertretung`, `von` und `bis`. Ohne diese Angabe bekäme ein
fremder Dienst alle Prozessvariablen, also auch die Bemerkung aus dem Antrag und die
interne Benutzerkennung. Der Vertrag steht in
[docs/SERVICE-TASK-WORKER.md](../../docs/SERVICE-TASK-WORKER.md).

Bei „Ablehnung mitteilen" stehen dort auch die drei Entscheidungen — als Schnappschuss
dessen, was bis zu diesem Moment gefallen ist. Sicher dabei ist die Prüfung, die „nein"
gesagt hat; andere können schon fertig sein (mit „ja" oder, im seltenen Fall, einem
zweiten „nein"), die übrigen laufen noch. Ein Eingang, dessen Variable es noch nicht
gibt, ist für die Engine kein Fehler, er kommt leer an. Wie „leer" beim Worker ankommt,
hängt am Ausdrucks-Handler — mit FEEL als `null`, mit dem einfachen Handler als der Name
der Variablen selbst. Der Demo-Worker behandelt beides als „nicht gesetzt" und sucht
sich die Prüfung heraus, die „nein" gesagt hat.

## Zwei Entwurfsentscheidungen

**Drei einzelne Tore statt einer zusammengesetzten Bedingung.** Die Engine wertet
Bedingungen je nach Umgebung mit FEEL oder mit dem einfachen Handler aus; letzterer kennt
nur einen Vergleich je Ausdruck, ein `und` gäbe es dort nicht. Drei Tore laufen in beiden
Fällen. Nebeneffekt: Der Ablehnungsgrund ist am Diagramm ablesbar.

**Jede Prüfung entscheidet in ihrem eigenen Zweig, und ein „nein" beendet den Vorgang.**
Jedes Tor steht direkt hinter seiner Aufgabe, nicht hinter dem parallelen Tor. Der
„nein"-Weg führt zu „Ablehnung mitteilen" und von dort auf ein **abbrechendes Ende**
(`bpmn:terminateEventDefinition`). Das beendet nicht nur seinen Zweig, sondern den ganzen
Vorgang: Die beiden anderen Prüfungen hören auf, ihre offenen Aufgaben verschwinden aus
den Aufgabenlisten. Ohne den Abbruch bliebe eine Aufgabe in der Liste der
Lohnbuchhaltung stehen, die niemand mehr braucht.

## Bekannte Grenze: „Ablehnung mitteilen" kann zweimal laufen

Das abbrechende Ende liegt **hinter** „Ablehnung mitteilen", nicht davor — die Nachricht
soll ja noch hinausgehen. Zwischen dem „nein" und dem Ende liegt also ein Service-Task,
und solange dessen Worker nicht geantwortet hat, läuft der Vorgang weiter: Die beiden
anderen Prüfungen bleiben in diesem Fenster offen.

Sagen zwei Prüfungen in diesem Fenster „nein", laufen beide „nein"-Wege los. Dann läuft
„Ablehnung mitteilen" zweimal, und die antragstellende Person bekommt zwei Nachrichten.
Der Abbruch beendet den Vorgang, sobald das Ende tatsächlich erreicht ist — er verhindert
nicht, dass in der Zwischenzeit ein zweiter Weg angestoßen wurde.

Der Fall ist nicht theoretisch: Zwei Menschen können ihre Aufgabe im selben Moment
abschließen, und die Vertretungsprüfung antwortet ohnehin von selbst. Wen das stört, macht
den Worker `urlaub-ablehnung-mitteilen` unempfindlich gegen Wiederholung — er merkt sich
je Vorgang, dass er schon benachrichtigt hat. Das ist die übliche Antwort für Worker: Ein
Auftrag kann sich auch aus anderen Gründen wiederholen, etwa wenn die Sperre abläuft
(siehe [docs/SERVICE-TASK-WORKER.md](../../docs/SERVICE-TASK-WORKER.md)).

## Die Gliederungsansicht zeigt dieses Beispiel nicht

Seit dem abbrechenden Ende liegt der Urlaubsantrag außerhalb der Teilmenge, die die
[Gliederungsansicht](../../docs/GLIEDERUNG-TEILMENGE.md) abbildet: Ein
`terminateEventDefinition` steht nicht auf ihrer Positivliste, und die „nein"-Kanten
verlassen den parallelen Block, statt sich am Join wieder zu treffen. Die Gliederung lehnt
das Modell mit einer Meldung ab; bearbeitet wird es im Diagramm. Das ist gewollt — die
Gliederung wird dafür nicht erweitert.

## Was für den echten Einsatz noch fehlt

- **Wer ist die antragstellende Person?** Sie trägt ihren Namen im Startformular selbst ein.
  Verlässlicher wäre, die startende Person beim Instanzstart in eine Variable zu schreiben —
  dann stünde sie fest, statt getippt zu werden.
- **Die Gruppennamen** `Lohnbuchhaltung` und `Vorgesetzte` müssen im Identity Provider als
  Gruppen existieren, sonst sieht niemand die Aufgaben.
- **Die drei Worker** sind zu schreiben. Der Demo-Worker zeigt den Ablauf, nicht die Fachlichkeit.
- **Eine Frist auf den Antrag** (Boundary-Timer) gibt es nicht; die Tasks tragen nur eine
  Fälligkeit zur Anzeige.
