# Flowzer BPMN Core Engine

Eine BPMN-Ausführungsengine in C#/.NET mit Parser, Laufzeitmodell, Web-API, Frontend und ersten Beispielprozessen.

> **Stand: 12. April 2026**
> Das Repository ist auf `next` wieder klar arbeitsfähig und deutlich weiter als noch in der reinen Rettungsphase. Es bleibt aber eher ein **produktnaher Prototyp mit belastbarer Basis** als ein fertig gehärtetes Framework.

## Warum das Projekt spannend ist

Das Projekt bringt bereits einige starke Bausteine mit:

- BPMN-Modellklassen in `/src/FlowzerBPMN`
- Ausführungslogik in `/src/core-engine`
- API-Schicht in `/src/WebApiEngine`
- Frontend in `/src/FlowzerFrontend`
- BPMN-Modeler-/Properties-Panel-Integration in `/bpmn.io`
- Testprozesse und Unit-Tests in `/src/core-engine-tests`
- Beispielcode in `/examples`

Kurz gesagt: **Die Richtung stimmt.** Die Engine ist nicht „tot“, aber sie braucht gerade mehr Wartbarkeit und Fokus als neue Features.

## Realistischer Projektstatus

Die bisherige Dokumentation klang teilweise deutlich reifer als der aktuelle Stand der Codebasis. Realistischer formuliert:

- Es gibt bereits eine **brauchbare Kernarchitektur**.
- Es gibt **fachlich wertvolle Tests, BPMN-Beispiele und eine grüne CI-Basis auf `next`**.
- Zentrale Produktpfade wie Demo, UI-Smokes, API-Fehlerverträge und ein erster Timer-Kernpfad sind inzwischen vorhanden.
- Timer-Subscriptions werden jetzt auch in Storage/Web-API persistiert, über einen kleinen Scheduler-Polling-Pfad verarbeitet und können wiederkehrende Start-Timer inklusive Restwiederholungen abbilden.
- Es gibt aber weiterhin **offene Restlücken** bei weitergehender Timer-/Boundary-Recovery, Auth/Identity und Betriebsreife.
- Das Projekt ist **klar revivierbar und aktiv weiterentwickelbar**, wenn die nächsten Schritte weiter fokussiert bleiben.

Mehr Details: [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md)

## Repository-Struktur

```text
.
├── Model/                        # Geteilte DTOs / Modelklassen
├── bpmn.io/                      # JS-basierter BPMN-Editor / Properties Panel Beispiel
├── examples/                     # Kleine Nutzungsbeispiele
├── src/
│   ├── FlowzerBPMN/              # BPMN-Domänenmodell
│   ├── core-engine/              # Engine / Ausführungslogik
│   ├── core-engine-tests/        # Unit-Tests + BPMN-Testdateien
│   ├── WebApiEngine/             # ASP.NET Core API
│   ├── WebApiEngine.Shared/      # API-DTOs
│   ├── FlowzerFrontend/          # Blazor-Frontend
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

- **.NET 8 SDK** empfohlen
- **Node.js 20+** für `/bpmn.io`
- Git

### .NET-Projekte

```bash
dotnet restore core-engine.sln
dotnet build core-engine.sln
dotnet test src/core-engine-tests/core-engine-tests.csproj
dotnet test src/WebApiEngine.Tests/WebApiEngine.Tests.csproj
dotnet test src/FlowzerFrontend.Tests/FlowzerFrontend.Tests.csproj
```

### BPMN-Editor / bpmn.io

```bash
cd bpmn.io
npm install
npm run build
```

## Bekannte Stolpersteine

Diese Punkte sollte man kennen, bevor man loslegt:

1. **Kernpfade sind stabil, aber noch nicht vollständig aufgeräumt**
   Build, CI sowie Kern-, Web-API-, Frontend- und UI-Smoke-Pfade laufen auf `next` reproduzierbar grün. Die wichtigsten offenen Lücken liegen inzwischen eher in fachlichen Runtime- und Betriebsfragen als in der nackten Build-Stabilität.

2. **Expression-/V8-Thema nicht abgeschlossen**
   Test- und CI-Umgebungen laufen inzwischen auch ohne native V8-Abhängigkeit stabiler. Die vollständige FEEL-/V8-Strategie der Engine ist fachlich aber weiterhin ein eigener Architekturstrang.

3. **Timer-, Fehler- und Abbruchpfade sind verbessert, aber noch nicht vollständig**
   Der Engine-Kern kann fällige Timer jetzt weiterführen, Boundary-Timer im bestehenden Subscription-Pfad verarbeiten, wiederkehrende Start-Timer überfälligkeitstolerant nachziehen und rohe `NotImplementedException`-Abbrüche in mehreren Pfaden vermeiden. Offen bleiben weiterhin speziellere Recovery-Fragen, vollständige Fehler-/Eskalationssemantik und echte Kompensation.

4. **Betrieb und Auth sind noch nicht am Ziel**
   Lokale Compose- und Runtime-Container sind vorhanden, aber Themen wie Telemetrie, Secrets, TLS, Recovery und eine belastbare Identity-/Auth-Story sind weiterhin Folgepakete.

## Dokumentation

- [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md) – ehrliche Bestandsaufnahme
- [docs/ROADMAP.md](docs/ROADMAP.md) – Vorschlag für die nächsten Schritte
- [docs/ICORE.md](docs/ICORE.md) – dokumentierter Kernvertrag und minimaler Integrationspfad
- [docs/DEMO.md](docs/DEMO.md) – Console-Demo, Startbefehl und erwartete Ausgabe
- [docs/FRONTEND.md](docs/FRONTEND.md) – Frontend-Konfiguration, lokale Starts und UI-Smoke-Tests
- [CONTRIBUTING.md](CONTRIBUTING.md) – Leitfaden für Beiträge über GitHub
- [AGENTS.md](AGENTS.md) – Hinweise für KI, Codex und Copilot
- [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md) – Entwicklungsprinzipien
- [.github/copilot-instructions.md](.github/copilot-instructions.md) – GitHub-Copilot-spezifische Hinweise

## Empfohlene nächste Schritte

Die sinnvolle Reihenfolge ist aktuell:

1. **Auth-/Identity- und weitergehende Fehlersemantik produktionsnah härten**
2. **Betriebsbasis mit Telemetrie, Secrets, TLS und Recovery vertiefen**
3. **Timer-Recovery nur noch in speziellen Boundary-/Spezialfällen nachziehen**
4. **Status-, Architektur- und Contributor-Dokumentation laufend nachziehen**

Details dazu stehen in [docs/ROADMAP.md](docs/ROADMAP.md).

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

## Frontend lokal und per UI-Smoke testen

Für das Frontend gibt es jetzt einen dokumentierten API-Konfigurationspfad und erste Playwright-Smoke-Tests.

Schnellstart:

```bash
dotnet build core-engine.sln --configuration Release
npm --prefix tests/ui-smoke ci
npm --prefix tests/ui-smoke run install:browsers
npm --prefix tests/ui-smoke run test
```

Details zu API-Basisadresse, lokalen Startbefehlen und den getesteten Routen stehen in [docs/FRONTEND.md](docs/FRONTEND.md).

## Lokaler Stack per Docker Compose

Für einen reproduzierbaren API-/Frontend-Start gibt es jetzt zusätzlich einen kleinen lokalen Compose-Stack:

```bash
./scripts/local/start-stack.sh
./scripts/local/check-stack.sh
./scripts/local/stop-stack.sh
```

Weitere Betriebs- und Diagnosehinweise stehen in [docs/OPERATIONS.md](docs/OPERATIONS.md).

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

## Beispiel

Ein kleines Nutzungsbeispiel liegt in [`/examples/SimpleEngineExample.cs`](examples/SimpleEngineExample.cs).

## Lizenz

Siehe [LICENSE](LICENSE).
