# Flowzer BPMN Core Engine

Eine BPMN-Ausführungsengine in C#/.NET mit Parser, Laufzeitmodell, Web-API, Frontend und ersten Beispielprozessen.

> **Stand: 10. April 2026**  
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

1. **Build mit aktuellem SDK nicht sauber stabilisiert**  
   Mit neueren .NET-SDKs kann der Build derzeit in `src/core-engine/InstanceEngine/InstanceEngine.cs` scheitern (Zeile mit `activeTokens.Reverse().Where(...)`).

2. **Verwaiste ProjectReference**  
   `src/WebApiEngine/WebApiEngine.csproj` referenziert `src/MemoryStorageSystem/MemoryStorageSystem.csproj`, die Datei liegt aktuell aber nicht im Repository vor.

3. **Keine CI-Workflows im Repository**  
   Es gibt derzeit keine GitHub-Actions-Pipeline, die Build/Test/Node-Checks automatisch absichert.

4. **Offene Security-Updates**  
   Es gibt offene Dependabot-PRs für `System.Text.Json`, `webpack-dev-server`/`on-headers`/`compression` und `AutoMapper`.

5. **Offene Draft-PR mit inhaltlicher Substanz**  
   PR #16 adressiert das V8-/Expression-Handling in Tests, ist aber nicht final bewertet oder abgeschlossen.

## Dokumentation

- [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md) – ehrliche Bestandsaufnahme
- [docs/ROADMAP.md](docs/ROADMAP.md) – Vorschlag für die nächsten Schritte
- [CONTRIBUTING.md](CONTRIBUTING.md) – Leitfaden für Beiträge über GitHub
- [AGENTS.md](AGENTS.md) – Hinweise für KI, Codex und Copilot
- [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md) – Entwicklungsprinzipien
- [.github/copilot-instructions.md](.github/copilot-instructions.md) – GitHub-Copilot-spezifische Hinweise

## Empfohlene nächste Schritte

Die sinnvolle Reihenfolge ist aktuell:

1. **Build-/Toolchain stabilisieren**
2. **CI einführen**
3. **Issue #10 und PR #16 sauber entscheiden**
4. **Security-PRs selektiv bewerten und mergen**
5. **ICore/API-Grenzen klarziehen**
6. **Demo und Contributor Experience verbessern**

Details dazu stehen in [docs/ROADMAP.md](docs/ROADMAP.md).

## Beispiel

Ein kleines Nutzungsbeispiel liegt in [`/examples/SimpleEngineExample.cs`](examples/SimpleEngineExample.cs).

## Lizenz

Siehe [LICENSE](LICENSE).
