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

Empfohlen wird aktuell **.NET 10 SDK**. `global.json` ist die technische Quelle für die verwendete SDK-Version; README und diese Beitragsdoku sind die dokumentarische Referenz.

```bash
dotnet restore core-engine.sln
dotnet build core-engine.sln --no-restore --configuration Release
dotnet test src/core-engine-tests/core-engine-tests.csproj --no-restore --configuration Release
```

### Oberfläche

```bash
npm --prefix src/FlowzerConsole ci
npm --prefix src/FlowzerConsole run test
npm --prefix src/FlowzerConsole run build
```

## Aktuell bekannte Probleme

Bitte berücksichtige diese Baustellen bei deiner Arbeit:

- Auf `main` und `release` gibt es eine GitHub-Actions-CI für Restore, Build, Test, die Konsole und die UI-Smokes.
- Zwei Tests sind aktuell noch temporär quarantiniert: `ParallelTaskTest` und `SequentialTest`.
- Für Test-/CI-Umgebungen ohne native V8-Abhängigkeit gibt es jetzt einen abgesicherten Fallback-Pfad; die vollständige FEEL-/V8-Story bleibt trotzdem ein Architekturthema.
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

### Oberfläche

React-Konsole samt BPMN-Modeler und Formulareditor:

- `src/FlowzerConsole/`

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
dotnet restore core-engine.sln
dotnet build core-engine.sln --no-restore --configuration Release
dotnet test src/core-engine-tests/core-engine-tests.csproj \
  --no-restore \
  --no-build \
  --configuration Release \
  --filter "FullyQualifiedName!=core_engine_tests.EngineTest.ParallelTaskTest&FullyQualifiedName!=core_engine_tests.EngineTest.SequentialTest" \
  --collect:"XPlat Code Coverage" \
  --logger "trx;LogFileName=core-engine-tests.trx" \
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

## Lizenz deines Beitrags

Das Projekt steht unter der [Mozilla Public License 2.0](LICENSE) (MPL-2.0). Mit dem
Einreichen eines Pull Requests stimmst du zu, dass dein Beitrag unter denselben Bedingungen
lizenziert wird ("Inbound = Outbound"): Deine Änderungen an MPL-lizenzierten Dateien werden
Teil des unter MPL-2.0 stehenden Codes. Du bestätigst außerdem, dass du berechtigt bist,
den Beitrag unter dieser Lizenz einzureichen (eigener Code oder eine mit MPL-2.0
kompatible Herkunft). Eine Übersicht der Lizenzen verwendeter Drittanbieter-Abhängigkeiten
steht in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); neue Abhängigkeiten bitte mit
einer damit verträglichen Lizenz auswählen und die Datei per
`python3 scripts/ci/generate-third-party-notices.py` aktualisieren, wenn sich direkte
Abhängigkeiten ändern.

Sicherheitsrelevante Funde bitte nicht als öffentlichen Issue/PR einreichen, sondern über
den in [SECURITY.md](SECURITY.md) beschriebenen Weg melden.
