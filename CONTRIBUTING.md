# Beitragen zu Flowzer BPMN Core Engine

Vielen Dank für dein Interesse am Projekt.

Dieses Repository hat bereits eine gute fachliche Basis, braucht aktuell aber vor allem **Stabilisierung, Aufräumen und verlässliche Entwicklungsabläufe**. Beiträge sind deshalb besonders wertvoll, wenn sie nicht nur Features ergänzen, sondern die Wartbarkeit verbessern.

## Bevor du startest

Bitte lies zuerst:

- [README.md](README.md)
- [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md)
- [docs/ROADMAP.md](docs/ROADMAP.md)
- [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md)
- [AGENTS.md](AGENTS.md) – falls du mit KI-Unterstützung arbeitest

## Entwicklungsumgebung

### .NET

Empfohlen wird aktuell **.NET 8**.

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

## Aktuell bekannte Probleme

Bitte berücksichtige diese Baustellen bei deiner Arbeit:

- Auf `next` gibt es jetzt eine erste GitHub-Actions-CI für Restore, Build, Test und `bpmn.io`.
- Drei Tests sind aktuell noch temporär quarantiniert: `ParallelTaskTest`, `SequentialTest` und `JavaScriptFeelTest`.
- Das Expression-/V8-Thema aus PR #16 ist weiterhin fachlich offen.
- Einige Doku- und Architektur-Aussagen im Altbestand waren optimistischer als der tatsächliche Reifegrad.

## Wo welche Änderungen hingehören

### BPMN-Modell

Änderungen an BPMN-Elementen und Modellklassen gehören typischerweise nach:

- `src/FlowzerBPMN/`

### Engine / Laufzeit

Änderungen an Prozessausführung, Token-Verhalten, Flow-Handling oder Expressions gehören typischerweise nach:

- `src/core-engine/`
- `src/core-engine-tests/`

### API

REST-Endpunkte, Business-Logik und DTO-Mapping liegen vor allem in:

- `src/WebApiEngine/`
- `src/WebApiEngine.Shared/`

### Frontend

Blazor-Frontend:

- `src/FlowzerFrontend/`

Modeler-/JS-Themen:

- `bpmn.io/`

## Qualitätsanspruch für Beiträge

Ein guter Beitrag sollte nach Möglichkeit:

- ein klar abgegrenztes Problem lösen
- vorhandene Architektur respektieren
- neue technische Schulden vermeiden
- Tests ergänzen oder bestehende Tests anpassen
- relevante Doku mit aktualisieren

## Coding-Konventionen

Die bestehenden Projektregeln findest du in [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md). Kurzfassung:

- **Code, Typen, Member, Methoden auf Englisch**
- **Kommentare und erklärende Doku bevorzugt auf Deutsch**
- BPMN-Konformität nicht behaupten, sondern nachweisbar umsetzen
- Änderungen an der Engine möglichst mit Tests absichern
- Kleine, fokussierte Funktionen sind besser als große monolithische Blöcke

## Tests

Wenn du die Engine, Expressions, Parser oder Flow-Logik anfasst:

- ergänze möglichst Tests in `src/core-engine-tests/`
- nutze vorhandene BPMN-Dateien oder lege kleine, fokussierte Testmodelle an
- dokumentiere bekannte Lücken transparent, statt sie stillschweigend zu umgehen
- prüfe nach Möglichkeit zusätzlich den aktuellen CI-Pfad mit Coverage:

```bash
dotnet test src/core-engine-tests/core-engine-tests.csproj \
  --filter "FullyQualifiedName!=core_engine_tests.EngineTest.ParallelTaskTest&FullyQualifiedName!=core_engine_tests.EngineTest.SequentialTest&FullyQualifiedName!=core_engine_tests.JavaScriptExpressionTest.JavaScriptFeelTest" \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults/ci
```

## Pull Requests

Bitte halte Pull Requests möglichst klein und thematisch fokussiert.

Ein guter PR enthält:

- **Was wurde geändert?**
- **Warum wurde es geändert?**
- **Wie wurde es getestet?**
- **Welche Risiken / offenen Punkte gibt es noch?**

### Checkliste vor einem PR

- [ ] Build lokal geprüft
- [ ] Relevante Tests lokal geprüft
- [ ] Dokumentation bei Bedarf aktualisiert
- [ ] Keine offensichtlichen Debug-Reste / Platzhalter / temporären Dateien übrig
- [ ] Bekannte Einschränkungen offen beschrieben

## Besonders hilfreiche Beiträge

Aktuell sind Beiträge in diesen Bereichen besonders wertvoll:

1. Build-/SDK-Stabilisierung
2. Einführung von CI
3. Bereinigung verwaister Projekt- und Storage-Referenzen
4. Fertigstellung bzw. Neubewertung von PR #16
5. API- und `ICore`-Schnittstellenklärung
6. Demo-/Getting-Started-Erlebnis

## Kommunikation

Wenn du unsicher bist, lieber früh ein kleines Issue oder einen kleinen PR aufmachen, statt einen großen Wurf im Blindflug vorzubereiten.
