# Projektstatus: Flowzer BPMN Core Engine

**Stand:** 11. April 2026

## Kurzfazit

Das Projekt hat **Substanz und Potenzial**, ist aber aktuell organisatorisch und technisch an einem Punkt, an dem es leicht „einschlafen“ kann, wenn nicht zuerst die Basis stabilisiert wird.

Meine ehrliche Bewertung:

| Bereich | Einschätzung |
|---|---|
| Fachliche Idee | stark |
| Architektur-Grundlage | gut |
| Code-Reife | mittel bis niedrig |
| Build-/Tooling-Zustand | niedrig |
| Contributor Experience | niedrig |
| Wiederbelebungschance | gut, wenn jetzt priorisiert gearbeitet wird |

## Was bereits gut ist

### 1. Klare fachliche Richtung

Die Trennung zwischen BPMN-Modell, Engine, API und Frontend ist grundsätzlich nachvollziehbar. Das ist eine gute Basis für spätere Modularisierung.

### 2. Bereits vorhandene Testbasis

Unter `src/core-engine-tests/` liegt eine nützliche Sammlung aus:

- Unit-Tests
- BPMN-Testdateien
- reproduzierbaren Engine-Szenarien

Das ist deutlich mehr wert als „nur Code“, weil es spätere Stabilisierung ermöglicht.

### 3. Mehr als nur ein Library-Prototyp

Das Projekt denkt bereits in mehreren Ebenen:

- Domänenmodell
- Engine
- Web-API
- Frontend
- Modeler-Integration
- Storage-Abstraktionen

Das zeigt: Hier steckt Produktdenken drin, nicht nur eine technische Spielerei.

## Was aktuell bremst

### 1. Der Repository-Zustand ist nicht sauber stabilisiert

Auffällige Punkte:

- Testsuite noch nicht vollständig grün
- offene Stabilisierung rund um Expression-/V8-Handling
- erste CI-Basis muss sich noch im Alltag bewähren
- mehrere offene PRs ohne klare Entscheidung
- ältere / unfertige Verzeichnisse und Artefakte im Repository

### 2. Dokumentation war bisher zu optimistisch

Die vorhandene README formulierte an mehreren Stellen einen Reifegrad, den die Codebasis aktuell nicht belegt. Das erschwert neue Beiträge, weil Erwartung und Realität auseinanderlaufen.

### 3. Kritische Stabilisierungsthemen sind nicht abgeschlossen

Insbesondere:

- Expression-/V8-Abhängigkeit
- Multi-Instance-Verhalten
- Toolchain-Reproduzierbarkeit
- CI/Automatisierung

## Technische Beobachtungen aus der Analyse

### Build

- `dotnet restore core-engine.sln` läuft auf `next`.
- `dotnet build core-engine.sln --no-restore` läuft auf `next`.
- Auf `next` gibt es jetzt außerdem einen ersten GitHub-Actions-Workflow für Restore, Build, Tests und `bpmn.io`.

Weiterhin offen:

- `dotnet test core-engine.sln --no-build` ist noch nicht vollständig grün.
- Aktuell bekannte Ausreißer:
  - `ParallelTaskTest`
  - `SequentialTest`
  - `JavaScriptFeelTest` (harter absoluter Pfad)
- Diese drei Tests sind im aktuellen CI-Pfad vorübergehend quarantiniert, bis die separaten Engine-/Teststränge abgeschlossen sind.

Zusätzlich gab es Security-Warnungen u. a. für:

- `System.Text.Json` 8.0.3
- `AutoMapper` 13.0.1

### Tests

Die lokale Testlage ist inzwischen besser eingrenzbar: Build und CI-Basis stehen auf `next`, aber die Testsuite ist noch nicht vollständig grün.

### Architektur

Positiv:

- Modell- und Engine-Trennung ist sichtbar
- Storage ist abstrahiert
- Web/API und Frontend existieren separat

Negativ:

- einzelne unvollständige Implementierungen (`NotImplementedException`, TODOs)
- inkonsistente Reife zwischen Modulen
- CI ist vorhanden, muss sich aber erst noch in der täglichen Weiterentwicklung bewähren

## Offene GitHub-Issues

Zum Zeitpunkt der Analyse offen:

- [#10 Test-Fehler beheben](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/10)
- [#7 Frontend bauen](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/7)
- [#5 Schnittstelle von ICore final definieren](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/5)
- [#4 Demo Console Application](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/4)

### Meine Einschätzung dazu

#### #10 – Test-Fehler beheben

Weiterhin hoch relevant, aber inzwischen eigentlich zu klein beschrieben. Das Problem ist nicht mehr nur „ein Testfehler“, sondern eher **Stabilisierung von Build + Test + Expression-Handling**.

#### #7 – Frontend bauen

Sinnvoll, aber **nicht als nächstes**. Erst wenn Build, API und Grundpfade stabil sind. Sonst investiert man in Oberfläche auf instabiler Basis.

#### #5 – ICore final definieren

Strategisch sehr wichtig. Sobald die technische Basis wieder steht, sollte das eine der nächsten Architekturaufgaben sein.

#### #4 – Demo Console Application

Sehr sinnvoll für Adoption und Contributor Experience – aber ebenfalls **nach** Stabilisierung.

## Offene Pull Requests

Zum Zeitpunkt der Analyse offen:

- [#16 Fix V8 dependency issue in tests - implement comprehensive fallback system](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/16)
- [#17 Bump the nuget group with 1 update](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/17)
- [#18 Bump the npm_and_yarn group across 1 directory with 5 updates](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/18)
- [#19 Bump the nuget group with 1 update](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/19)

### Einschätzung pro PR

#### #16 – Draft PR zum V8-/Testproblem

Das ist die inhaltlich wichtigste offene PR.

Positiv:

- adressiert ein reales Problem
- versucht Konfiguration und Expression-Handling sauberer zu machen
- bringt sichtbaren fachlichen Mehrwert

Risiken / offene Punkte:

- weiterhin offene Multi-Instance-Testprobleme
- Draft-Status seit August 2025
- noch keine saubere Abschlussbewertung
- enthält weiterhin absolute Testpfade im Testkontext, was ein Signal für unfertige Testhygiene ist

**Empfehlung:** Nicht verwerfen, aber **neu bewerten, auf aktuellen Main heben und gezielt abschließen** – idealerweise in kleinerem, klarerem Scope.

#### #17 – `System.Text.Json` Update

Wahrscheinlich sinnvoll und eher risikoarm. Sollte aber nicht blind gemerged werden, bevor Build/Frontend geprüft sind.

**Empfehlung:** Nach Stabilisierung zeitnah prüfen und mergen.

#### #18 – npm-/Frontend-Sicherheitsupdates

Ebenfalls sinnvoll. Da `bpmn.io` kein zentral abgesicherter CI-Pfad ist, sollte vorher einmal lokal gebaut/getestet werden.

**Empfehlung:** Nach oder zusammen mit einer kleinen JS-/CI-Stabilisierung prüfen.

#### #19 – AutoMapper Major Update

Das ist **nicht** einfach nur ein kleines Security-Update. Laut PR-Beschreibung bringt der Sprung auf AutoMapper 15/16 auch **Lizenz-/Breaking-Change-Themen** mit.

**Empfehlung:** Nicht blind mergen. Eher separat fachlich/technisch bewerten. Falls möglich, lieber eine kompatiblere Sicherheitsstrategie wählen als unkontrolliert ein Major-Upgrade einzuführen.

## Was zuerst passieren sollte

### Phase 1 – Projekt retten / wieder handlungsfähig machen

1. Build reproduzierbar machen
2. fehlende ProjectReferences bereinigen
3. .NET-SDK sauber festzurren
4. minimale CI einführen

### Phase 2 – offene Kernfrage lösen

5. PR #16 sauber bewerten und entscheiden
6. Testlage wieder verlässlich grün bekommen
7. Issue #10 neu zuschneiden oder erweitern

### Phase 3 – Produktlinie wieder klar machen

8. `ICore`/API sauber definieren
9. Demo-App erstellen
10. Frontend gezielt fertigziehen

## Gesamtempfehlung

Das Projekt sollte **nicht** mit neuen Features reanimiert werden, sondern mit einer **Stabilisierungsrunde in 2–4 kleinen, sauberen PRs**:

1. Toolchain + Build
2. CI + Repository-Aufräumen
3. PR #16 / Tests / Expression-Handling
4. danach API-/ICore-/Demo-Fokus

Wenn diese vier Schritte erledigt sind, ist die Wahrscheinlichkeit hoch, dass das Projekt wieder attraktiv für Beiträge wird – und eben **nicht stirbt**.
