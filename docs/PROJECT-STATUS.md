# Projektstatus: Flowzer BPMN Core Engine

**Stand:** 12. April 2026

## Kurzfazit

Das Projekt ist **nicht mehr im kritischen Stillstand**, sondern wieder in einer aktiven Stabilisierungs- und Ausbauphase.

Meine ehrliche Einschätzung auf dem heutigen Stand:

| Bereich | Einschätzung |
|---|---|
| Fachliche Idee | stark |
| Architektur-Grundlage | gut |
| Build-/CI-Zustand | solide |
| Testbarkeit | solide mit Ausbaupotenzial |
| Produktreife | mittel |
| Wiederbelebungschance | sehr gut |

Der wichtigste Unterschied zum früheren Stand: Das Repository ist wieder **arbeitsfähig**, `next` ist als Integrationsbranch etabliert und die größten Basisprobleme wurden bereits systematisch angegangen.

## Was inzwischen erreicht wurde

### 1. Arbeitsmodell und Projektorganisation

- `next` dient als langlebiger Integrationsbranch
- größere Themen werden über eigene Topic-Branches und PRs nach `next` umgesetzt
- die Dokumentation im Repository wurde auf einen realistischeren Stand gebracht
- offene Frontend-Arbeit wurde in kleinere GitHub-Issues zerlegt, damit keine unklaren Sammelthemen mehr dominieren

### 2. Build, CI und Testbasis

- die Solution baut auf dem aktuellen Arbeitsstand reproduzierbar
- GitHub Actions für .NET und `bpmn.io` sind vorhanden
- die wichtigsten lokalen Testpfade laufen wieder:
  - `dotnet build core-engine.sln`
  - `dotnet test src/core-engine-tests/core-engine-tests.csproj`
  - `dotnet test src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`
  - `dotnet test src/FlowzerFrontend.Tests/FlowzerFrontend.Tests.csproj`
  - `tests/ui-smoke` per Playwright

### 3. Bereits umgesetzte Stabilisierung

Unter anderem bereits umgesetzt:

- V8-/Expression-Fallback für CI-/lokale Umgebungen
- Multi-Instance- und Engine-Stabilisierung aus dem früheren Testproblemfeld
- Demo-Console-App für einen nachvollziehbaren Happy Path
- DTO-/Warnungsbereinigung und API-Härtung in mehreren Teilbereichen
- Signal- und Service-Task-Subscriptions im Web-API-Pfad
- Nullability- und Guard-Härtung in zentralen Frontend-Seiten
- lokale Runtime-Containerbasis für API, Frontend und Gateway

## Was weiterhin bremst

### 1. Produktpfade sind noch nicht komplett durchgehärtet

Besonders relevant sind noch:

- weiter ausgebautes Playwright-/E2E-Smoke-Set
- Restlücken bei Auth-/Identity- und Fehlerpfaden
- Release-/Telemetry-/Secret-Story über die lokale Basis hinaus

### 2. Es gibt noch Restlücken im Codebestand

Noch offen sind unter anderem:

- einzelne `NotImplementedException`-Stellen in Runtime-/Storage-Pfaden
- provisorische Auth-/Identity-Platzhalter
- Betriebs- und Deployment-Themen wie Reverse Proxy, TLS, Logging-/Telemetry und Recovery
- Altlasten und Doppelstrukturen im Repository

Für die inzwischen vorhandene lokale Start- und Diagnosebasis siehe zusätzlich [`docs/OPERATIONS.md`](./OPERATIONS.md).
Eine aktuellere Einordnung der verbliebenen Engine-/Runtime-Lücken steht zusätzlich in [`docs/RUNTIME-GAPS.md`](./RUNTIME-GAPS.md).

### 3. Dokumentation muss nun mit der Technik mitwachsen

Die Basisdokumentation ist deutlich besser als zuvor, aber für die nächste Reifestufe fehlen bzw. benötigen Updates:

- Architekturübersicht
- API-/Fehlervertragsdokumentation
- Storage-/Persistenzdokumentation
- Test- und E2E-Dokumentation
- aktualisierte Status-/Roadmap-Texte bei größeren Fortschritten

## Offene GitHub-Issues auf dem aktuellen Stand

Die erste große Stabilisierungsrunde ist inzwischen weitgehend geschlossen. Der aktuelle nächste Ausbaupunkt ist:

- [#63 Release-Dockerfiles und runtime-nahe Compose-Variante ergänzen](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/63)

Die frühere Frontend-Aufteilung über [#47](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/47) bis [#50](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/50) sowie die Härtungen [#52](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/52) bis [#55](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/55) sind bereits abgeschlossen.

## Aktuelle Gesamtempfehlung

Das Projekt sollte jetzt **nicht mehr primär gerettet**, sondern gezielt **zur produktionsnahen Nutzbarkeit weiterentwickelt** werden.

Die sinnvolle Reihenfolge ist aus heutiger Sicht:

1. runtime-nahe Release-Containerbasis sauber abschließen
2. Reverse-Proxy-/Secret-/Telemetry-Story weiter schärfen
3. verbleibende Runtime- und Auth-Lücken abbauen
4. E2E- und Recovery-/Operations-Dokumentation weiter vertiefen

## Gesamturteil

Flowzer BPMN Core Engine ist aktuell **kein gescheitertes Projekt**, sondern ein wieder belebtes Projekt mit belastbarer Basis. Der kritische Unterschied ist, dass jetzt nicht mehr an einer diffusen Vision gearbeitet wird, sondern in klaren, testbaren und reviewbaren Arbeitspaketen auf `next`.
