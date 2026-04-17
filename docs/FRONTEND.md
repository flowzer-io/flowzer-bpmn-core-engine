# Frontend und UI-Smoke-Tests

## Ziel

Das Blazor-Frontend soll reproduzierbar gegen die Web-API laufen – lokal, in CI und später auch hinter einem Reverse Proxy.

## API-Konfiguration

Die Frontend-App liest ihre API-Basisadresse aus `FlowzerApi:BaseUrl`:

- Standard: `src/FlowzerFrontend/wwwroot/appsettings.json`
- lokale Entwicklung: `src/FlowzerFrontend/wwwroot/appsettings.Development.json`
- UI-Smoke-/Playwright-Läufe: `src/FlowzerFrontend/wwwroot/appsettings.Playwright.json`

Aktuell gilt damit bewusst:

- **Produktion / Same-Origin:** Standardwert `/`
- **Lokale Entwicklung:** `http://localhost:5182/`
- **Verwaltete Playwright-Läufe:** `http://localhost:5288/`

Damit lässt sich das Frontend lokal direkt gegen die Web-API starten, ohne dass im Code harte URLs verdrahtet bleiben.

Zusätzlich kann `FlowzerApi:DevelopmentUserId` gesetzt werden. Wenn das Frontend selbst im `Development`- oder `Playwright`-Modus läuft, sendet es diesen Wert automatisch als `X-Flowzer-UserId` an die Web-API. Damit bleiben lokal gehärtete Definition-, User-Task- und Formularpfade testbar, ohne Produktionsumgebungen wieder für freie Header-Impersonation zu öffnen.

## Lokaler Start

### Web-API

```bash
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project src/WebApiEngine/WebApiEngine.csproj \
  --configuration Release \
  --no-launch-profile \
  --urls http://localhost:5182
```

### Frontend

```bash
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project src/FlowzerFrontend/FlowzerFrontend.csproj \
  --configuration Release \
  --no-launch-profile \
  -p:WasmApplicationEnvironmentName=Development \
  --urls http://localhost:5269
```

Danach ist das Frontend unter [http://localhost:5269](http://localhost:5269) erreichbar.

### Hinweis zu unerwarteten JavaScript-Haltepunkten

Die Standard-Launch-Profile des Frontends tragen bewusst **kein** `inspectUri` mehr. Dadurch soll der normale lokale Start nicht automatisch einen JS-Debug-Proxy für die WebAssembly-App aufziehen.

Wenn gezieltes JavaScript-Debugging gewünscht ist, stehen dafür jetzt separate Profile zur Verfügung:

- `http-js-debug`
- `https-js-debug`
- `IIS Express JS Debug`

Damit bleibt der normale Blazor-Start ruhiger, ohne den expliziten Debug-Pfad ganz zu verlieren.

## Automatisierte UI-Smoke-Tests

Die Smoke- und kleinen Happy-Path-E2E-Tests liegen unter `tests/ui-smoke` und prüfen aktuell die Kernrouten:

- `/`
- `/models`
- `/forms`
- `/instances`
- `/instances/all`
- `/instances/active`
- `/instances/done`
- `/instances/error`

Geprüft werden insbesondere:

- erfolgreiche Seitennavigation
- sichtbare Kern-UI je Route
- funktionierende Filter-Navigation innerhalb der Instanzliste
- API-gesäte Happy Paths für Formulare, Modelle und Instanzverläufe
- explizite **Open**-/`Open deployed`-/`Start instance`-Aktionen in der Modellliste für deployte Workflows
- kein sichtbarer Blazor-Fatalfehler
- keine fehlgeschlagenen Browser-Requests

## Aktuelle Frontend-Konventionen für Kernseiten

### UI-/UX-Refresh April 2026

Der aktuelle Stand auf `next` enthält einen größeren UX-Rundumschlag für die wichtigsten Frontend-Flächen. Ziel war nicht nur optische Politur, sondern vor allem eine klarere Bedienlogik:

- **einheitliche App-Shell** mit klarer Hauptnavigation und stärkerem Produktcharakter
- **explizite Primäraktionen** statt versteckter Titel-Links
- **bessere Empty-/Error-/Loading-States** auf Listen- und Detailseiten
- **konsistentere List-/Detailmuster** für Workflows, Formulare und Runtime-Instanzen
- **stärkere Runtime-Transparenz** in der Instanzdetailansicht (Diagramm, Token, Subscriptions, Payloads)

Die fachlichen Leitgedanken und Folgeideen sind in [docs/UI-UX-AUDIT-2026-04.md](UI-UX-AUDIT-2026-04.md) dokumentiert.

- Listen- und Startseiten zeigen bei Ladefehlern eine sichtbare Inline-Fehlermeldung statt still abzustürzen.
- Leere Listen rendern einen expliziten Empty State.
- Instanzfilter werden ausschließlich über gültige Frontend-Routen (`all`, `active`, `done`, `error`) adressiert.
- Die Instanzliste lädt bewusst auch abgeschlossene bzw. fehlgeschlagene Instanzen, damit `done`- und `error`-Filter echte Daten anzeigen können.
- Formularrouten werden defensiv geparst, damit ungültige URLs nicht in eine ungefangene `Guid.Parse`-Ausnahme laufen.

### Einmalig installieren

```bash
npm --prefix tests/ui-smoke ci
npm --prefix tests/ui-smoke run install:browsers
```

### Smoke-Tests ausführen

```bash
dotnet build core-engine.sln --configuration Release
npm --prefix tests/ui-smoke run test
```

Die Playwright-Konfiguration startet Web-API und Frontend standardmäßig selbst. Wenn beide Prozesse bereits laufen, kann der automatische Start übersprungen werden:

```bash
PLAYWRIGHT_SKIP_WEBSERVERS=1 npm --prefix tests/ui-smoke run test
```

Wenn dieser Skip-Pfad verwendet wird, muss das Frontend unter **.NET 10** ebenfalls explizit mit der gewünschten WASM-Umgebung gestartet werden. Für den Playwright-Fall sieht ein passender manueller Start z. B. so aus:

```bash
ASPNETCORE_ENVIRONMENT=Playwright \
dotnet run --project src/FlowzerFrontend/FlowzerFrontend.csproj \
  --configuration Release \
  --no-build \
  --no-launch-profile \
  -p:WasmApplicationEnvironmentName=Playwright \
  --urls http://localhost:5290
```

Alternativ kann die Playwright-Konfiguration über `FLOWZER_FRONTEND_ENVIRONMENT=<env>` auf eine andere Client-Umgebung gestellt werden; wichtig ist in jedem Fall, dass `WasmApplicationEnvironmentName` und die erwartete `appsettings.<env>.json` zusammenpassen.

Für verwaltete Playwright-Läufe werden absichtlich eigene Ports genutzt, damit lokale Debug-Sessions auf `5182`/`5269` nicht mit den Smoke-Tests kollidieren:

- Web-API: `http://localhost:5288`
- Frontend: `http://localhost:5290`

Seit dem Upgrade auf **.NET 10** wird die Client-Umgebung einer Standalone-Blazor-WASM-App nicht mehr zuverlässig über `launchSettings.json` bzw. nur über den Host-Prozess gesteuert. Für die UI-Smokes startet die Playwright-Konfiguration das Frontend deshalb explizit mit `-p:WasmApplicationEnvironmentName=Playwright`, damit weiterhin `appsettings.Playwright.json` statt `appsettings.Development.json` geladen wird.

Der `npm test`-Pfad verwendet zusätzlich einen kleinen Prozesswächter. Dieser erkennt alte verwaiste `ms-playwright`-/`chrome-headless-shell`-Prozesse aus früheren Läufen und räumt nach dem Test auch neu entstandene Browser-Reste wieder weg. Damit bleiben keine CPU-intensiven Headless-Prozesse mehr unbemerkt liegen.

Bei Bedarf kann das Verhalten angepasst werden:

- `PLAYWRIGHT_SKIP_PROCESS_GUARD=1` deaktiviert den Wächter
- `PLAYWRIGHT_PROCESS_STALE_THRESHOLD_SECONDS=<sekunden>` steuert, ab wann alte Browser-Prozesse als verwaist gelten
- `FLOWZER_DEVELOPMENT_USER_ID=<guid>` überschreibt den technischen Development-Benutzer für API-Seed- und Runtime-Hilfsaufrufe der UI-Smokes

## Diagramm- und Modeler-Prüfpfade

Für die BPMN-Seiten gelten jetzt zusätzlich ein paar harte Erwartungswerte:

- `/definition/{metaDefinitionId}` lädt Canvas **und** Properties Panel reproduzierbar
- ein Speichern aus dem Modeler sendet den bisherigen Definitionsstand als `previousGuid`, damit Versionsketten im Backend nachvollziehbar bleiben
- deployte Workflows lassen sich aus der Modellliste und aus der deployten Definitionsansicht direkt als neue Instanz starten
- die Frontend-Route wechselt nach Save/Deploy auf die neu erzeugte Definition (`/definition/{metaDefinitionId}/{definitionGuid}`), damit URL und angezeigter Stand nicht auseinanderlaufen
- ungültige Modellrouten zeigen eine sichtbare Inline-Fehlermeldung mit Retry-Button statt eines fatalen Blazor-Fehlers
- Viewer- und Modeler-Instanzen werden beim Verlassen der Seite best-effort zerstört, damit keine veralteten Diagrammobjekte im Browser hängen bleiben

### Empfohlene manuelle Checks

1. `/models` öffnen und einen Workflow laden
2. prüfen, dass die Liste pro deploytem Workflow explizite `Open latest`-, `Open deployed`- und `Start instance`-Aktionen zeigt
3. eine Suche eingeben und prüfen, dass ein expliziter `Clear search`-Pfad sichtbar ist
4. prüfen, dass Diagramm **und** Properties Panel sichtbar sind
5. einmal `Save draft` auslösen und kontrollieren, dass die URL auf eine konkrete Definitions-GUID springt
6. testweise einen deployten Workflow direkt starten und die Instanzansicht prüfen
7. Dashboard-Übersicht öffnen und prüfen, dass die Summary-Cards direkt auf Workflows, aktive Instanzen, Fehlerinstanzen und Formulare verzweigen
8. testweise eine ungültige Modellroute öffnen und die Inline-Fehlermeldung verifizieren

## Hinweise zur lokalen Datenbasis

Die dateibasierte Persistenz landet standardmäßig unterhalb der Build-Ausgabe von `FilesystemStorageSystem`.

Für reproduzierbare Playwright-Läufe setzt die Testkonfiguration automatisch ein eigenes `FLOWZER_STORAGE_ROOT` für die verwalteten Webserver. Der Pfad wird über `PLAYWRIGHT_MANAGED_STORAGE_ROOT` gesteuert und aus Sicherheitsgründen nur unterhalb von `tests/ui-smoke/.tmp` gelöscht bzw. neu aufgebaut. Wenn Web-API und Frontend manuell gestartet werden, kann dasselbe Verhalten lokal explizit aktiviert werden:

```bash
FLOWZER_STORAGE_ROOT="$(pwd)/tests/ui-smoke/.tmp/manual-storage" \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/WebApiEngine/WebApiEngine.csproj \
--configuration Release \
--no-launch-profile \
--urls http://localhost:5182
```
