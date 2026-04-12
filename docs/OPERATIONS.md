# Betriebs- und Deployment-Basis

**Stand:** 12. April 2026

Dieses Dokument beschreibt den derzeit realistischen Betriebsrahmen für `next`: lokale Starts, Health-Signale, Compose-Setup und sinnvolle Prüfpfade.

> Wichtig: Das ist **noch keine produktionsfertige Deployment-Story**. Ziel dieses Pakets ist ein reproduzierbarer, dokumentierter Start- und Prüfpfad für API und Frontend.

## Enthaltene Bausteine

- dokumentierte Health-Endpunkte der Web-API
- lokaler Startpfad per `dotnet run`
- lokaler Startpfad per Docker Compose
- runtime-nahe Release-Container für API + Frontend + Gateway
- kleine Shell-Skripte zum Starten, Stoppen und Prüfen des lokalen Stacks
- definierter Storage-Pfad für dateibasierte Persistenz

## Health-Signale

Die Web-API stellt aktuell folgende Endpunkte bereit:

- `GET /health` – Liveness
- `GET /health/ready` – Readiness inkl. Storage-Prüfung

Typische URLs lokal:

- [http://localhost:5182/health](http://localhost:5182/health)
- [http://localhost:5182/health/ready](http://localhost:5182/health/ready)

## Lokaler Start ohne Docker

### Web-API

```bash
ASPNETCORE_ENVIRONMENT=Development \
FLOWZER_STORAGE_ROOT="$(pwd)/.data/flowzer-storage" \
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

## Lokaler Start per Docker Compose

Für einen reproduzierbaren Entwicklungsstack liegt jetzt `compose.local.yml` im Repository-Root.

### Starten

```bash
./scripts/local/start-stack.sh
```

Das Start-Skript wartet, bis API und Frontend ihren Health-Status erreicht haben.

Alternativ direkt:

```bash
docker compose -f compose.local.yml up -d --wait api frontend
```

### Prüfen

```bash
./scripts/local/check-stack.sh
```

### Stoppen

```bash
./scripts/local/stop-stack.sh
```

## Runtime-nahe Containerbasis

Für lokale Release-Checks liegt zusätzlich `compose.runtime.yml` mit echten Runtime-Images und vorgeschaltetem Gateway im Repository-Root.

### Starten

```bash
./scripts/runtime/start-runtime-stack.sh
```

Das Skript baut API- und Frontend-Images, startet anschließend den Gateway-Stack und wartet auf grüne Healthchecks.

### Prüfen

```bash
./scripts/runtime/check-runtime-stack.sh
```

Typische URLs:

- [http://localhost:5288](http://localhost:5288)
- [http://localhost:5288/health](http://localhost:5288/health)
- [http://localhost:5288/health/ready](http://localhost:5288/health/ready)

Bei Portkonflikten kann der Host-Port über `FLOWZER_RUNTIME_PORT` überschrieben werden.

### Stoppen

```bash
./scripts/runtime/stop-runtime-stack.sh
```

## Storage- und Dateipfade

Der Compose- und Local-Run-Pfad nutzt bewusst denselben Storage-Ort:

```text
.data/flowzer-storage
```

Dadurch bleiben Definitionen, Instanzen, Subscriptions und Formulare lokal reproduzierbar an einer bekannten Stelle liegen.

Der runtime-nahe Stack nutzt bewusst einen separaten Pfad:

```text
.data/runtime-storage
```

Damit bleiben lokale Dev-Daten und runtime-nahe Containerdaten getrennt.

## Logs und Diagnose

### Container-Logs

```bash
docker compose -f compose.local.yml logs -f api
docker compose -f compose.local.yml logs -f frontend

docker compose -f compose.runtime.yml logs -f api
docker compose -f compose.runtime.yml logs -f frontend
docker compose -f compose.runtime.yml logs -f gateway
```

### Lokale UI-Smokes gegen laufenden Stack

Wenn API und Frontend bereits laufen, können die Playwright-Smokes gezielt gegen den bestehenden Stack ausgeführt werden:

```bash
PLAYWRIGHT_SKIP_WEBSERVERS=1 \
FLOWZER_API_URL=http://localhost:5182 \
FLOWZER_FRONTEND_URL=http://localhost:5269 \
npm --prefix tests/ui-smoke run test
```

Der `npm test`-Pfad enthält zusätzlich den Prozesswächter für verwaiste `ms-playwright`-/`chrome-headless-shell`-Prozesse.

Dasselbe funktioniert auch gegen den runtime-nahen Gateway-Stack:

```bash
PLAYWRIGHT_SKIP_WEBSERVERS=1 \
FLOWZER_API_URL=http://localhost:5288 \
FLOWZER_FRONTEND_URL=http://localhost:5288 \
npm --prefix tests/ui-smoke run test
```

## Bewusst noch offen

Folgende Betriebsaspekte sind mit diesem Paket **noch nicht abgeschlossen**:

- strukturierte Produktions-Logformate über die Standard-Konsole hinaus
- Metrics/Tracing
- produktionsnahe Reverse-Proxy- oder TLS-Story
- Secret-/Configuration-Story jenseits lokaler Entwicklungswerte

## Sinnvolle nächste Ausbauschritte

1. Reverse-Proxy-/Gateway-Konfiguration für echte Zielumgebungen weiter härten
2. Metrics/Tracing einführen
3. Recovery-/Backup-Hinweise für dateibasierte Persistenz dokumentieren
4. Secret-/Konfigurationsstory für Nicht-Entwicklungsumgebungen schärfen
