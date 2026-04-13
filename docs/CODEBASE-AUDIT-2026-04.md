# Codebase-Audit April 2026

**Stand:** 13. April 2026

## Ziel des Audits

Dieses Audit beantwortet drei Fragen:

1. **Was fehlt nach der ersten Revitalisierungswelle noch?**
2. **Wo ist der Code heute schon solide, aber noch nicht sauber genug strukturiert?**
3. **Welche Arbeitspakete sollten als Nächstes in eigenen `codex/*`-Branches nach `next` umgesetzt werden?**

## Kurzfazit

Die Codebasis ist heute **klar arbeitsfähig und deutlich belastbarer** als noch zu Beginn der Rettungsphase. Gleichzeitig zeigt das Audit, dass die nächsten Schritte nicht mehr primär „Build wieder grün bekommen“, sondern **Wartbarkeit, Semantik, Auth und Betriebsreife** betreffen.

## Was heute schon gut ist

- `next` baut und testet reproduzierbar
- Engine-, API-, Frontend- und UI-Smoke-Pfade sind vorhanden
- Timer-, Boundary-, Diagnose- und erste Observability-Pfade sind inzwischen im Produktpfad angekommen
- die Projektstruktur ist fachlich grundsätzlich sinnvoll getrennt (`FlowzerBPMN`, `core-engine`, `WebApiEngine`, `FlowzerFrontend`, Storage)

## Wichtigste Audit-Ergebnisse

### 1. Runtime-Semantik ist verbessert, aber noch nicht abgeschlossen

Offene Restthemen liegen vor allem in:

- spezieller Boundary-/Spezialtimer-Recovery
- weitergehender Error-/Escalation-Semantik
- Kompensation und präziseren Abbruchpfaden
- offenen Start-/Initialisierungsfragen wie `ProcessEngine.ActiveUserTasks()`

**Folgeissue:** [#93](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/93)

### 2. Auth-/Identity-Pfade sind noch eine Übergangslösung

Die API erzwingt heute bereits einen aufgelösten Benutzerkontext, aber:

- Rollen und Claims sind noch nicht als vollständiges Modell ausgearbeitet
- der technische Development-Header ist bewusst nur ein lokaler Helfer
- produktive Auth-/Berechtigungspfade brauchen noch eine klarere Zielarchitektur

**Folgeissue:** [#94](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/94)

### 3. Operations-Basis ist vorhanden, aber noch nicht produktionsreif

Vorhanden sind inzwischen:

- lokale Compose-/Runtime-Skripte
- Health-/Readiness-Endpunkte
- Diagnose-Endpunkt
- optionale OpenTelemetry-Exporter

Nicht abgeschlossen sind dagegen u. a.:

- Collector-/Dashboard-/Alerting-Pfade
- Secrets- und TLS-Strategie
- Reverse-Proxy-Härtung
- Backup-/Restore- und Recovery-Automatisierung

**Folgeissue:** [#95](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/95)

### 4. Codehygiene und Struktur sind der nächste große Hebel

Das Audit hat mehrere strukturelle Baustellen sichtbar gemacht:

- sehr große Dateien erschweren Reviews und Wartung, z. B.:
  - `src/WebApiEngine.Tests/ApiHardeningIntegrationTest.cs`
  - `src/core-engine-tests/EngineTest.cs`
  - `src/core-engine/ModelParser.cs`
  - `src/WebApiEngine/BusinessLogic/BpmnBusinessLogic.cs`
  - `src/FlowzerFrontend/Pages/Instance.razor.cs`
- es existieren weiterhin mehrere generische `Exception`-Würfe statt fachlich passender Fehler
- offene TODOs markieren noch nicht sauber abgeschlossene Pfade
- öffentliche Methoden in Kern- und API-Klassen sind nicht überall dokumentiert

**Folgeissue:** [#96](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/96)

### 5. Die Testsuite ist fachlich wertvoll, aber war bislang zu wenig erklärt

Zum Audit-Zeitpunkt gab es **166** `[Test]`-Methoden in den drei zentralen Testprojekten.

Der technische Abdeckungsstand war gut, aber die Wartbarkeit litt darunter, dass die Tests ihren Zweck nicht immer unmittelbar erklärten. Deshalb gilt jetzt als Leitlinie:

- **jeder Test braucht einen kurzen Kommentar zum Testzweck**
- große Testsammlungen sollen mittelfristig weiter aufgeteilt werden
- E2E-/UI-Smoke-Pfade sollen gezielt, aber nicht unkontrolliert wachsen

**Folgeissue:** [#97](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/97)

## Priorisierter Schlachtplan

Der übergreifende Folgeplan ist als eigenes GitHub-Issue dokumentiert:

- [#98 Schlachtplan nach der Revitalisierung: Wartbarkeit, Runtime, Auth und Betrieb gezielt weiterziehen](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/98)

Empfohlene Reihenfolge:

1. Codehygiene und Verständlichkeit erhöhen
2. Testsuite dokumentieren und strukturieren
3. Runtime-Restsemantik schließen
4. Auth-/Identity-Modell produktionsnäher machen
5. Operations-/Deployment-Reife ausbauen

## Was dieses Audit direkt ausgelöst hat

- Der Folge-Backlog wurde in konkrete Issues zerlegt.
- Alle bestehenden NUnit- und Playwright-Tests wurden im zugehörigen Paket um einen kurzen Kommentar zum Testzweck ergänzt.
- Die weitere Arbeit orientiert sich nicht mehr an diffusen Sammelaufgaben, sondern an klar reviewbaren Paketen.

## Leitprinzip für die nächsten Pakete

Die Revitalisierung ist jetzt an einem Punkt, an dem **Qualität vor Breite** wichtiger ist als neue Features. Das Repository profitiert aktuell am meisten von:

- kleineren, nachvollziehbaren Refactorings
- präziserer Semantik in Edge Cases
- klarerem Betriebsmodell
- verständlicherer Test- und API-Dokumentation
