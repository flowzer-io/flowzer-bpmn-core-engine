# Frontend und UI-Smoke-Tests

## Ziel

Das Blazor-Frontend soll reproduzierbar gegen die Web-API laufen – lokal, in CI und später auch hinter einem Reverse Proxy.

## API-Konfiguration

Die Frontend-App liest ihre API-Basisadresse aus `FlowzerApi:BaseUrl`:

- Standard: `src/FlowzerFrontend/wwwroot/appsettings.json`
- lokale Entwicklung: `src/FlowzerFrontend/wwwroot/appsettings.Development.json`

Aktuell gilt damit bewusst:

- **Produktion / Same-Origin:** Standardwert `/`
- **Lokale Entwicklung:** `http://localhost:5182/`

Damit lässt sich das Frontend lokal direkt gegen die Web-API starten, ohne dass im Code harte URLs verdrahtet bleiben.

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
  --urls http://localhost:5269
```

Danach ist das Frontend unter [http://localhost:5269](http://localhost:5269) erreichbar.

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
- kein sichtbarer Blazor-Fatalfehler
- keine fehlgeschlagenen Browser-Requests

## Aktuelle Frontend-Konventionen für Kernseiten

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

Der `npm test`-Pfad verwendet zusätzlich einen kleinen Prozesswächter. Dieser erkennt alte verwaiste `ms-playwright`-/`chrome-headless-shell`-Prozesse aus früheren Läufen und räumt nach dem Test auch neu entstandene Browser-Reste wieder weg. Damit bleiben keine CPU-intensiven Headless-Prozesse mehr unbemerkt liegen.

Bei Bedarf kann das Verhalten angepasst werden:

- `PLAYWRIGHT_SKIP_PROCESS_GUARD=1` deaktiviert den Wächter
- `PLAYWRIGHT_PROCESS_STALE_THRESHOLD_SECONDS=<sekunden>` steuert, ab wann alte Browser-Prozesse als verwaist gelten

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
