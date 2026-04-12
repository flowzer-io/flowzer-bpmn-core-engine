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
- Timer-Ausführung im Engine-Kern für fällige Start- und Intermediate-Timer ergänzt
- Form-/Message-Fehlerverträge in der Web-API weiter geschärft
- lokale und CI-nahe Testpfade wieder grün gemacht
- lokale Runtime-Containerbasis für Release-nahe Prüfpfade ergänzt
- das ursprüngliche Frontend-Epic (#7) und seine Teilpakete #47–#50 sind abgeschlossen
- der erste große Revitalisierungs-Backlog ist damit weitgehend abgearbeitet

Die Roadmap startet also **nicht mehr bei Null**, sondern baut auf einer funktionierenden Basis auf.

## Priorität 1: Timer- und Runtime-Restlücken schließen

### 1.1 Timer-Persistenz und Scheduler-Anbindung

- persistierte Timer-Subscriptions ergänzen
- Wiederaufnahme nach Neustart für fällige Timer vorbereiten
- klaren Scheduler-/Polling-Pfad um `HandleTime(...)` modellieren und testen

### 1.2 Boundary-Timer und BPMN-Fehlerpfade vertiefen

- Boundary-Timer-Parsing und -Abarbeitung ergänzen
- Error-/Escalation-Semantik jenseits des Best-Effort-Fallbacks modellieren
- Kompensations- und Abbruchpfade weiter präzisieren

## Priorität 2: Betriebs- und Auth-Reife erhöhen

### 2.1 Operations-Basis über die lokale Compose-Story hinaus vertiefen

- Metrics/Tracing
- Secret-/Konfigurationsstory
- Recovery-/Backup-Hinweise
- Reverse-Proxy-/TLS-Härtung für echte Zielumgebungen

### 2.2 Auth-/Identity-Pfade weiter absichern

- Fallback-/Development-Pfade weiter reduzieren
- Nutzer- und Rollenmodell klarer kapseln
- API-Verträge und Betriebssignale entlang der Auth-Story ergänzen

## Priorität 3: Test- und Dokumentationsreife nachziehen

### 3.1 Weitere E2E-/Smoke-Pfade gezielt ausbauen

- Kernpfade für Direktaufrufe, Refreshes und Betriebsfehler weiter ausbauen
- Testdaten und Hilfslogik für reproduzierbare Läufe schaffen
- die Suite klein und CI-tauglich halten

### 3.2 Architektur-, Status- und Repo-Hygiene weiter verbessern

- Altlasten und Doppelstrukturen bewerten und bereinigen
- Status- und Roadmap-Dokumente laufend aktualisieren
- technische Realität und Doku synchron halten

## Empfohlene Reihenfolge der nächsten Sprints

### Sprint A – Timer und Runtime

- Timer-Persistenz
- Scheduler-/Polling-Pfad
- Boundary-Timer-Sicht

### Sprint B – Betrieb und Auth

- Telemetrie/Secrets/Recovery
- Auth-/Identity-Härtung

### Sprint C – E2E und Dokumentation

- zusätzliche E2E-/Smoke-Pfade
- Architektur- und Operations-Dokumentation vertiefen

## Leitprinzip für die weitere Arbeit

Neue Features sollten weiterhin **nur dann** priorisiert werden, wenn die zugehörigen Kernpfade bereits belastbar getestet und dokumentiert sind. Die Stärke des Projekts liegt jetzt nicht in maximaler Breite, sondern in der Kombination aus **klaren Arbeitspaketen, reproduzierbarer Testbasis und schrittweise steigender Produktreife**.
