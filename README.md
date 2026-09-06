# Flowzer BPMN Core Engine

Eine BPMN-Ausführungsengine in C#/.NET mit Parser, Laufzeitmodell, Web-API, React-Oberfläche und ersten Beispielprozessen.

> **Stand: 6. September 2026**
> Das Repository ist arbeitsfähig, die Kernpfade sind getestet, die API ist gegen einen OIDC-Identity-Provider abgesichert und kennt ein Rollenmodell. Die Oberfläche ist die **React-Konsole** in `src/FlowzerConsole`; die frühere Blazor-Oberfläche wurde entfernt. Die Bestandsaufnahme mit Empfehlungen steht in [docs/REVIEW-2026-09.md](docs/REVIEW-2026-09.md).

## Warum das Projekt spannend ist

Das Projekt bringt bereits einige starke Bausteine mit:

- BPMN-Modellklassen in `/src/FlowzerBPMN`
- Ausführungslogik in `/src/core-engine`
- API-Schicht in `/src/WebApiEngine`
- Oberfläche in `/src/FlowzerConsole` (React, TypeScript, bpmn-js, Form.io)
- Testprozesse und Unit-Tests in `/src/core-engine-tests`
- Beispielcode in `/examples`

Kurz gesagt: **Die Richtung stimmt.** Die Engine ist nicht „tot“, aber sie braucht gerade mehr Wartbarkeit und Fokus als neue Features.

## Realistischer Projektstatus

Die bisherige Dokumentation klang teilweise deutlich reifer als der aktuelle Stand der Codebasis. Realistischer formuliert:

- Es gibt bereits eine **brauchbare Kernarchitektur**.
- Es gibt **fachlich wertvolle Tests, BPMN-Beispiele und eine grüne CI-Basis auf `next`**.
- Zentrale Produktpfade wie Demo, UI-Smokes, API-Fehlerverträge und ein erster Timer-Kernpfad sind inzwischen vorhanden.
- Timer-Subscriptions werden jetzt auch in Storage/Web-API persistiert, über einen kleinen Scheduler-Polling-Pfad verarbeitet und können wiederkehrende Start-Timer inklusive Restwiederholungen abbilden.
- Geschützte API-Pfade verlangen inzwischen einen **aufgelösten Benutzerkontext**, statt stillschweigend über einen System-Fallback weiterzulaufen.
- Für lokale Entwicklung und UI-Smokes setzt das Frontend im **Development-Modus** jetzt automatisch einen technischen Benutzerheader, damit die gehärteten API-Pfade lokal weiter reproduzierbar testbar bleiben.
- Zusätzlich gibt es jetzt einen kleinen **Operations-/Diagnose-Endpunkt** sowie lokale Metrics-/Tracing-Namen, damit Scheduler- und Storage-Zustand nicht nur über reine Healthchecks sichtbar werden.
- Die Web-API kann diese Signale jetzt optional auch über **OpenTelemetry** an Console- oder OTLP-Exporter weitergeben, ohne den Default-Pfad in Dev/CI zu verschärfen.
- Es gibt aber weiterhin **offene Restlücken** bei weitergehender Timer-/Boundary-Recovery, Auth/Identity und Betriebsreife.
- Das Projekt ist **klar revivierbar und aktiv weiterentwickelbar**, wenn die nächsten Schritte weiter fokussiert bleiben.

Mehr Details: [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md)

## Repository-Struktur

```text
.
├── Model/                        # Geteilte DTOs / Modelklassen
├── examples/                     # Kleine Nutzungsbeispiele
├── src/
│   ├── FlowzerBPMN/              # BPMN-Domänenmodell
│   ├── core-engine/              # Engine / Ausführungslogik
│   ├── core-engine-tests/        # Unit-Tests + BPMN-Testdateien
│   ├── WebApiEngine/             # ASP.NET Core API
│   ├── WebApiEngine.Shared/      # API-DTOs
│   ├── FlowzerConsole/           # React-Oberfläche (Vite, TanStack, bpmn-js, Form.io)
│   ├── FilesystemStorageSystem/  # Dateibasierte Persistenz
│   ├── StorageSystemShared/      # Storage-Abstraktionen
│   └── Flowzer.Shared/           # Gemeinsame Hilfslogik
├── DEVELOPMENT-GUIDELINES.md     # Entwicklungsrichtlinien
├── CONTRIBUTING.md               # GitHub- und Beitragsleitfaden
├── AGENTS.md                     # Hinweise für KI-/Codex-Agenten
└── core-engine.sln               # Haupt-Solution
```

## Schnellstart

### Voraussetzungen

- **.NET 10 SDK im Feature-Band 10.0.1xx** (siehe `global.json`, z. B. 10.0.100 vom offiziellen Installer). Ein SDK aus einem anderen Band, etwa 10.0.400 aus Homebrew, wird abgelehnt. Liegt ein solches SDK vorn im `PATH`, hilft `export PATH=/usr/local/share/dotnet:$PATH`.
- **Node.js 22.15+** für die Konsole und die UI-Smokes
- Git

### .NET-Projekte

```bash
dotnet restore core-engine.sln
python3 scripts/ci/check_test_purpose_comments.py
dotnet build core-engine.sln
dotnet test src/core-engine-tests/core-engine-tests.csproj
dotnet test src/WebApiEngine.Tests/WebApiEngine.Tests.csproj
```

### Oberfläche

```bash
npm --prefix src/FlowzerConsole ci
npm --prefix src/FlowzerConsole run dev     # spricht über /api gegen http://localhost:5182
npm --prefix src/FlowzerConsole run test
npm --prefix src/FlowzerConsole run build
```

Ohne konfigurierten Identity Provider meldet die Konsole im Entwicklungsmodus einen
technischen Benutzer an, damit sich alle Seiten lokal ohne Anmeldung prüfen lassen.

## Bekannte Stolpersteine

Diese Punkte sollte man kennen, bevor man loslegt:

1. **Kernpfade sind stabil, aber noch nicht vollständig aufgeräumt**
   Build, CI sowie Kern-, Web-API-, Konsolen- und UI-Smoke-Pfade laufen auf `next` reproduzierbar grün. Die wichtigsten offenen Lücken liegen inzwischen eher in fachlichen Runtime- und Betriebsfragen als in der nackten Build-Stabilität.

2. **Expression-/V8-Thema nicht abgeschlossen**
   Test- und CI-Umgebungen laufen inzwischen auch ohne native V8-Abhängigkeit stabiler. Die vollständige FEEL-/V8-Strategie der Engine ist fachlich aber weiterhin ein eigener Architekturstrang.

3. **Timer-, Fehler- und Abbruchpfade sind verbessert, aber noch nicht vollständig**
   Der Engine-Kern kann fällige Timer jetzt weiterführen, Boundary-Timer im bestehenden Subscription-Pfad verarbeiten, wiederkehrende Start-Timer überfälligkeitstolerant nachziehen und rohe `NotImplementedException`-Abbrüche in mehreren Pfaden vermeiden. Offen bleiben weiterhin speziellere Recovery-Fragen, vollständige Fehler-/Eskalationssemantik und echte Kompensation.

4. **Betrieb und Auth sind noch nicht am Ziel**
   Lokale Compose- und Runtime-Container sind vorhanden, geschützte API-Pfade verlangen jetzt zwar einen aufgelösten Benutzerkontext und die Web-API liefert erste Operations-Diagnose-Informationen inklusive optionaler OpenTelemetry-Exporter-Konfiguration, aber Themen wie echte Authentifizierung, Rollenmodell, Secrets, TLS und Recovery-Automatisierung bleiben weiterhin Folgepakete.

## Authentifizierung und CORS

Die Web-API läuft standardmäßig ohne Authentifizierung (`Authentication:Scheme=None`); im Development-Modus identifiziert der Header `X-Flowzer-UserId` den Benutzer. Für echte Umgebungen prüft die API OIDC-Tokens:

```json
"Authentication": {
  "Scheme": "JwtBearer",
  "JwtBearer": {
    "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
    "Audience": "<client-id-der-api>"
  }
},
"Cors": {
  "AllowedOrigins": ["https://flowzer.example.com"]
}
```

Die Konsole meldet sich über die zur Laufzeit geladene `config.json` (Authority, Client-Id, Audience, Scopes) beim selben Identity Provider an und sendet das Access-Token als Bearer an die API. Die Werte kommen im Container aus Umgebungsvariablen, siehe `deploy/console/entrypoint.sh`.

Details stehen in [docs/OPERATIONS.md](docs/OPERATIONS.md#authentifizierung-jwt-bearer--oidc), der komplette Pilot-Ablauf (Identity Provider, Compose, Backup, Fehlerbilder) in [docs/RUNBOOK-PILOT.md](docs/RUNBOOK-PILOT.md).

## Ablage

Standardmäßig persistiert die Web-API als JSON-Dateien unter `FLOWZER_STORAGE_ROOT`. Für den Betrieb steht eine PostgreSQL-Ablage mit echten Transaktionen bereit (`Storage:Provider=PostgreSql`, Migrationen per `dotnet WebApiEngine.dll --migrate`). Details in [docs/OPERATIONS.md](docs/OPERATIONS.md#ablage-dateisystem-oder-postgresql).

## Release und Deployment

Der Workflow `release.yml` baut bei jedem Push auf `main` die Images `ghcr.io/flowzer-io/flowzer-api` und `ghcr.io/flowzer-io/flowzer-console`, pinnt den Tag in Coolify und löst dort das Deployment aus (`compose.coolify.yaml`). Deploy-Zugangsdaten liegen im GitHub-Environment `maassit-production`.

## Dokumentation

- [docs/REVIEW-2026-09.md](docs/REVIEW-2026-09.md) – Review September 2026: Stand, Sofortmaßnahmen, offene Probleme, nächste Schritte
- [docs/RUNBOOK-PILOT.md](docs/RUNBOOK-PILOT.md) – Pilotbetrieb: Identity Provider, Compose-Stack, Backup, Fehlerbilder
- [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md) – ehrliche Bestandsaufnahme
- [docs/ROADMAP.md](docs/ROADMAP.md) – Vorschlag für die nächsten Schritte
- [docs/CODEBASE-AUDIT-2026-04.md](docs/CODEBASE-AUDIT-2026-04.md) – Audit-Feststellungen und Folgepakete nach der Revitalisierung
- [docs/ICORE.md](docs/ICORE.md) – dokumentierter Kernvertrag und minimaler Integrationspfad
- [docs/DEMO.md](docs/DEMO.md) – Console-Demo, Startbefehl und erwartete Ausgabe
- [docs/GLIEDERUNG-TEILMENGE.md](docs/GLIEDERUNG-TEILMENGE.md) – Gliederungsansicht neben dem Diagramm: abgedeckte BPMN-Teilmenge und wie Verluste verhindert werden
- [src/FlowzerConsole/README.md](src/FlowzerConsole/README.md) – Oberfläche: Konfiguration, lokale Starts, Aufbau
- [CONTRIBUTING.md](CONTRIBUTING.md) – Leitfaden für Beiträge über GitHub
- [AGENTS.md](AGENTS.md) – Hinweise für KI, Codex und Copilot
- [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md) – Entwicklungsprinzipien
- [.github/copilot-instructions.md](.github/copilot-instructions.md) – GitHub-Copilot-spezifische Hinweise

## Empfohlene nächste Schritte

Die sinnvolle Reihenfolge ist aktuell:

1. **Pilot starten** nach [docs/RUNBOOK-PILOT.md](docs/RUNBOOK-PILOT.md): Identity Provider registrieren, `.env` füllen, Stack hinter dem Reverse Proxy betreiben
2. **Rollen und Zuweisungen** für Aufgaben, Definitionen und Diagnose
3. **PostgreSQL-Persistenz** hinter `IStorageSystem`
4. **Fehler-, Eskalations- und Kompensationssemantik** in der Engine

Details dazu stehen in [docs/ROADMAP.md](docs/ROADMAP.md) und [docs/REVIEW-2026-09.md](docs/REVIEW-2026-09.md).

## Minimaler `ICore`-Nutzungsweg

Für Integrationen ohne WebAPI oder Storage liegt jetzt ein bewusst kleiner Kernvertrag vor:

1. BPMN-Datei laden
2. initiale Start-Subscriptions lesen
3. Event über `BpmnNodeId` verarbeiten
4. aktive Interaktionen aus dem Ergebnis ableiten

Ein vollständiger Ablauf ist dokumentiert in [docs/ICORE.md](docs/ICORE.md).  
Eine konkrete Beispiel-Datei liegt unter [`examples/SimpleEngineExample.cs`](examples/SimpleEngineExample.cs).

## Console-Demo starten

Die Demo-Anwendung lässt sich lokal mit einem Befehl starten:

```bash
dotnet run --project src/FlowzerDemoConsole/FlowzerDemoConsole.csproj
```

Eine Schritt-für-Schritt-Erklärung und die erwartete Ausgabe stehen in [docs/DEMO.md](docs/DEMO.md).

## Oberfläche per UI-Smoke testen

Die Playwright-Smokes starten API und Konsole selbst und prüfen die Kernrouten im Browser.

```bash
dotnet build core-engine.sln --configuration Release
npm --prefix src/FlowzerConsole ci
npm --prefix tests/ui-smoke ci
npm --prefix tests/ui-smoke run install:browsers
npm --prefix tests/ui-smoke run test
```

Zusätzlich vergleicht `tests/ui-smoke/check-gateway-routes.sh` die Weiterleitungsliste des
Konsolen-Gateways mit den tatsächlichen API-Routen — fehlt dort eine Route, beantwortet die
Konsole sie mit ihrer eigenen Startseite und der Aufruf bekommt 200 statt der erwarteten Antwort.

## Lokaler Stack per Docker Compose

Für einen reproduzierbaren API-Start gibt es zusätzlich einen kleinen lokalen Compose-Stack (die Oberfläche läuft daneben mit `npm run dev`):

```bash
./scripts/local/start-stack.sh
./scripts/local/check-stack.sh
./scripts/local/stop-stack.sh
```

Weitere Betriebs- und Diagnosehinweise stehen in [docs/OPERATIONS.md](docs/OPERATIONS.md).

### Optionale OpenTelemetry-Exporter

Für produktionsnahe Umgebungen kann die Web-API die vorhandenen Signale jetzt optional an Console- oder OTLP-Exporter weiterreichen.

Beispiel:

```bash
ASPNETCORE_ENVIRONMENT=Development \
FLOWZER_STORAGE_ROOT="$(pwd)/.data/flowzer-storage" \
Observability__Enabled=true \
Observability__UseConsoleExporter=true \
Observability__OtlpEndpoint=http://localhost:4318 \
Observability__OtlpProtocol=http/protobuf \
dotnet run --project src/WebApiEngine/WebApiEngine.csproj --configuration Release
```

Welche Exporter aktiv sind, zeigt zusätzlich `GET /operations/diagnostics`.

## Timer-Scheduler im Web-API-Host

Die Web-API enthält jetzt zusätzlich einen kleinen Hintergrund-Poller für fällige Timer-Subscriptions.

Persistierte wiederkehrende Start-Timer werden dabei inklusive verbleibender Wiederholungen nachgezogen und nach einem Neustart wieder sauber auf den nächsten Fälligkeitszeitpunkt vorgeschoben.

Relevante Konfiguration:

```json
"TimerScheduler": {
  "Enabled": true,
  "PollIntervalSeconds": 5
}
```

Zusätzlich sichtbar sind Timer-Subscriptions jetzt über:

- `GET /timer`
- `GET /instance/{instanceId}/subscription/timers`

Die API-DTOs für Timer enthalten dabei jetzt auch `RemainingOccurrences`, wenn ein Start-Timer über ein BPMN-`timeCycle` mit begrenzter Wiederholung definiert wurde.

## Runtime-Container für lokale Release-Checks

Zusätzlich zur Dev-Compose-Variante gibt es jetzt auch eine runtime-nahe Containerbasis:

```bash
./scripts/runtime/start-runtime-stack.sh
./scripts/runtime/check-runtime-stack.sh
./scripts/runtime/stop-runtime-stack.sh
```

Der Runtime-Gateway-Stack ist anschließend standardmäßig unter [http://localhost:5288](http://localhost:5288) erreichbar.

## Beispiele

Ein kleines Nutzungsbeispiel der Engine-Bibliothek liegt in
[`/examples/SimpleEngineExample.cs`](examples/SimpleEngineExample.cs).

Ein vollstaendiger Prozess ueber die API — Formulare, parallele Zweige, menschliche
Entscheidungen und Service-Tasks — liegt in
[`/examples/urlaubsantrag/`](examples/urlaubsantrag/README.md). Er laesst sich mit einem
Befehl einspielen und mit dem mitgelieferten Demo-Worker durchspielen.

## Lizenz

Siehe [LICENSE](LICENSE).
