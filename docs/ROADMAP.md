# Roadmap-Vorschlag

## Zielbild

Flowzer BPMN Core Engine soll von einem interessanten, aber fragilen Prototypen zu einem **stabilen, nachvollziehbaren BPMN-Core mit API und guter Contributor Experience** weiterentwickelt werden.

## Priorität 1: Stabilisieren

### 1.1 Toolchain festziehen

- .NET-SDK-Version verbindlich machen
- lokale Build-Anleitung vereinheitlichen
- fehlende/verwaiste Projektverweise entfernen oder wiederherstellen

### 1.2 Buildfehler beseitigen

- aktuellen Solution-Build wieder grün bekommen
- Restore-Warnungen bereinigen
- offensichtliche Repo-Inkonsistenzen entfernen

### 1.3 CI einführen

Mindestens:

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- optional `npm ci && npm run build` für `bpmn.io`

## Priorität 2: Engine korrekt und testbar machen

### 2.1 PR #16 entscheiden

- auf aktuellen Main heben
- Scope prüfen
- V8-/Expression-Fallback sauber bewerten
- Multi-Instance-Nachwirkungen getrennt behandeln

### 2.2 Teststrategie schärfen

- flaky/unfertige Tests identifizieren
- BPMN-Testmodelle gezielt strukturieren
- klare Regressionstests für Parser, Gateways, Multi-Instance und Message/Signal-Flows

### 2.3 Expression-Strategie klären

- FEEL/V8-Abhängigkeit bewusst designen
- optional einfache Fallback-Strategie definieren
- Testumgebungen ohne native V8-Bibliotheken sauber unterstützen

## Priorität 3: Öffentliche Schnittstellen klarziehen

### 3.1 `ICore` finalisieren

- Was ist die stabile Kern-API?
- Was ist intern?
- Welche Datenstrukturen sind Teil des öffentlichen Vertrags?

### 3.2 API konsolidieren

- REST-Endpunkte dokumentieren
- Fehlerbilder vereinheitlichen
- DTOs und Domänenmodelle schärfen

### 3.3 Storage schärfen

- Dateisystem-Storage sauber dokumentieren
- Memory-Storage entweder wiederherstellen oder Referenzen entfernen

## Priorität 4: Entwicklererlebnis verbessern

### 4.1 Demo-App / Happy Path

- Console-Demo zu Issue #4
- einfacher End-to-End-Beispielprozess
- klarer „so starte ich das Projekt“-Pfad

### 4.2 GitHub-Hygiene

- offene Issues nachschärfen
- offene PRs aktiv entscheiden
- Labels/Meilensteine nutzen
- PR-Checklisten einsetzen

### 4.3 Doku weiter ausbauen

Sinnvolle nächste Dokumente:

- `docs/ARCHITECTURE.md`
- `docs/API.md`
- `docs/TESTING.md`
- `docs/STORAGE.md`

## Empfohlene konkrete Umsetzungsreihenfolge

### Sprint A – „Projekt wieder grün bekommen“

- Buildfehler beheben
- fehlende ProjectReference bereinigen
- CI-Grundgerüst anlegen
- SDK festzurren

### Sprint B – „Tests und Engine verlässlich machen“

- PR #16 bewerten
- Teststatus sauber herstellen
- Expression-/V8-Thema entscheiden

### Sprint C – „Projekt wieder attraktiv machen“

- `ICore`-Thema angehen
- Demo-App bauen
- README/API/Demo-Flows verbessern

### Sprint D – „Produktreife erhöhen“

- Frontend fokussiert abschließen
- Security-PRs gezielt abarbeiten
- Packaging / Release-Story / Versionierung definieren
