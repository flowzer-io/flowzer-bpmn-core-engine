# Prozesspakete

Ein Prozesspaket trägt einen Workflow samt seiner Formulare aus einer Installation heraus und
in eine andere hinein: eine Vorlage weitergeben, einen geprüften Stand von Test nach Produktion
bringen, einem Partner einen Ablauf geben. Ein Paket ist eine Datei; es enthält alles, was der
Workflow braucht, und **nie** etwas, was nur der Quellinstallation gehört.

## Was nie in einem Paket steht

Diese Liste ist der Kern des Formats, nicht eine Empfehlung:

- **Secrets und Tokens.** Auch keine Secret-*Referenz* wie `env:FLOWZER_AI_PRIMARY`. Wo ein
  Konnektor einen Secret-Namen nennt (`secret:NAME`), wandert nur der Name ins Manifest, damit
  die Zielinstallation weiß, was sie bereitstellen muss.
- **Instanzen, Aufgaben, Historie, Prozessvariablen.**
- **Personen- und Gruppenkennungen.** Eine Verzeichniszuweisung wird auf ihren Anzeigenamen
  reduziert und als „zuzuordnen“ markiert.
- **Kennungen von KI-Verbindungen.** Sie benennen einen Zugang der Quellinstallation; im Paket
  steht nur der Verbindungsname.

Der Export ist deshalb dieselbe Berechtigung wie das Lesen des Workflows: Ein Paket zeigt nichts,
was nicht ohnehin im Modellierer sichtbar wäre.

## Format `flowzer.process-package/1`

Eine ZIP-Datei mit diesen Einträgen:

| Eintrag | Inhalt |
|---|---|
| `package.json` | das Manifest (siehe unten) |
| `workflow.bpmn` | das BPMN der exportierten Fassung, mit Platzhaltern an den installationsgebundenen Stellen |
| `forms/<formId>.json` | je gebundenem Formular sein Schema; eingebettete Formulare heißen `forms/embedded-<nn>.json` |
| `README.md` | erzeugt: Name, Fassung, Formulare, benötigte Zuordnungen |

Das Manifest:

```json
{
  "format": "flowzer.process-package/1",
  "formatVersion": 1,
  "exportedAt": "2026-09-19T08:00:00+00:00",
  "flowzerVersion": "0.1.0",
  "bpmnCapabilitiesContract": 4,
  "formsContract": "flowzer.forms/4",
  "workflow": {
    "definitionId": "workflow-rechnungspruefung",
    "name": "Rechnungsprüfung",
    "version": "2.0",
    "processIds": ["Process_Rechnung"],
    "source": "deployed"
  },
  "forms": [
    {
      "formId": "…",
      "name": "Prüfformular",
      "revision": "1.0",
      "formKey": "Prüfformular",
      "file": "forms/….json",
      "embedded": false
    }
  ],
  "references": [
    {
      "id": "f1002ef0-0000-4000-8000-000000000001",
      "kind": "directoryGroup",
      "elementId": "Pruefen",
      "elementName": "Rechnung prüfen",
      "label": "/team/pruefung",
      "requiresMapping": true
    }
  ]
}
```

### Welche Fassung exportiert wird

Exportiert wird die **veröffentlichte** Fassung; dann steht `workflow.source` auf `deployed` und
die Formulare sind genau die unveränderlichen Bindungen dieser Fassung (siehe
[Formularbindungen](FORM-DEPLOYMENT-BINDINGS.md)). Gibt es keine Veröffentlichung, reist der
zuletzt gespeicherte Entwurf mit, `workflow.source` steht auf `draft`, und die Formularstände
werden so aufgelöst, wie eine Veröffentlichung es täte. Das Manifest sagt das ausdrücklich,
statt einen geprüften Stand vorzutäuschen.

### Platzhalter statt fremder Kennungen

Alles mit `requiresMapping: true` steht im mitgelieferten `workflow.bpmn` **nicht** mit seinem
ursprünglichen Wert, sondern als Platzhalterkennung der Form
`f1002ef0-0000-4000-8000-<laufende Nummer>`. Der Zähler beginnt in jedem Paket wieder bei eins
und trägt deshalb keine Information über die Quellinstallation.

Der Platzhalter ist bewusst eine gültige GUID: So bleibt `workflow.bpmn` ein lesbares
BPMN-Dokument, das sich gegen den [Fähigkeitsvertrag](BPMN-CAPABILITIES.md) prüfen lässt. Ohne
Import ist es allerdings kein lauffähiger Workflow — die Platzhalter zeigen auf nichts.

Ersetzt werden:

- `flowzer:taskAssignment/@assigneeId`, `@candidateUserIds`, `@candidateGroupIds` bei
  `mode="directory"`,
- `flowzer:aiTask/@connectionId`.

Derselbe Wert an mehreren Knoten wird zu **einem** Bezug zusammengefasst: Wer eine Gruppe an drei
Aufgaben verwendet, ordnet sie beim Import einmal zu.

### Arten von Bezügen

| `kind` | Bedeutung | zuzuordnen |
|---|---|---|
| `directoryUser` | Person aus dem Verzeichnis, genannt mit Anzeigenamen | ja |
| `directoryGroup` | Gruppe aus dem Verzeichnis, genannt mit ihrem Pfad | ja |
| `aiConnection` | KI-Verbindung an einem KI-Task, genannt mit ihrem Namen | ja |
| `jobType` | Auftragstyp eines Service-Tasks: „Worker für Typ X nötig“ | nein |
| `secret` | Name einer Secret-Referenz — nur der Name | nein |
| `calledProcess` | `zeebe:calledElement`: der aufgerufene Prozess muss dort bestehen | nein |
| `calledDecision` | `zeebe:calledDecision`: die Entscheidung muss dort bestehen | nein |

Ist ein Anzeigename nicht mehr auflösbar — der Eintrag wurde gelöscht oder stammt aus einem
älteren Verzeichnisstand —, steht `(unbekannter Eintrag)` im Manifest. Die Kennung wird auch
dann nicht ausgegeben.

## API

| Aufruf | Rolle | Ergebnis |
|---|---|---|
| `GET /definition/meta/{id}/package` | wie Workflow lesen | die ZIP-Datei, mit sprechendem Dateinamen |
| `POST /definition/package/preview` | modeler | Manifest, Prüfergebnis, Bezüge mit Vorschlägen, Konflikte |
| `POST /definition/package/import` | modeler, mit Ordnerrecht | legt Workflow und Formulare an |

Vorschau und Import nehmen `multipart/form-data` mit dem Dateifeld `package`; der Import zusätzlich
das Textfeld `mapping` mit diesem JSON:

```json
{
  "mode": "new",
  "definitionId": null,
  "folderId": null,
  "name": "Rechnungsprüfung (Kopie)",
  "references": { "f1002ef0-0000-4000-8000-000000000001": "<Gruppenkennung hier>" }
}
```

`mode` ist `new` (neuer Katalogeintrag) oder `newVersionOf` (neuer Stand eines vorhandenen
Eintrags, dann nennt `definitionId` ihn). Bei `new` wird die Kennung aus dem Paket übernommen,
solange sie frei ist; sonst muss eine eigene genannt werden oder es entsteht eine neue.

### Was der Import tut — und was nicht

Der Import wendet die Zuordnungen an, legt die Formulare an, schreibt das BPMN und speichert es
als neuen Stand. **Veröffentlicht wird nicht.** Ein fremdes Modell in Betrieb zu nehmen ist eine
eigene Entscheidung, die jemand im Modellierer trifft. Der Bericht sagt das ausdrücklich.

Ein Modell, das diese Installation (noch) nicht ausführen könnte, hält den Import **nicht** auf:
Es kommt als Entwurf an und lässt sich hier zu Ende bringen. Die Vorschau meldet es unter
`deployableHere: false` samt Befunden. Eine fehlende Zuordnung hält den Import dagegen auf — sie
kann niemand erraten, und eine halb aufgelöste Definition wäre schlimmer als ein abgelehnter
Import.

### Formulare beim Import

Zugeordnet wird **ausschließlich über die stabile Formularkennung** (`formId`). Über den Namen zu
gehen wäre bequemer, machte aber aus einem gleichnamigen fremden Formular stillschweigend
dasselbe — und ein Import schriebe dann in einen Bestand, den er gar nicht kennt.

| Lage im Zielsystem | Ergebnis | `outcome` |
|---|---|---|
| keine Formularkennung dieser `formId` | neues Formular mit derselben `formId`; ein belegter Name bekommt `(2)` angehängt | `created` |
| gleiche `formId`, inhaltlich identische Fassung vorhanden | diese Fassung wird wiederverwendet, nichts wird geschrieben | `reused` |
| gleiche `formId`, anderer Inhalt | neue Fassung desselben Formulars; vorhandene Stände bleiben unangetastet | `revised` |
| Formular liegt im Diagramm | nichts anzulegen, es reist im BPMN mit | `embedded` |

Verglichen wird der Formularinhalt bis auf Zeilenenden und äußeren Leerraum. Weiter zu
normalisieren wäre eine Auslegung des Schemas und könnte zwei wirklich verschiedene Formulare für
gleich erklären.

Anschließend schreibt der Import jeden Form-Key auf `Name:Fassung` um — auf genau den Stand, den
das Paket mitgebracht hat. Ohne die Fassung im Schlüssel griffe später „die neueste“, und der
importierte Workflow liefe gegen ein Formular, das er nie gesehen hat.

## Grenzen des Uploads

Ein hochgeladenes Archiv kommt von außen und wird entsprechend behandelt:

- Eintragsnamen ohne Pfadanteile, ohne absolute Pfade, ohne `..` („Zip-Slip“) — sonst `400`.
- höchstens 256 Einträge und 8 MiB **entpackt**; gezählt wird beim Lesen, nicht anhand der
  Angabe im Archivverzeichnis, die Teil der hochgeladenen Datei ist.
- fehlendes oder unlesbares Manifest → `400`.
- fremdes Format oder fremde `formatVersion` → `422`; das Manifest wird dafür zuerst nur auf
  `format` und `formatVersion` gelesen, damit ein Paket aus einer künftigen Fassung eine klare
  Auskunft bekommt statt eines Feldfehlers.

## Konsole

Im Katalog steht neben „Neuer Workflow“ der Knopf **Paket importieren** (Rolle fürs
Modellieren): Datei wählen, Vorschau mit Manifest und Prüfergebnis lesen, jeden Bezug zuordnen
— der gleichnamige Eintrag ist vorbelegt, bestätigen muss ihn ein Mensch —, Ziel entscheiden,
importieren. Danach steht der Bericht im Dialog samt „Im Modellierer öffnen“.

**Paket exportieren** liegt auf jeder Workflow-Kachel und in der Werkzeugleiste des
Modellierers. Der Modellierer weist darauf hin, wenn ungespeicherte Änderungen offen sind: Das
Paket enthält den zuletzt gespeicherten Stand, nicht den Entwurf im Editor.

Es gibt derzeit **keinen** Dialog, der eine einzelne BPMN-Datei einliest. Käme einer dazu, bliebe
er getrennt: Eine BPMN-Datei ist ein Diagramm, ein Paket ist ein Workflow samt seinen Formularen.

## Nicht-Ziele

Instanzen und Historie reisen nicht mit. DMN-Dateien sind kein Paketinhalt — eine aufgerufene
Entscheidung wird nur genannt. Pakete werden weder signiert noch verschlüsselt: Wer einem Paket
traut, traut seiner Quelle. Und ein Import veröffentlicht nichts.
