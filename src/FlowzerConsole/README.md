# Flowzer Console

React-Oberfläche für die Flowzer-BPMN-Engine. Sie spricht dieselbe Web-API und denselben Identity Provider wie die Blazor-Oberfläche und läuft unter einer eigenen Adresse, damit beide nebeneinander betrachtet werden können.

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

Die Sitzung liegt in `sessionStorage`: Sie endet mit dem Tab und wandert nicht in andere Fenster. Das ist für einen öffentlichen Client mit PKCE üblich, hat aber eine bekannte Grenze — wer es schafft, fremdes JavaScript in die Seite zu bekommen, kann das Token lesen. Wer diese Grenze nicht akzeptieren will, braucht einen serverseitigen Vermittler, der das Token behält und der Oberfläche nur ein HttpOnly-Cookie gibt. Das ist ein eigener Umbau und keine Einstellung; er lohnt sich, sobald die Entscheidung für diese Oberfläche gefallen ist.

Die API muss im selben Origin liegen. Das mitgelieferte nginx leitet die API-Pfade weiter, deshalb genügt dort `/`. Eine Adresse in einem anderen Origin wird abgelehnt: Das Zugangstoken ginge dorthin, und der Browser gäbe den Header, mit dem die API eine Ablehnung einordnet, ohne ausdrückliche Freigabe gar nicht heraus.

Die Rollen aus dem Token bestimmen, was die Oberfläche anbietet:

| Rolle | Wirkung |
| --- | --- |
| `access` | Zugang überhaupt; ohne sie antwortet die API auf jeden Fachaufruf mit 403 |
| `modeler` | Veröffentlichen von Definitionen und Formularen |
| `operator` | Diagnose, Instanzabbruch, Sicht auf alle Aufgaben |
| `worker` | Aufträge für Service-Tasks abholen |

Wer weder `modeler` noch `operator` trägt, sieht die reduzierte Aufgabenoberfläche statt der vollständigen Konsole. Die Anzeige richtet sich nach den Rollen, die Entscheidung trifft weiterhin die API bei jedem Aufruf.

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
