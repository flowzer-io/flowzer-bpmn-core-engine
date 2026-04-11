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

Die Smoke-Tests liegen unter `tests/ui-smoke` und prüfen aktuell die Kernrouten:

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
- kein sichtbarer Blazor-Fatalfehler
- keine fehlgeschlagenen Browser-Requests

## Aktuelle Frontend-Konventionen für Kernseiten

- Listen- und Startseiten zeigen bei Ladefehlern eine sichtbare Inline-Fehlermeldung statt still abzustürzen.
- Leere Listen rendern einen expliziten Empty State.
- Instanzfilter werden ausschließlich über gültige Frontend-Routen (`all`, `active`, `done`, `error`) adressiert.
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

## Hinweise zur lokalen Datenbasis

Die dateibasierte Persistenz landet unterhalb der Build-Ausgabe von `FilesystemStorageSystem`. Für saubere lokale Testläufe kann es sinnvoll sein, diese Ablage vorab zu leeren, wenn alte Modelldaten oder Formulare stören.
