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

Die ausgelieferte Konsole verwendet einen **same-origin Backend-for-Frontend
(BFF)**. Der Browser startet die Anmeldung über `GET /bff/login`; die Web-API
führt den Authorization-Code-Flow mit PKCE als **vertraulicher OIDC-Client** aus.
Das Client-Secret und erhaltene Access-Tokens bleiben im API-Prozess. Weder
`config.json` noch JavaScript, `sessionStorage` oder `localStorage` enthalten
Tokens oder OIDC-Clientdaten.

Nach erfolgreicher Anmeldung setzt die API die Cookies `__Host-Flowzer-Session`
und `__Host-Flowzer-Csrf`: beide sind `HttpOnly`, `Secure`, haben `Path=/` und
sind damit nur unter HTTPS auf genau diesem Host gültig. Die Sitzung liegt im
Cookie, nicht im Browser-Speicher. Die Konsole liest nur die datensparsame
Sitzungsprojektion von `GET /bff/session`.

Für jede schreibende Cookie-Anfrage holt die Konsole bei `GET /bff/csrf` einen
nur im JavaScript-Speicher gehaltenen Request-Token und sendet ihn im Header
`X-Flowzer-CSRF`. Die API verlangt zusätzlich einen gleichen Origin. Logout ist
ebenfalls ein CSRF-geschütztes `POST /bff/logout`. Liefert es mit aktivem
Provider-Logout eine Abmeldeadresse des Identity Providers (`redirectTo`, nur absolute
HTTPS-Adressen), navigiert die Konsole nach der lokalen Abmeldung dorthin. Direkte API-Konsumenten dürfen
weiterhin `Authorization: Bearer …` verwenden; dieser Vertrag ist nicht
CSRF-pflichtig und ein fehlerhafter Bearer fällt nicht auf ein vorhandenes Cookie
zurück.

Konsole und API müssen unter derselben HTTPS-Origin liegen. Das mitgelieferte
nginx leitet die API- und `/bff`-Pfade weiter, daher genügt als API-Basis `/`.
Eine fremde Origin wird abgelehnt. Für lokale Entwicklungsprüfungen bleibt
`Authentication:Scheme=None` mit technischem Benutzer möglich. `JwtBearer`
bleibt für direkte/externe Bearer-Clients kompatibel, betreibt die gelieferte
Konsole aber nicht als Browser-Anmeldung; dafür ist `Bff` zu konfigurieren.

Die Rollen werden serverseitig aus dem validierten Access-Token in die minimale
BFF-Sitzung projiziert und bestimmen, was die Oberfläche anbietet:

| Rolle | Wirkung |
| --- | --- |
| `access` | Zugang überhaupt; ohne sie antwortet die API auf jeden Fachaufruf mit 403 |
| `modeler` | Veröffentlichen von Definitionen und Formularen |
| `operator` | Diagnose, Instanzabbruch, Instanzmigration, Sicht auf alle Aufgaben |
| `worker` | Aufträge für Service-Tasks abholen |

Die vollständige Konsole steht jedem Zugelassenen offen: Definitionen, Instanzen und Formulare darf die API jeder zugelassenen Person zeigen. Erst was schreibt oder den Betrieb betrifft, verlangt eine Rolle — dann bietet die Oberfläche es gar nicht erst an, statt es anzubieten und ablehnen zu lassen:

| Ohne Rolle nicht sichtbar | Verlangt |
| --- | --- |
| Speichern und Deployen im Modellierer, neue Workflows und Formulare anlegen | `modeler` |
| Der Bereich Betrieb mit Diagnose und Timern | `operator` |

Wer die Zugangsrolle nicht hat, bekommt die reduzierte Aufgabenansicht — die vollständige Konsole zeigte dann nur eine Reihe abgelehnter Aufrufe.

Der Modellierer wird ohne `modeler` zur Ansicht: Das Diagramm lässt sich betrachten, zoomen und auswählen, aber nicht ändern — keine Palette, kein Kontextpad, kein Verschieben oder Löschen. Das Eigenschaften-Panel zeigt weiterhin alle Werte, nimmt aber keine an. Sonst entstünden Änderungen, die niemand speichern kann, und die Seite warnte beim Verlassen davor.

Die Anzeige richtet sich nach den serverseitig projizierten Fähigkeiten, die Entscheidung trifft weiterhin die API bei jedem Aufruf.

Damit geänderte Rollen ohne Reload ankommen, fragt die Konsole `GET /bff/session` nicht nur beim Start ab: Bei angemeldeter BFF-Sitzung wiederholt sie die Abfrage alle fünf Minuten, solange das Fenster sichtbar ist, und sofort bei Rückkehr ins Fenster (Fokus oder Sichtbarkeitswechsel) — höchstens einmal je 30 Sekunden und nie parallel (`src/lib/auth/useSessionWatch.ts`). Der BFF ersetzt die Rollen bei der serverseitigen Token-Erneuerung; ein Entzug im Identity Provider nimmt der Oberfläche die betreffenden Aktionen so spätestens mit der nächsten Sitzungsabfrage und ohne Reload. Bis dahin kann die API eine entzogene Aktion bereits mit 403 ablehnen; maßgeblich bleibt sie bei jedem Aufruf. Schlägt eine solche Abfrage vorübergehend fehl, erscheint nur der Verbindungshinweis, niemand wird abgemeldet. Kommt ihre Antwort erst nach dem Abmelden oder nach einem 401 an, verwirft die Konsole sie, statt die Sitzung wieder als angemeldet zu zeigen.

## Konfiguration zur Laufzeit

Ein gebautes Bündel ist unveränderlich; der Container schreibt nur unkritische
Bereitstellungswerte nach `config.json`, bevor die Anwendung zeichnet. OIDC-
Authority, Client-ID, Scopes, Rollenbezeichnungen, Secrets und Tokens gehören
**nicht** zu dieser Datei und nicht in den Konsolen-Container.

| Umgebungsvariable | Bedeutung |
| --- | --- |
| `FLOWZER_API_BASE_URL` | Basisadresse der API, im Container `/` (das mitgelieferte nginx leitet weiter) |
| `FLOWZER_API_UPSTREAM` | Ziel der Weiterleitung als `host:port`, z. B. `api:8080` |
| `FLOWZER_BFF_ENABLED` | `true` für die ausgelieferte BFF-Konsole; lokale Entwicklung kann explizit `false` setzen |
| `FLOWZER_ACCENT` | Akzentfarbe: `iris`, `teal`, `emerald`, `amber` oder `rose` |

Im Entwicklungsbetrieb ohne `config.json` greifen nur die nicht geheimen `VITE_`
Werte. Ein lokaler technischer Benutzer setzt weiterhin `Authentication:Scheme=None`
voraus; er ist keine Produktions-Anmeldevariante.

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

Denselben Formularabschnitt trägt das **reine Startereignis** — dort meint er das
*Startformular*, das ausfüllt, wer den Workflow startet. Es ist freiwillig: Ohne Formular
startet der Workflow direkt, und deshalb warnt das Panel dort nicht vor einem fehlenden
Verweis (an einer menschlichen Aufgabe tut es das weiterhin). An einem Start mit Zeit-,
Nachrichten- oder Signaldefinition wird der Abschnitt nicht gezeigt: Dort gäbe es niemanden,
der ausfüllt, und der Parser liest den Schlüssel folgerichtig nicht. Ebenso wenig am
Startereignis eines Subprozesses — das startet den Subprozess, nicht den Workflow. Die Übersicht
„Formulare in diesem Workflow" und die Markierung im Diagramm führen das Startformular mit
(`bpmnEditor.listFormOwners()`).

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

## Einen Workflow starten

Gestartet wird an drei Stellen — auf der Workflow-Karte, im Schnellstart des Dashboards und
im Modellierer. Alle drei benutzen `components/workflows/useStartWorkflow.ts` und rendern
`StartWorkflowDialog` genau einmal; vorher stand die Toast-Logik dreimal da.

Der Ablauf: Der Knopf holt zuerst `GET /definition/meta/{id}/start-form`. Antwortet die API
mit **204**, hat der Workflow kein Startformular und die Instanz startet sofort — der
frühere Weg, unverändert. Kommt ein Formular, öffnet sich der Dialog; „Starten" prüft dort
die Pflichtfelder über den Form.io-Renderer und schickt die Eingaben als `variables` an
`POST /definition/meta/{id}/instance`.

Die Pflichtfelder prüft bewusst nur die Konsole: Form.io kennt bedingt sichtbare Felder
(`conditional`), die der Server nicht auswertet — er würde damit gültige Eingaben ablehnen.
Der Server prüft deshalb nur, ob überhaupt ein `variables`-Objekt kam; ein leeres zählt als
Antwort.

Datumsfelder brauchen im Dialog eine Sonderbehandlung
(`components/forms/dialogCalendarWidget.ts`). Form.io baut sie mit flatpickr, und flatpickr
hängt seinen Kalender ans `<body>` — außerhalb des Dialogs. Dort sperrt der modale Dialog
aber die Zeigereingaben (`pointer-events: none` am Body) und hält den Tastaturfokus fest:
Der Klick auf einen Tag traf das Overlay, galt flatpickr als Klick nach außen und klappte
den Kalender wieder zu. Von Hand tippen ging, auswählen nicht. Der Renderer tauscht deshalb
einmalig das Kalender-Widget in Form.ios Registry gegen eine Unterklasse, die den Kalender
im Dialog der Konsole an eine eigene Ebene im Dialogelement hängt (flatpickrs `appendTo`)
— also weiter innerhalb von `role="dialog"`, aber außerhalb seiner Bildlauffläche — und ihn
selbst positioniert: unter oder über dem Feld, im Dialog, wo er hineinpasst, und immer im
Fenster. Solange er offen ist, folgt er dem Feld, wenn der Dialoginhalt rollt oder seine Größe
ändert; rollt das Feld ganz aus dem sichtbaren Inhalt, klappt er zu. Escape schließt zuerst
den offenen Kalender und erst beim nächsten Druck den Dialog (`components/ui/Modal.tsx`).

Erkannt wird der Dialog am Attribut `data-flowzer-modal`, das nur `ui/Modal` setzt. Form.ios
eigener Einstellungsdialog im Editor trägt zwar auch `role="dialog"`, sperrt aber weder Zeiger
noch Fokus und ist selbst eine Bildlauffläche — dort, auf der Aufgabenseite und überall sonst
außerhalb des Modals bleibt flatpickr bei seinem Standard am `<body>`.

Die frühere Lösung über flatpickrs `static` stellte den gut 300 px breiten Kalender als Block
in die Bildlauffläche. In einer halben Dialogspalte oder auf dem Telefon war er breiter als
der Platz; der Dialog rollte seitlich und schnitt Beschriftungen ab (Issue #368).

Die Sprache des Renderers — Aufgabenformulare, Startdialog, Vorschau — folgt dem Browser
(`lib/locale.ts`): Deutsch oder Englisch, alles andere fällt auf Deutsch zurück. Der
Renderer gibt sie als `language` an Form.io (`components/forms/formioLanguage.ts`). Der
Formular-Editor bleibt dagegen fest deutsch wie die übrige Konsole; seine eigenen Texte
(„Übernehmen“, Palettengruppen) gibt es nur auf Deutsch.

Form.io bringt deutsche Prüfmeldungen selbst mit (`ist erforderlich`, Mindest-/Höchstlänge,
Muster, E-Mail, Datum, Mindest-/Höchstwert …) und reicht die Sprache an flatpickr weiter, das
die passende Übersetzung vom selben CDN lädt wie sich selbst: deutsche Wochentage und Monate,
Wochenbeginn Montag, das Datumsformat bestimmt weiter das Feld. Die Übersetzung holt Form.io
wie flatpickr selbst per Nachladen mit Abfrage ohne Zeitlimit (`Formio.requireLibrary`) — ein
vorbestehendes Muster; schlägt das Laden fehl, bleibt der Kalender englisch. Einige häufige
Renderer-Texte, die Form.io nicht übersetzt, ergänzt `formioLanguage.ts`; weitere Meldungen —
etwa rund um Datei-Upload oder Unterschrift — bleiben englisch.

Bewusst in Kauf genommen: `language` steuert in Form.io mehr als Texte. Zahlen- und
Währungsfelder formatieren danach Dezimal- und Tausendertrennzeichen, das Tagesfeld (`day`)
ordnet danach Tag, Monat und Jahr. Vorher galt für Zahlen die Browsersprache selbst; jetzt
bekommt etwa ein Browser mit `de-CH` das deutsche Dezimalkomma statt der Schweizer Trennzeichen,
und nicht unterstützte Sprachen wie Französisch bekommen deutsche Formatierung — passend zur
deutschen Oberfläche.

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

## Gliederung neben dem Diagramm

Ein Workflow lässt sich auf zwei Wegen bearbeiten:

- `/workflows/<id>` — das Diagramm (bpmn-js). Kann alles, was BPMN kann.
- `/workflows/<id>/gliederung` — die **Gliederung**: der Ablauf als senkrechte Liste,
  parallele Blöcke und Zweige eingerückt, Formular, Zuständigkeit und Frist direkt am
  Schritt. Für Fachleute, die einen Workflow lesen und nachjustieren, statt ihn zu zeichnen.

Die Gliederung deckt bewusst nur einen Ausschnitt von BPMN ab. Sie speichert nur, was sie
vollständig abbildet; alles andere meldet sie und verweist aufs Diagramm. Welcher Ausschnitt
das ist und wie das abgesichert wird, steht in
[docs/GLIEDERUNG-TEILMENGE.md](../../docs/GLIEDERUNG-TEILMENGE.md). Der Code liegt in
`src/lib/outline/` (Lesen, Schreiben, Anordnung, Bearbeiten) und
`src/components/outline/` (Darstellung).

Status: **Prototyp** zum Ausprobieren, keine fertige Funktion.

## Volle Höhe

Seiten, die den Rest des Fensters füllen (Modellierer, Instanzansicht), hängen sich als
`flex min-h-0 flex-1` in die Flex-Spalte von `AppShell` ein. Eine Prozenthöhe (`h-full`)
gegen einen Flex-Container löst Safari nicht auf — die Zeichenfläche wäre dort 0 Pixel hoch,
und der Modellierer schiene gar nicht erst zu starten.
