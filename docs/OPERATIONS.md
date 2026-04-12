# Betriebs- und Deployment-Basis

**Stand:** 12. April 2026

Dieses Dokument beschreibt den derzeit realistischen Betriebsrahmen für `next`: lokale Starts, Health-Signale, Compose-Setup und sinnvolle Prüfpfade.

> Wichtig: Das ist **noch keine produktionsfertige Deployment-Story**. Ziel dieses Pakets ist ein reproduzierbarer, dokumentierter Start- und Prüfpfad für API und Frontend.

## Enthaltene Bausteine

- dokumentierte Health-Endpunkte der Web-API
- lokaler Startpfad per `dotnet run`
- lokaler Startpfad per Docker Compose
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

## Storage- und Dateipfade

Der Compose- und Local-Run-Pfad nutzt bewusst denselben Storage-Ort:

```text
.data/flowzer-storage
```

Dadurch bleiben Definitionen, Instanzen, Subscriptions und Formulare lokal reproduzierbar an einer bekannten Stelle liegen.

## Logs und Diagnose

### Container-Logs

```bash
docker compose -f compose.local.yml logs -f api
docker compose -f compose.local.yml logs -f frontend
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

## Bewusst noch offen

Folgende Betriebsaspekte sind mit diesem Paket **noch nicht abgeschlossen**:

- strukturierte Produktions-Logformate über die Standard-Konsole hinaus
- Metrics/Tracing
- produktionsnahe Reverse-Proxy- oder TLS-Story
- Image-Builds für echte Releases statt lokaler SDK-Container
- Secret-/Configuration-Story jenseits lokaler Entwicklungswerte

## Sinnvolle nächste Ausbauschritte

1. dedizierte Release-Dockerfiles ergänzen
2. Compose um persistente Volumes und optionalen Reverse Proxy erweitern
3. Metrics/Tracing einführen
4. Recovery-/Backup-Hinweise für dateibasierte Persistenz dokumentieren
