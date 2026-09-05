# Flowzer Console

Die Oberfläche der Flowzer-BPMN-Engine: React, TypeScript, Vite. Sie enthält den
BPMN-Modellierer (bpmn-js mit Camunda-8-Eigenschaftenpanel), den Formulareditor (Form.io),
die Instanz- und Aufgabenansichten sowie den Betriebsbereich. Die frühere Blazor-Oberfläche
ist entfernt; diese hier ist die einzige.

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

Die reduzierte Aufgabenansicht bleibt erhalten, aber als eigene Wahl im Benutzermenü unter „Umfang“. Sie ist keine Folge fehlender Rollen mehr; das war strenger als die API und verbarg Ansichten, die dem Zugang offenstanden.

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

Heißen die Rollen im Identity Provider anders als `access`, `modeler`, `operator` und `worker`, gehören die abweichenden Namen unter `roleNames` in die `config.json`. Die API wertet sie ebenfalls konfigurierbar aus; beide Seiten müssen dieselben Namen kennen.

Im Entwicklungsbetrieb ohne `config.json` greifen die `VITE_`-Werte aus `.env`.

## Fremde Oberflächen im Bündel

Zwei Bibliotheken bringen eine eigene, fest verdrahtete Optik mit. Beide sind deshalb an
die Design-Tokens der Konsole angeglichen, und beides ist leicht zu übersehen:

- **bpmn-js und das Eigenschaftenpanel** (`src/components/bpmn/bpmn.css`). Das Panel
  deklariert seine Farben auf `.bio-properties-panel` selbst; Überschreibungen müssen
  deshalb auf demselben Element stehen, nicht auf dem Rahmen darum. Die Regeln für
  Palette und Kontextmenü kommen bewusst ohne `:where()` aus, weil die eigenen Regeln von
  diagram-js zweistufig sind.
- **Form.io** (`src/components/forms/formio.css`). Form.io setzt Bootstrap-5-Vorlagen und
  Bootstrap-Symbole voraus. Bootstrap global einzubinden würde Tailwind überschreiben,
  deshalb sind nur die tatsächlich verwendeten Bausteine nachgezogen — begrenzt auf
  `.formio-surface` **und** `.formio-dialog`. Der Eigenschaftendialog des Editors hängt am
  `<body>`, also außerhalb jeder Seitenfläche; Regeln nur unter `.formio-surface`
  erreichen ihn nicht.

## Volle Höhe

Seiten, die den Rest des Fensters füllen (Modellierer, Instanzansicht), hängen sich als
`flex min-h-0 flex-1` in die Flex-Spalte von `AppShell` ein. Eine Prozenthöhe (`h-full`)
gegen einen Flex-Container löst Safari nicht auf — die Zeichenfläche wäre dort 0 Pixel hoch,
und der Modellierer schiene gar nicht erst zu starten.
