# Flowzer Console

Die Oberfläche der Flowzer-BPMN-Engine: React, TypeScript, Vite. Sie enthält den
BPMN-Modellierer (bpmn-js mit einem eigenen Eigenschaften-Panel), den Formulareditor
(Form.io), die Instanz- und Aufgabenansichten sowie den Betriebsbereich. Die frühere
Blazor-Oberfläche ist entfernt; diese hier ist die einzige.

## Entwickeln

```bash
npm ci
npm run dev
```

Der Entwicklungsserver läuft auf `http://localhost:5273` und leitet `/api` an die Web-API weiter (`FLOWZER_API_URL`, Standard `http://localhost:5182`). Ohne konfigurierten Identity Provider meldet sich die Konsole als technischer Benutzer über den Header `X-Flowzer-UserId`; die API akzeptiert das nur im Entwicklungsmodus.

| Befehl | Zweck |
| --- | --- |
| `npm run dev` | Entwicklungsserver |
| `npm run build` | Produktionsbündel nach `dist/` |
| `npm run typecheck` | Typprüfung ohne Ausgabe |
| `npm run lint` | Linter |
| `npm run test` | Unit-Tests |
| `npm run api:types` | Typen aus der OpenAPI-Beschreibung erzeugen |

## Anmeldung

Authorization Code Flow mit PKCE gegen den konfigurierten Identity Provider. Die Konsole ist ein öffentlicher Client ohne Geheimnis; ein Geheimnis im Browser wäre keines. Das Zugangstoken geht als Bearer an die API und wird im Hintergrund über eine eigene, minimale Seite erneuert.

Die Sitzung liegt in `sessionStorage`: Sie endet mit dem Tab und wandert nicht in andere Fenster. Das ist für einen öffentlichen Client mit PKCE üblich, hat aber eine bekannte Grenze — wer es schafft, fremdes JavaScript in die Seite zu bekommen, kann das Token lesen. Wer diese Grenze nicht akzeptieren will, braucht einen serverseitigen Vermittler, der das Token behält und der Oberfläche nur ein HttpOnly-Cookie gibt. Das ist ein eigener Umbau und keine Einstellung; er steht als nächster Härtungsschritt an.

Die API muss im selben Origin liegen. Das mitgelieferte nginx leitet die API-Pfade weiter, deshalb genügt dort `/`. Eine Adresse in einem anderen Origin wird abgelehnt: Das Zugangstoken ginge dorthin, und der Browser gäbe den Header, mit dem die API eine Ablehnung einordnet, ohne ausdrückliche Freigabe gar nicht heraus.

Die Rollen aus dem Token bestimmen, was die Oberfläche anbietet:

| Rolle | Wirkung |
| --- | --- |
| `access` | Zugang überhaupt; ohne sie antwortet die API auf jeden Fachaufruf mit 403 |
| `modeler` | Veröffentlichen von Definitionen und Formularen |
| `operator` | Diagnose, Instanzabbruch, Sicht auf alle Aufgaben |
| `worker` | Aufträge für Service-Tasks abholen |

Die vollständige Konsole steht jedem Zugelassenen offen: Definitionen, Instanzen und Formulare darf die API jeder zugelassenen Person zeigen. Erst was schreibt oder den Betrieb betrifft, verlangt eine Rolle — dann bietet die Oberfläche es gar nicht erst an, statt es anzubieten und ablehnen zu lassen:

| Ohne Rolle nicht sichtbar | Verlangt |
| --- | --- |
| Speichern und Deployen im Modellierer, neue Workflows und Formulare anlegen | `modeler` |
| Der Bereich Betrieb mit Diagnose und Timern | `operator` |

Wer die Zugangsrolle nicht hat, bekommt die reduzierte Aufgabenansicht — die vollständige Konsole zeigte dann nur eine Reihe abgelehnter Aufrufe.

Die Anzeige richtet sich nach den Rollen, die Entscheidung trifft weiterhin die API bei jedem Aufruf.

## Konfiguration zur Laufzeit

Ein gebautes Bündel ist unveränderlich; die Adressen dürfen deshalb nicht beim Bauen feststehen, sonst braucht jede Umgebung ein eigenes Image. Der Container schreibt beim Start `config.json`, und die Anwendung lädt sie, bevor sie das erste Mal zeichnet.

| Umgebungsvariable | Bedeutung |
| --- | --- |
| `FLOWZER_API_BASE_URL` | Basisadresse der API, im Container `/` (das mitgelieferte nginx leitet weiter) |
| `FLOWZER_API_UPSTREAM` | Ziel der Weiterleitung als `host:port`, z. B. `api:8080` |
| `FLOWZER_OIDC_AUTHORITY` | OIDC-Issuer; leer heißt: ohne Anmeldung |
| `FLOWZER_OIDC_CLIENT_ID` | Client-Id der Konsole |
| `FLOWZER_OIDC_AUDIENCE` | Audience der API im Token; unter ihr stehen die Clientrollen |
| `FLOWZER_OIDC_SCOPES` | zusätzliche Scopes über `openid profile email` hinaus |
| `FLOWZER_ACCENT` | Akzentfarbe: `iris`, `teal`, `emerald`, `amber` oder `rose` |

Heißen die Rollen im Identity Provider anders als `access`, `modeler`, `operator` und `worker`, gehören die abweichenden Namen unter `roleNames` in die `config.json`. Die API wertet sie ebenfalls konfigurierbar aus; beide Seiten müssen dieselben Namen kennen.

Im Entwicklungsbetrieb ohne `config.json` greifen die `VITE_`-Werte aus `.env`.

## Darstellung

Im Benutzermenü steht genau eine Einstellung: hell, dunkel oder wie das Betriebssystem.
Das ist eine Frage des Arbeitsplatzes und der Tageszeit.

Die **Akzentfarbe** steht bewusst nicht dort. Sie gehört zum Erscheinungsbild des
Unternehmens, gilt deshalb für alle gleich und wird bei der Bereitstellung über
`FLOWZER_ACCENT` gesetzt. Ein unbekannter Wert fällt still auf `iris` zurück: Ein
Tippfehler in der Farbe darf die Oberfläche nicht am Starten hindern.

Dichte und Umfang der Ansicht waren Schalter aus dem Entwurf und sind entfallen.

## Eigenschaften-Panel des Modellierers

Das Panel neben dem Diagramm ist ein eigenes React-Panel und nicht das mitgelieferte
`bpmn-js-properties-panel`. Es zeigt, was diese Engine auswertet, und benennt es in Flowzers
Begriffen: Name, Formular, Zuweisung, Frist, Zuordnungen, Auftragstyp und Wiederholungen,
Zeitangabe, Nachricht samt Korrelationsschlüssel, Signal, aufgerufener Prozess, Skript,
Mehrfachausführung und an Toren die Bedingungen der ausgehenden Flüsse.

Vier Dateien, vier Aufgaben:

| Datei | Aufgabe |
|---|---|
| `bpmn/moddle.ts` | Typen und Lesehilfen für das BPMN-Objektmodell, ohne Seiteneffekte |
| `bpmn/elementProperties.ts` | Liest ein Element in die flachen Werte des Panels |
| `bpmn/bpmnEditor.ts` | Schreibt ins Modell — die einzige Datei, die das tut |
| `bpmn/properties/*` | Die Oberfläche: Abschnitte, Felder, der Formulareditor |

Die Schreiber nehmen **Teiländerungen** und mischen sie mit dem Modellstand. Das ist kein
Komfort: Ein Textfeld schreibt erst beim Verlassen. Klickt jemand aus einem Feld heraus direkt
auf einen Schalter derselben Gruppe, laufen beide Schreiber nacheinander — der zweite mit den
Werten aus dem Bild *vor* dem ersten. Gäbe er die ganze Gruppe mit, machte er die eben
getippte Eingabe wieder zunichte.

**Der Umfang folgt `src/core-engine/ModelParser.cs`.** Was der Parser liest, gehört ins
Panel; was das Panel anbietet, muss der Parser lesen. Ein Feld ohne Wirkung ist derselbe
Fehler wie eine Angabe, die sich nur im XML setzen lässt. Beide Seiten haben Tests
(`elementProperties.test.ts` liest, `bpmnEditor.test.ts` schreibt gegen ein Modeler-Doppel).

Nicht im Panel und bewusst nicht: die Wahl der Elementart selbst — ob ein Ereignis
unterbrechend ist, ob eine Mehrfachausführung sequenziell läuft, welcher Ereignistyp
vorliegt. Das entscheidet in bpmn-js das Kontextmenü am Element, und zwei Bedienwege für
dieselbe Sache wären ein Widerspruch. Ohne Modelliererrolle ist das Panel schreibgeschützt,
Speichern und Deployen sind ausgeblendet; die Zeichenfläche selbst bleibt bedienbar.

Zwei Dinge sind bewusst so und leicht wieder kaputtzumachen:

- **`camunda-bpmn-js-behaviors` läuft nicht mit.** Das Modul setzt Camundas 8.5-Semantik
  durch: Jede neu gezeichnete menschliche Aufgabe bekäme ein `zeebe:userTask`, und ihr
  Form-Key wanderte danach nach `zeebe:externalReference` — ein Attribut, das der Parser
  dieser Engine nicht liest. Ein so modellierter Workflow ließe sich nicht mehr speichern.
- **Das Formular im Workflow wird auf einer eigenen Vollbildfläche bearbeitet**
  (`properties/EmbeddedFormDialog.tsx`), nicht im Dialog aus `ui/Modal`. Form.io hängt
  seinen Eigenschaftendialog ans `<body>` (siehe unten); für Radix ist ein Klick darin ein
  Klick nach außen, und der umgebende Dialog schloss sich beim ersten Feldklick.

## Fremde Oberflächen im Bündel

Zwei Bibliotheken bringen eine eigene, fest verdrahtete Optik mit. Beide sind deshalb an
die Design-Tokens der Konsole angeglichen, und beides ist leicht zu übersehen:

- **bpmn-js** (`src/components/bpmn/bpmn.css`). Die Bibliothek ist über Custom Properties
  thembar, deklariert sie aber auf `.djs-parent` beziehungsweise `.bjs-container`
  **selbst** — Überschreibungen müssen deshalb auf denselben Elementen stehen, nicht auf
  dem Rahmen darum. Zwei Graustufen dienen dort als Fläche und nicht als Text; sie sind
  einzeln herausgezogen, sonst stünde heller Text auf hellgrauem Grund.
- **Form.io** (`src/components/forms/formio.css`). Form.io setzt Bootstrap-5-Vorlagen und
  Bootstrap-Symbole voraus. Bootstrap global einzubinden würde Tailwind überschreiben,
  deshalb sind nur die tatsächlich verwendeten Bausteine nachgezogen — begrenzt auf
  `.formio-surface` **und** `.formio-dialog`. Der Eigenschaftendialog des Editors hängt am
  `<body>`, also außerhalb jeder Seitenfläche; Regeln nur unter `.formio-surface`
  erreichen ihn nicht. Beim Auswahl-Widget (Choices.js) sind einige Vendor-Selektoren
  dreistufig; die Überschreibungen bauen sie nach, sonst verlieren sie.

  Alle Blätter kommen aus `formioStyles.ts` in fester Reihenfolge. Importierte jede
  Komponente für sich, entschied die Ladereihenfolge der Module, welches zuletzt steht —
  und der Editor zog sein Blatt erst beim Öffnen nach, also nach unseren Anpassungen.

## Volle Höhe

Seiten, die den Rest des Fensters füllen (Modellierer, Instanzansicht), hängen sich als
`flex min-h-0 flex-1` in die Flex-Spalte von `AppShell` ein. Eine Prozenthöhe (`h-full`)
gegen einen Flex-Container löst Safari nicht auf — die Zeichenfläche wäre dort 0 Pixel hoch,
und der Modellierer schiene gar nicht erst zu starten.
