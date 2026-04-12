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
- lokale Runtime-Containerbasis für Release-nahe Prüfpfade ergänzt
- das ursprüngliche Frontend-Epic (#7) und seine Teilpakete #47–#50 sind abgeschlossen

Die Roadmap startet also **nicht mehr bei Null**, sondern baut auf einer funktionierenden Basis auf.

## Priorität 1: Release-nahe Betriebsbasis abschließen

### 1.1 Runtime-Container und Release-Compose ergänzen

Referenz: [#63](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/63)

- dedizierte Runtime-Container für API und Frontend bereitstellen
- Gateway-/Reverse-Proxy-Basis für Same-Origin-Betrieb ergänzen
- lokalen Build-/Start-/Check-Pfad für echte Runtime-Images dokumentieren

### 1.2 Weitere Betriebsaspekte nachziehen

- Metrics/Tracing
- Secret-/Konfigurationsstory
- Recovery-/Backup-Hinweise
- Reverse-Proxy-/TLS-Härtung für echte Zielumgebungen

## Priorität 2: Restliche Produktpfade vertiefen

### 2.1 Weitere E2E-/Smoke-Pfade ausbauen

- Kernpfade für Direktaufrufe, Refreshes und Betriebsfehler weiter ausbauen
- Testdaten und Hilfslogik für reproduzierbare Läufe schaffen
- die Suite klein und CI-tauglich halten

### 2.2 API-Fehlerverträge und Auth-/Betriebssignale weiter verbessern

- Fehlerantworten weiter vereinheitlichen
- Betriebsmetriken ergänzen
- Auth-Platzhalter technisch sauberer kapseln

## Priorität 3: Restlücken abbauen

### 3.1 Engine- und Runtime-Restbestand bereinigen

- `NotImplementedException`-Stellen inventarisieren
- produktionskritische Pfade priorisiert schließen
- Regressionstests ergänzen

### 3.2 Repository- und Dokumentationshygiene weiter verbessern

Referenz: [#55](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/55)

- Altlasten und Doppelstrukturen bewerten und bereinigen
- Status- und Roadmap-Dokumente laufend aktualisieren
- technische Realität und Doku synchron halten

## Empfohlene Reihenfolge der nächsten Sprints

### Sprint A – Runtime-Container abschließen

- #63 Release-Dockerfiles und runtime-nahe Compose-Variante ergänzen

### Sprint B – Betriebs- und E2E-Reife

- Gateway-/Reverse-Proxy-Pfade weiter härten
- zusätzliche direkte Refresh-/Fehlerpfade in Playwright abdecken

### Sprint C – Runtime- und Auth-Restlücken

- verbleibende Runtime-/Storage-/Auth-Lücken abbauen
- Recovery- und Operations-Dokumentation vertiefen

## Leitprinzip für die weitere Arbeit

Neue Features sollten weiterhin **nur dann** priorisiert werden, wenn die zugehörigen Kernpfade bereits belastbar getestet und dokumentiert sind. Die Stärke des Projekts liegt jetzt nicht in maximaler Breite, sondern in der Kombination aus **klaren Arbeitspaketen, reproduzierbarer Testbasis und schrittweise steigender Produktreife**.
