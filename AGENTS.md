# AGENTS.md

Hinweise für KI-Assistenten, Codex, Copilot und andere agentische Werkzeuge.

## Repository in einem Satz

Dieses Repository enthält eine BPMN-Engine mit Parser, Laufzeit, API, Frontend und Editor-Experimenten – **fachlich interessant, aber aktuell noch in einer Stabilisierungsphase**.

## Lies zuerst

1. [README.md](README.md)
2. [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md)
3. [docs/ROADMAP.md](docs/ROADMAP.md)
4. [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md)
5. [.github/copilot-instructions.md](.github/copilot-instructions.md)
6. [.codex-instructions.md](.codex-instructions.md)

## Wichtige Bereiche

- `src/FlowzerBPMN/` – BPMN-Domänenmodell
- `src/core-engine/` – Prozessausführung, Handler, Expressions
- `src/core-engine-tests/` – Regressionstests und BPMN-Testdateien
- `src/WebApiEngine/` – REST-API
- `src/FlowzerFrontend/` – Blazor-Frontend
- `bpmn.io/` – JS-/Modeler-Experiment

## Arbeitsprinzipien

- **Nicht von vollständiger BPMN-2.0-Abdeckung ausgehen.** Erst im Code und in den Tests verifizieren.
- **Kleine, fokussierte Änderungen bevorzugen.**
- **Engine-Änderungen möglichst mit Tests absichern.**
- **Doku aktualisieren, wenn sich Architektur oder Setup ändern.**
- **Code auf Englisch, erklärende Kommentare/Dokumentation bevorzugt auf Deutsch.**

## Git-Write-Regel für dieses Repository

- Auf Arbeits- und Integrationszweigen dürfen agentische Werkzeuge in diesem Repository **ohne weitere Rückfrage committen und pushen**.
- Diese Freigabe gilt insbesondere für Branches wie `codex/*`, `next` und vergleichbare Nicht-Release-Zweige.
- **Nicht** darunter fallen direkte Git-Write-Aktionen auf:
  - `main`
  - `release`
  - `release/*`
- Solche Writes auf `main` oder `release` bleiben weiterhin nur mit expliziter Freigabe erlaubt.
- Solange nichts anderes gefordert ist, sollen PRs und Merges weiterhin **nach `next`** gehen.

## Bekannte Fallstricke

1. **Tests noch nicht vollständig stabil**
   Auf `next` laufen Restore, Build und CI inzwischen reproduzierbar. Trotzdem bleibt die Engine-Testbasis ein aktiver Arbeitsvorrat; vor allem Multi-Instance-, Error- und Timer-Pfade sollten weiterhin kritisch geprüft werden.

2. **V8-/Expression-Thema nicht abgeschlossen**
   Die Default-Expression-Logik hängt weiterhin an `ClearScript/V8`. Für Tests und CI gibt es jetzt einen robusteren Fallback-Pfad, die langfristige FEEL-/V8-Strategie bleibt aber offen.

3. **CI ist vorhanden, aber noch im Stabilisierungsmodus**
   Nutze die GitHub-Checks als Basis, behandle trotz grünem Grundlauf weiterhin problematische Randfälle als aktiven Arbeitsvorrat.

4. **Nicht alle Verzeichnisse sind gleich aktiv**
   Es gibt Spuren älterer bzw. unfertiger Arbeit. Prüfe vor Refactorings, welche Projekte tatsächlich von der Solution und vom Produktpfad genutzt werden.

## Nützliche Kommandos

### .NET

```bash
dotnet restore core-engine.sln
dotnet build core-engine.sln
dotnet test src/core-engine-tests/core-engine-tests.csproj
```

### bpmn.io

```bash
cd bpmn.io
npm install
npm run build
```

## Erwartung an gute Agenten-Arbeit

Wenn du an diesem Repository arbeitest, sollte dein Ergebnis nach Möglichkeit:

- die aktuelle Lage ehrlich widerspiegeln
- keine Reife behaupten, die der Code nicht trägt
- Build/Test/Tooling verbessern oder zumindest nicht verschlechtern
- die Roadmap nicht verwässern, sondern vereinfachen
