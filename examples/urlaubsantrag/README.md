# Beispielprozess: Urlaubsantrag

Ein vollständiger Prozess mit allem, was Flowzer kann: Formulare, parallele Zweige,
menschliche Entscheidungen, automatische Prüfungen und Anbindungen an andere Systeme.

```
Antrag stellen
      │
      ├─ Urlaubstage prüfen        (Lohnbuchhaltung, Formular)
      ├─ Fachlich entscheiden      (Vorgesetzte, Formular)
      └─ Vertretung prüfen         (Service-Task)
      │
   alle drei ok?  ──nein──▶  Ablehnung mitteilen ──▶ Antrag abgelehnt
      │ ja
      ├─ Antragsteller benachrichtigen   (Service-Task)
      ├─ Urlaub in LexOffice eintragen   (Lohnbuchhaltung, Formular)
      └─ Urlaub in TickyTask eintragen   (Service-Task)
      │
   Urlaub genehmigt
```

## Einspielen

```bash
node examples/urlaubsantrag/import.mjs http://localhost:5182
```

Gegen eine abgesicherte Instanz zusätzlich ein Zugangstoken mit der Modelliererrolle:

```bash
FLOWZER_TOKEN=<access-token> node examples/urlaubsantrag/import.mjs https://flowzer.example
```

Das Skript legt vier Formulare, den Katalogeintrag und die erste deployte Version an. Es
ist wiederholbar: Vorhandenes bleibt stehen, es kommt nur eine neue Version dazu.

## Ausprobieren

Die drei Service-Tasks brauchen Worker. Zum Durchspielen genügt der mitgelieferte:

```bash
node examples/urlaubsantrag/demo-worker.mjs http://localhost:5182
```

Er ersetzt keine Anbindung — die Vertretungsprüfung sagt immer ja, benachrichtigt wird auf
der Konsole, und TickyTask bekommt eine erfundene Vorgangsnummer. Als Vorlage für die
echten Worker taugt er trotzdem; der Vertrag steht in
[docs/SERVICE-TASK-WORKER.md](../../docs/SERVICE-TASK-WORKER.md).

## Formulare

| Formular (= Form-Key) | Wer füllt es aus | Was dabei herauskommt |
| --- | --- | --- |
| `Urlaubsantrag` | die antragstellende Person | `mitarbeiter`, `art`, `von`, `bis`, `arbeitstage`, `vertretung`, `bemerkung` |
| `Urlaub – Restanspruch` | Lohnbuchhaltung | `resttage`, `tageAusreichend`, `lohnbuchhaltungKommentar` |
| `Urlaub – Fachliche Entscheidung` | Vorgesetzte | `fachlichGenehmigt`, `entscheidungKommentar` |
| `Urlaub – Eintrag LexOffice` | Lohnbuchhaltung | `lexofficeEingetragen`, `lexofficeReferenz` |

Der Form-Key im BPMN ist der **Name** des Formulars. Namen müssen deshalb eindeutig sein;
mit `Name:1.0` lässt sich eine feste Version binden.

## Service-Tasks

| Typ | Aufgabe | Erwartete Rückmeldung |
| --- | --- | --- |
| `urlaub-vertretung-pruefen` | Hat die genannte Vertretung im Zeitraum selbst genehmigten Urlaub? | `vertretungFrei`: `"ja"` oder `"nein"` |
| `urlaub-genehmigung-mitteilen` | Nachricht an die antragstellende Person | frei |
| `urlaub-ablehnung-mitteilen` | Nachricht mit dem Ablehnungsgrund | frei |
| `urlaub-tickytask-eintragen` | Abwesenheit in TickyTask anlegen | frei, z. B. `tickytaskVorgang` |

Der Auftrag trägt die Prozessvariablen, der Worker sieht also den Antrag mitsamt Zeitraum
und Vertretung. Für eine echte Anbindung an ein Fremdsystem ist das mehr, als nötig ist:
Mit `zeebe:ioMapping` am Service-Task bekommt der Worker genau die deklarierten Felder und
sonst nichts — siehe [docs/SERVICE-TASK-WORKER.md](../../docs/SERVICE-TASK-WORKER.md).

## Zwei Entwurfsentscheidungen

**Drei einzelne Tore statt einer zusammengesetzten Bedingung.** Die Engine wertet
Bedingungen je nach Umgebung mit FEEL oder mit dem einfachen Handler aus; letzterer kennt
nur einen Vergleich je Ausdruck, ein `und` gäbe es dort nicht. Drei Tore laufen in beiden
Fällen. Nebeneffekt: Der Ablehnungsgrund ist am Diagramm ablesbar.

**Alle drei Prüfungen laufen immer zu Ende.** Das parallele Tor sammelt erst alle drei
Zweige ein, danach wird entschieden. Eine frühe Ablehnung würde die beiden anderen Zweige
mit hängenden Tokens zurücklassen — und die Lohnbuchhaltung hätte eine Aufgabe in der
Liste, die niemand mehr braucht.

## Was für den echten Einsatz noch fehlt

- **Wer ist die antragstellende Person?** Der erste Task ist niemandem zugewiesen, also für
  alle Zugelassenen sichtbar. Sauberer wäre, die startende Person festzuhalten und den Task
  ihr zuzuweisen — dafür müsste der Instanzstart den Benutzer in eine Variable schreiben.
- **Die Gruppennamen** `Lohnbuchhaltung` und `Vorgesetzte` müssen im Identity Provider als
  Gruppen existieren, sonst sieht niemand die Aufgaben.
- **Die drei Worker** sind zu schreiben. Der Demo-Worker zeigt den Ablauf, nicht die Fachlichkeit.
- **Eine Frist auf den Antrag** (Boundary-Timer) gibt es nicht; die Tasks tragen nur eine
  Fälligkeit zur Anzeige.
