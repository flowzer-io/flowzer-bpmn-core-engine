# Roadmap-Vorschlag

## Zielbild

Flowzer BPMN Core Engine soll von einem stabilisierten Entwicklungsstand zu einem **verlässlich nutzbaren BPMN-Kern mit API, Frontend, reproduzierbarer Testbasis und produktionsnaher Betriebsgrundlage** weiterentwickelt werden.

## Was bereits erreicht ist

Die erste große Stabilisierungsrunde ist bereits erfolgt:

- `next` als Integrationsbranch eingeführt
- Grunddokumentation neu aufgesetzt
- Build- und CI-Basis stabilisiert
- Demo-Console-App ergänzt
- wesentliche Engine-/Subscription-/Frontend-Härtungen umgesetzt
- lokale und CI-nahe Testpfade wieder grün gemacht

Die Roadmap startet also **nicht mehr bei Null**, sondern baut auf einer funktionierenden Basis auf.

## Priorität 1: Kernpfade vollständig stabilisieren

### 1.1 Formularpfad durchhärten

Referenz: [#48](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/48)

- Formularspeicherung, Metadaten und Versionspfad robust machen
- API-Verträge für Formulare sauber absichern
- Formularpfad bis zur Frontend-Anbindung testen

### 1.2 Frontend-Kernseiten weiter härten

Referenz: [#47](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/47)

- Navigation und Kernseiten systematisch prüfen
- Lade-, Nullability- und Zustandsprobleme weiter abbauen
- manuelle und kleine automatisierte Checks zusammenführen

### 1.3 Diagramm-/Modeler-Integration absichern

Referenz: [#49](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/49)

- Diagrammladen und Modeler-Interaktionen prüfen
- Fehlerzustände besser abfangen
- zentrale Diagramm-/Modeler-Pfade dokumentieren

## Priorität 2: Test- und Produktpfade vertiefen

### 2.1 Playwright-Smoke- und E2E-Suite ausbauen

Referenz: [#50](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/50)

- Kernpfade für Start, Formulare, Instanzen und Diagrammzugriffe automatisieren
- Testdaten und Hilfslogik für reproduzierbare Läufe schaffen
- die Suite klein und CI-tauglich halten

### 2.2 API-Fehlerverträge und Betriebs-Signale verbessern

Referenz: [#52](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/52)

- Fehlerantworten vereinheitlichen
- Health-/Readiness-Endpunkte ergänzen
- Auth-Platzhalter technisch sauberer kapseln

## Priorität 3: Restlücken abbauen

### 3.1 Engine- und Runtime-Restbestand bereinigen

Referenz: [#53](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/53)

- `NotImplementedException`-Stellen inventarisieren
- produktionskritische Pfade priorisiert schließen
- Regressionstests ergänzen

### 3.2 Repository- und Dokumentationshygiene weiter verbessern

Referenz: [#55](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/55)

- Altlasten und Doppelstrukturen bewerten und bereinigen
- Status- und Roadmap-Dokumente laufend aktualisieren
- technische Realität und Doku synchron halten

## Priorität 4: Produktnahe Betriebsbasis schaffen

### 4.1 Lokale Betriebs- und Deployment-Story vorbereiten

Referenz: [#54](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/54)

- lokale Startpfade für API und Frontend vereinheitlichen
- kleine Compose- und Skriptbasis für reproduzierbare lokale Starts schaffen
- Logging-/Betriebsanforderungen dokumentieren
- Compose-/Deployment-Basis für eine produktionsnahe Nutzung vorbereiten

### 4.2 Weitere Betriebsaspekte nachziehen

Sinnvolle Folgearbeiten danach:

- Metrics/Tracing
- Release-/Versionierungsstory
- Packaging/Container-Story
- Betriebsdokumentation für Fehleranalyse und Recovery

## Empfohlene Reihenfolge der nächsten Sprints

### Sprint A – Formular- und Frontend-Kernpfade

- #48 Formularpfad stabilisieren
- #47 Kernseiten und Navigation weiter härten

### Sprint B – Diagramm- und E2E-Pfade

- #49 Diagramm-/Modeler-Integration härten
- #50 Playwright-/E2E-Abdeckung ausbauen

### Sprint C – API- und Runtime-Reife

- #52 API-Härtung für Fehlerverträge, Health und Auth-Vorbereitung
- #53 Engine-/Runtime-Restlücken abbauen

### Sprint D – Betriebs- und Repo-Reife

- #54 Betriebs- und Deployment-Basis vorbereiten
- #55 Repo- und Status-Dokumentation weiter aufräumen

## Leitprinzip für die weitere Arbeit

Neue Features sollten weiterhin **nur dann** priorisiert werden, wenn die zugehörigen Kernpfade bereits belastbar getestet und dokumentiert sind. Die Stärke des Projekts liegt jetzt nicht in maximaler Breite, sondern in der Kombination aus **klaren Arbeitspaketen, reproduzierbarer Testbasis und schrittweise steigender Produktreife**.
