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
- Boundary-Timer im Parser, in der Runtime und im persistierten Subscription-Pfad ergänzt
- persistierte Timer-Subscriptions in Storage/Web-API ergänzt
- Scheduler-/Polling-Pfad für fällige Timer im Web-API-Host ergänzt
- wiederkehrende Start-Timer inklusive Restwiederholungen und Catch-up-Verhalten ergänzt
- Startup-Recovery für überfällige Start-Timer über persistierte Timer-Subscriptions ergänzt
- Form-/Message-Fehlerverträge in der Web-API weiter geschärft
- lokale und CI-nahe Testpfade wieder grün gemacht
- lokale Runtime-Containerbasis für Release-nahe Prüfpfade ergänzt
- das ursprüngliche Frontend-Epic (#7) und seine Teilpakete #47–#50 sind abgeschlossen
- der erste große Revitalisierungs-Backlog ist damit weitgehend abgearbeitet

Die Roadmap startet also **nicht mehr bei Null**, sondern baut auf einer funktionierenden Basis auf.

## Priorität 1: Timer- und Runtime-Restlücken schließen

### 1.2 BPMN-Fehlerpfade und weitergehende Timer-Semantik vertiefen

- Error-/Escalation-Semantik jenseits des Best-Effort-Fallbacks modellieren
- Kompensations- und Abbruchpfade weiter präzisieren
- Boundary-Timer bei Bedarf um speziellere Randfälle wie konkurrierende Timer oder komplexere Recovery-Szenarien vertiefen

### 1.3 Timer-Recovery und wiederkehrende Strategien vertiefen

- Recovery-Verhalten für bereits persistierte Boundary- und Spezialtimer nach Neustarts weiter härten
- wiederkehrende Start-Timer nur noch bei komplexeren Spezialfällen vertiefen
- wiederkehrende Boundary- oder Spezialtimer nur dann ergänzen, wenn sie fachlich wirklich benötigt werden

## Priorität 2: Betriebs- und Auth-Reife erhöhen

### 2.1 Operations-Basis über die lokale Compose-Story hinaus vertiefen

- Metrics/Tracing
- Secret-/Konfigurationsstory
- Recovery-/Backup-Hinweise
- Reverse-Proxy-/TLS-Härtung für echte Zielumgebungen

### 2.2 Auth-/Identity-Pfade weiter absichern

- Claim-basierte Authentifizierung entlang echter Betriebsumgebungen verdrahten
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

### Sprint A – Timer-Runtime vertiefen

- Boundary-/Spezialtimer-Recovery
- verbleibende wiederkehrende Spezialtimer
- verbleibende Boundary-Timer-Randfälle

### Sprint B – Betrieb und Auth

- Telemetrie/Secrets/Recovery
- Claim-/Rollenmodell und Auth-/Identity-Härtung

### Sprint C – E2E und Dokumentation

- zusätzliche E2E-/Smoke-Pfade
- Architektur- und Operations-Dokumentation vertiefen

## Leitprinzip für die weitere Arbeit

Neue Features sollten weiterhin **nur dann** priorisiert werden, wenn die zugehörigen Kernpfade bereits belastbar getestet und dokumentiert sind. Die Stärke des Projekts liegt jetzt nicht in maximaler Breite, sondern in der Kombination aus **klaren Arbeitspaketen, reproduzierbarer Testbasis und schrittweise steigender Produktreife**.
