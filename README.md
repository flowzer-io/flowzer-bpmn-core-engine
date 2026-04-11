# Flowzer BPMN Core Engine

Eine BPMN-Ausführungsengine in C#/.NET mit Parser, Laufzeitmodell, Web-API, Frontend und ersten Beispielprozessen.

> **Stand: 11. April 2026**
> Das Repository hat eine gute fachliche Basis, ist aktuell aber eher ein **starker Prototyp** als ein produktionsreifes Framework. Wer das Projekt weiterführen will, sollte zuerst Stabilisierung, Build/CI und die offene PR-/Issue-Lage bereinigen.

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
- Es gibt **fachlich wertvolle Tests und BPMN-Beispiele**.
- Es gibt **offene technische Schulden** in Build, Tooling und Repository-Hygiene.
- Mehrere Bereiche sind **begonnen, aber nicht sauber abgeschlossen**.
- Das Projekt ist **revivierbar**, wenn man die nächsten Schritte konsequent priorisiert.

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
   Build, CI sowie die Kern- und WebAPI-Tests laufen auf `next` reproduzierbar grün. Offen sind jedoch weiterhin mehrere Compiler-, Nullability- und Frontend-Warnungen.

2. **Expression-/V8-Thema nicht abgeschlossen**
   Test- und CI-Umgebungen laufen inzwischen auch ohne native V8-Abhängigkeit stabiler. Die vollständige FEEL-/V8-Strategie der Engine ist fachlich aber weiterhin ein eigener Architekturstrang.

3. **Sicherheits- und Dependency-Lage weiter beobachten**
   Die risikoarmen Updates wurden auf `next` bereits übernommen. Der Security-Status des Default-Branches und weitere Abhängigkeitsentscheidungen sollten dennoch aktiv beobachtet werden.

4. **Produktpfade bleiben im Ausbau**
   `ICore` und die Console-Demo sind jetzt vorhanden. API-Verträge, UI-Smoke-Tests und weitere Contributor-Pfade werden in den nächsten Paketen weiter geschärft.

## Dokumentation

- [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md) – ehrliche Bestandsaufnahme
- [docs/ROADMAP.md](docs/ROADMAP.md) – Vorschlag für die nächsten Schritte
- [docs/ICORE.md](docs/ICORE.md) – dokumentierter Kernvertrag und minimaler Integrationspfad
- [docs/DEMO.md](docs/DEMO.md) – Console-Demo, Startbefehl und erwartete Ausgabe
- [CONTRIBUTING.md](CONTRIBUTING.md) – Leitfaden für Beiträge über GitHub
- [AGENTS.md](AGENTS.md) – Hinweise für KI, Codex und Copilot
- [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md) – Entwicklungsprinzipien
- [.github/copilot-instructions.md](.github/copilot-instructions.md) – GitHub-Copilot-spezifische Hinweise

## Empfohlene nächste Schritte

Die sinnvolle Reihenfolge ist aktuell:

1. **API- und DTO-Grenzen weiter schärfen**
2. **Console-Demo und Contributor Experience weiter ausbauen**
3. **Frontend-Smoke- und E2E-Pfade absichern**
4. **Warnungen und Nullability-Themen schrittweise abbauen**

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

## Beispiel

Ein kleines Nutzungsbeispiel liegt in [`/examples/SimpleEngineExample.cs`](examples/SimpleEngineExample.cs).

## Lizenz

Siehe [LICENSE](LICENSE).
