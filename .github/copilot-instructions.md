# Copilot Instructions für flowzer-bpmn-core-engine

Diese Hinweise sollen GitHub Copilot dabei helfen, in diesem Repository hilfreiche und realistische Vorschläge zu machen.

## Wichtiger Kontext

Dieses Repository ist **kein vollständig abgeschlossener BPMN-Stack**, sondern ein Projekt mit guter fachlicher Basis und aktuell laufender Stabilisierungsarbeit.

Bitte also:

- **nicht automatisch vollständige BPMN-2.0-Abdeckung annehmen**
- **keine Produktreife behaupten, die der Code nicht belegt**
- **vor Änderungen immer den aktuellen Repository-Zustand berücksichtigen**

## Zuerst lesen

- `README.md`
- `docs/PROJECT-STATUS.md`
- `docs/ROADMAP.md`
- `DEVELOPMENT-GUIDELINES.md`
- `AGENTS.md`

## Relevante Module

- `src/FlowzerBPMN/` – BPMN-Domänenmodell
- `src/core-engine/` – Engine, Handler, Parser, Expressions
- `src/core-engine-tests/` – Regressionstests und BPMN-Testmodelle
- `src/WebApiEngine/` – ASP.NET Core API
- `src/FlowzerFrontend/` – Blazor-Frontend
- `bpmn.io/` – JS-/Modeler-Themen

## Arbeitsprinzipien

### 1. Kleine, fokussierte Änderungen

Bevorzuge kleine Änderungen mit klarer Wirkung statt großer, riskanter Umbauten.

### 2. Tests ernst nehmen

Wenn Engine-, Parser-, Gateway-, Multi-Instance-, Message- oder Signal-Logik verändert wird, sollten die Änderungen möglichst durch Tests abgesichert werden.

### 3. Dokumentation mitdenken

Wenn Architektur, Setup oder bekannte Einschränkungen verändert werden, sollen passende Dokumente mit aktualisiert werden.

### 4. Sprache

- **Code auf Englisch**
- **Erklärende Kommentare und Doku bevorzugt auf Deutsch**

### 5. Git-Write-Regel im Repository

- Auf Arbeits- und Integrationszweigen dieses Repositories darf GitHub Copilot bzw. ein agentischer Workflow **ohne weitere Rückfrage committen und pushen**.
- Diese Freigabe gilt für Branches wie `codex/*`, `next` und vergleichbare Nicht-Release-Zweige.
- **Nicht** erlaubt ohne explizite Freigabe bleiben direkte Git-Write-Aktionen auf:
  - `main`
  - `release`
  - `release/*`
- Solange nichts anderes verlangt wird, sollen PRs weiterhin **nach `next`** erstellt werden.

## Bekannte Fallstricke

### Build, Tests und CI

- Auf `next` laufen Restore, Build und eine erste CI inzwischen reproduzierbar.
- Die bisher quarantänisierten Multi-Instance-Tests laufen inzwischen wieder regulär im CI-Pfad.
- Änderungen an Build-/SDK-/Testthemen bitte nicht stillschweigend einbauen, sondern sauber begründen und mit Doku flankieren.

### Expression-Handling

- Das Standard-Expression-Handling hängt an V8/ClearScript.
- Die Draft-PR #16 zeigt, dass dieser Bereich noch nicht abschließend gelöst ist.

### Reifegrad einzelner Module

- Nicht alle Verzeichnisse sind gleich aktiv oder gleich weit entwickelt.
- Prüfe vor größeren Refactorings immer zuerst, was tatsächlich produktiv genutzt wird.

## Gute Vorschläge in diesem Repository

Hilfreich sind vor allem Vorschläge, die:

- Build und Testbarkeit verbessern
- technische Schulden sichtbar machen und abbauen
- die Architektur klarer machen
- das Projekt für weitere Beiträge einfacher zugänglich machen

## Weniger hilfreiche Vorschläge

Weniger hilfreich sind Vorschläge, die:

- große neue BPMN-Features hinzufügen, bevor die Basis stabil ist
- ungetestete Magie in die Engine einbauen
- veraltete Annahmen aus alter Dokumentation wiederholen
- Sicherheitsupdates blind als trivial behandeln
