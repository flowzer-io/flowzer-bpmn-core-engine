# Flowzer BPMN Core Engine

Eine BPMN-Ausführungsengine in C#/.NET mit Parser, Laufzeitmodell, Web-API, React-Oberfläche und ersten Beispielprozessen.

> **Stand: 9. September 2026**
> Das Repository ist arbeitsfähig, die Kernpfade sind getestet. Die Roadmap-Slices liegen bis zur finalen Gesamtprüfung in gestapelten PRs; dazu gehören BFF, Directory, Form-/Human-Task-Verträge und eine zentrale BPMN-Fähigkeitsprüfung. Externe Bearer-Clients bleiben kompatibel. Die Oberfläche ist die **React-Konsole** in `src/FlowzerConsole`; die frühere Blazor-Oberfläche wurde entfernt. Die Bestandsaufnahme mit Empfehlungen steht in [docs/REVIEW-2026-09.md](docs/REVIEW-2026-09.md).

## Warum das Projekt spannend ist

Das Projekt bringt bereits einige starke Bausteine mit:

- BPMN-Modellklassen in `/src/FlowzerBPMN`
- Ausführungslogik in `/src/core-engine`
- API-Schicht in `/src/WebApiEngine`
- Oberfläche in `/src/FlowzerConsole` (React, TypeScript, bpmn-js, Form.io)
- Testprozesse und Unit-Tests in `/src/core-engine-tests`
- Beispielcode in `/examples`

Kurz gesagt: **Die Richtung stimmt.** Die Engine ist nicht „tot“, aber sie braucht gerade mehr Wartbarkeit und Fokus als neue Features.

## Realistischer Projektstatus

Die bisherige Dokumentation klang teilweise deutlich reifer als der aktuelle Stand der Codebasis. Realistischer formuliert:

- Es gibt bereits eine **brauchbare Kernarchitektur**.
- Es gibt **fachlich wertvolle Tests, BPMN-Beispiele und eine grüne CI-Basis auf `main`**.
- Zentrale Produktpfade wie Demo, UI-Smokes, API-Fehlerverträge und ein erster Timer-Kernpfad sind inzwischen vorhanden.
- Timer-Subscriptions werden jetzt auch in Storage/Web-API persistiert, über einen kleinen Scheduler-Polling-Pfad verarbeitet und können wiederkehrende Start-Timer inklusive Restwiederholungen abbilden.
- Geschützte API-Pfade verlangen inzwischen einen **aufgelösten Benutzerkontext**, statt stillschweigend über einen System-Fallback weiterzulaufen.
- Für lokale Entwicklung und UI-Smokes setzt das Frontend im **Development-Modus** jetzt automatisch einen technischen Benutzerheader, damit die gehärteten API-Pfade lokal weiter reproduzierbar testbar bleiben.
- Zusätzlich gibt es jetzt einen kleinen **Operations-/Diagnose-Endpunkt** sowie lokale Metrics-/Tracing-Namen, damit Scheduler- und Storage-Zustand nicht nur über reine Healthchecks sichtbar werden.
- Die Web-API kann diese Signale jetzt optional auch über **OpenTelemetry** an Console- oder OTLP-Exporter weitergeben, ohne den Default-Pfad in Dev/CI zu verschärfen.
- Es gibt aber weiterhin **offene Restlücken** bei weitergehender Timer-/Boundary-Recovery, Auth/Identity und Betriebsreife.
- Das Projekt ist **klar revivierbar und aktiv weiterentwickelbar**, wenn die nächsten Schritte weiter fokussiert bleiben.

Mehr Details: [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md)

## Repository-Struktur

```text
.
├── Model/                        # Geteilte DTOs / Modelklassen
├── examples/                     # Kleine Nutzungsbeispiele
├── src/
│   ├── FlowzerBPMN/              # BPMN-Domänenmodell
│   ├── core-engine/              # Engine / Ausführungslogik
│   ├── core-engine-tests/        # Unit-Tests + BPMN-Testdateien
│   ├── WebApiEngine/             # ASP.NET Core API
│   ├── WebApiEngine.Shared/      # API-DTOs
│   ├── FlowzerConsole/           # React-Oberfläche (Vite, TanStack, bpmn-js, Form.io)
│   ├── FilesystemStorageSystem/  # Dateibasierte Persistenz
│   ├── StorageSystemShared/      # Storage-Abstraktionen
│   └── Flowzer.Shared/           # Gemeinsame Hilfslogik
├── DEVELOPMENT-GUIDELINES.md     # Entwicklungsrichtlinien
├── CONTRIBUTING.md               # GitHub- und Beitragsleitfaden
├── AGENTS.md                     # Hinweise für KI-/Codex-Agenten
└── core-engine.sln               # Haupt-Solution
```

## Schnellstart

### Voraussetzungen

- **.NET 10 SDK im Feature-Band 10.0.1xx** (siehe `global.json`, z. B. 10.0.100 vom offiziellen Installer). Ein SDK aus einem anderen Band, etwa 10.0.400 aus Homebrew, wird abgelehnt. Liegt ein solches SDK vorn im `PATH`, hilft `export PATH=/usr/local/share/dotnet:$PATH`.
- **Node.js 22.15+** für die Konsole und die UI-Smokes
- Git

### .NET-Projekte

```bash
dotnet restore core-engine.sln
python3 scripts/ci/check_test_purpose_comments.py
dotnet build core-engine.sln
dotnet test src/core-engine-tests/core-engine-tests.csproj
dotnet test src/WebApiEngine.Tests/WebApiEngine.Tests.csproj
```

### Oberfläche

```bash
npm --prefix src/FlowzerConsole ci
npm --prefix src/FlowzerConsole run dev     # spricht über /api gegen http://localhost:5182
npm --prefix src/FlowzerConsole run test
npm --prefix src/FlowzerConsole run build
```

Ohne konfigurierten Identity Provider meldet die Konsole im Entwicklungsmodus einen
technischen Benutzer an, damit sich alle Seiten lokal ohne Anmeldung prüfen lassen.

## Bekannte Stolpersteine

Diese Punkte sollte man kennen, bevor man loslegt:

1. **Kernpfade sind stabil, aber noch nicht vollständig aufgeräumt**
   Build, CI sowie Kern-, Web-API-, Konsolen- und UI-Smoke-Pfade laufen auf `main` reproduzierbar grün. Die wichtigsten offenen Lücken liegen inzwischen eher in fachlichen Runtime- und Betriebsfragen als in der nackten Build-Stabilität.

2. **Expression-/V8-Thema nicht abgeschlossen**
   Test- und CI-Umgebungen laufen inzwischen auch ohne native V8-Abhängigkeit stabiler. Die vollständige FEEL-/V8-Strategie der Engine ist fachlich aber weiterhin ein eigener Architekturstrang.

3. **Timer-, Fehler- und Abbruchpfade sind verbessert, aber noch nicht vollständig**
   Der Engine-Kern kann fällige Timer jetzt weiterführen, Boundary-Timer im bestehenden Subscription-Pfad verarbeiten, wiederkehrende Start-Timer überfälligkeitstolerant nachziehen und rohe `NotImplementedException`-Abbrüche in mehreren Pfaden vermeiden. Offen bleiben weiterhin speziellere Recovery-Fragen, vollständige Fehler-/Eskalationssemantik und echte Kompensation.

4. **Betrieb und Auth sind belastbarer, aber noch nicht vollständig abgenommen**
   Der Runtime-Pfad besitzt OIDC/BFF, Bearer-Kompatibilität, Rollen, CSRF, einen persistenten Data-Protection-Keyring sowie Health-/Diagnose- und Telemetriegrundlagen. Vor einer Kundeninstallation fehlen weiterhin die echte Keycloak-/TLS-/Secret-Store-/Keyring-Restore-Abnahme und die weiterführende Recovery-Automatisierung.

## Authentifizierung und CORS

Der Runtime-Standard ist `Authentication:Scheme=Bff`: Die Web-API führt den
OIDC-Code-Flow mit PKCE als vertraulicher Client aus und hält Secret sowie Tokens
serverseitig. Die Konsole erhält stattdessen ausschließlich `HttpOnly`, `Secure`
`__Host-`-Cookies; Cookie-Mutationen benötigen den same-origin CSRF-Header
`X-Flowzer-CSRF`. `config.json` und das Browser-Storage enthalten keine OIDC-
Konfiguration, Secrets oder Tokens.

```json
"Authentication": {
  "Scheme": "Bff",
  "JwtBearer": {
    "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
    "Audience": "api://<api-client-id>"
  },
  "Bff": {
    "ClientId": "<confidential-client-id>",
    "DataProtectionKeysPath": "/var/lib/flowzer/data-protection"
  }
}
```

`ClientSecret` wird absichtlich nicht in JSON, `.env`, Browser oder Dokumentation
hinterlegt, sondern beim API-Start aus dem Secret-Store injiziert. Der
Data-Protection-Keyring benötigt ein ausschließlich für den API-Container
beschreibbares persistentes Volume, weil Session-, OIDC-Korrelations- und
Antiforgery-Cookies Redeploys überleben müssen.

Direkte/externe API-Clients dürfen unverändert Bearer-Tokens senden. `JwtBearer`
bleibt dafür als explizite Konfiguration vorhanden, `None` nur für lokale
Development-/CI-Prüfungen. Beide ersetzen nicht den BFF-Browserpfad. Hinter dem
Konsolen-nginx teilen Browser und API denselben Origin und benötigen kein CORS.
Details stehen in [docs/OPERATIONS.md](docs/OPERATIONS.md#authentifizierung-bff-und-externe-bearer-clients), der komplette Pilot-Ablauf in [docs/RUNBOOK-PILOT.md](docs/RUNBOOK-PILOT.md).

## Ordner und Fachverantwortung

Workflows lassen sich in einen Ordnerbaum mit beliebig tiefen Unterordnern einsortieren. An jedem Ordner hängt die Zuständigkeit für alles, was darin liegt: **Bearbeiten** (`editor`) darf die Workflows des Ordners anlegen, ändern, veröffentlichen und löschen; die **Fachverantwortung** (`steward`) darf zusätzlich Unterordner anlegen und die Zuständigkeit selbst weiterreichen. Zuweisungen gelten für alle Unterordner mit. Sie verwenden wahlweise den ausdrücklich erhaltenen Freitextmodus oder stabile Benutzer-/Gruppenreferenzen aus dem synchronisierten Directory.

Damit lässt sich ein Ausschnitt des Katalogs an die Menschen übergeben, die ihn fachlich verantworten, ohne ihnen die globale Rolle fürs Modellieren zu geben. Die oberste Ebene bleibt dieser Rolle vorbehalten, Lesen und Starten stehen weiterhin allen Zugelassenen offen. Regeln und Fehlerbilder in [docs/OPERATIONS.md](docs/OPERATIONS.md#ordner-und-delegation).

## Ablage

Standardmäßig persistiert die Web-API als JSON-Dateien unter `FLOWZER_STORAGE_ROOT`. Für den Betrieb steht eine PostgreSQL-Ablage mit echten Transaktionen bereit (`Storage:Provider=PostgreSql`, Migrationen per `dotnet WebApiEngine.dll --migrate`). Details in [docs/OPERATIONS.md](docs/OPERATIONS.md#ablage-dateisystem-oder-postgresql).

## Aufgabenentwürfe

Offene User-Tasks besitzen einen privaten serverseitigen Bearbeitungsstand mit
optimistischer Revision. Die Konsole kann ihn speichern, nach einem Neustart wieder
aufnehmen und bewusst verwerfen; konkurrierende Tabs überschreiben einander nicht still.
Der Server akzeptiert nur deklarierte beschreibbare Felder des an die Workflow-Version
gebundenen Formulars. Rechte, Lebenszyklus, API und Betriebsgrenzen stehen in
[docs/USER-TASK-DRAFTS.md](docs/USER-TASK-DRAFTS.md).

## Hostneutrale Integrationspakete

`packages/flowzer-sdk` stellt einen zustandslosen, aus dem versionierten OpenAPI-
Snapshot typisierten Client für Aufgaben, Formulare, Bearbeitungsaktionen,
Verzeichnisauswahl und Vorgangsstatus bereit. `packages/flowzer-react` ergänzt
optionale darstellungsfreie Hooks und Render-Prop-Controller. Authentisierung,
Darstellung und Fachobjekte bleiben vollständig bei der konsumierenden Anwendung;
Flowzer kennt keine konkrete Host-Anwendung. Verträge und Entwicklungsablauf stehen
in [packages/flowzer-sdk/README.md](packages/flowzer-sdk/README.md),
[packages/flowzer-react/README.md](packages/flowzer-react/README.md) und
[docs/HOST-INTEGRATION.md](docs/HOST-INTEGRATION.md).

## Gemeinsame Formularbibliothek

Jedes Formular ist zugleich eine wiederverwendbare Komponente und kann in hierarchischen
Katalogordnern organisiert werden. Beim Publish expandiert der Server die konkret gewählte
Formularversion, prüft den vollständigen Vertrag und speichert einen eigenständigen Snapshot;
`latest`, Selbst-/Zyklusreferenzen und clientseitig behauptete Bindungen sind nicht zulässig.
Die frühere Abschnitts-API bleibt als kompatibler Alias erhalten, besitzt in der Konsole aber
keinen zweiten Verwaltungsbereich mehr. Flowzer kennt dabei keine konkrete Host-Anwendung. Details stehen in
[docs/FORM-SECTIONS.md](docs/FORM-SECTIONS.md).

## BPMN-Fähigkeitsvertrag

`contracts/bpmn-capabilities/v3.json` beschreibt maschinenlesbar, welche BPMN-
Elementarten nur modellierbar beziehungsweise parsebar und welche wirklich ausführbar
sind. `GET /definition/capabilities` veröffentlicht den Vertrag; Vorabprüfung, Save und
Deploy erzwingen ihn serverseitig. Strukturierte `422`-Befunde sind im Diagramm und in
der Gliederung anwählbar. Details und bewusste Runtime-Grenzen stehen in
[docs/BPMN-CAPABILITIES.md](docs/BPMN-CAPABILITIES.md).

## KI-Verbindungen

Flowzer verwaltet revisionsgeschützte, hostneutrale Metadaten für OpenAI, Anthropic
und OpenAI-kompatible Cloud-/lokale Endpunkte. Cloud und lokale Verarbeitung sind
getrennte Installations-Opt-ins; Verwenden und Verwalten besitzen getrennte Rollen.
Secret-Referenzen sind nur schreibbar, Secret-Werte bleiben ausschließlich im
serverseitigen `IAiSecretStore`. Beim Deployment bindet Flowzer eine unveränderliche
Verbindungsrevision und ein Modell an jeden KI-Schritt. Pro wartendem Engine-Token entsteht
ein interner, persistenter Lauf; der optional aktivierte Hintergrunddienst führt ihn ohne
Provider- oder Cloud-Fallback aus und übernimmt ein erneut schema-validiertes Ergebnis
atomar in die Prozessinstanz. PostgreSQL serialisiert konkurrierende Mutationen derselben
Instanz. Typisierte Werkzeugversionen können über eine serverseitige Registry und eine
Verbindungs-Allowlist bereits sicher modelliert werden; ihr Deployment bleibt bis zum
persistenten Aktionsjournal und parametergebundenen Freigaben bewusst gesperrt.
Details: [docs/AI-CONNECTIONS.md](docs/AI-CONNECTIONS.md) und
[docs/AI-TASKS.md](docs/AI-TASKS.md).

## Release und Deployment

`main` ist der Entwicklungsstand, `release` das ausgerollte Paket; ein Release ist ein Pull Request von `main` nach `release`. Der Workflow `release.yml` baut bei jedem Push auf `release` die Images `ghcr.io/flowzer-io/flowzer-api` und `ghcr.io/flowzer-io/flowzer-console`, pinnt den Tag in Coolify und löst dort das Deployment aus (`compose.coolify.yaml`). Deploy-Zugangsdaten liegen im GitHub-Environment `maassit-production`.

## Dokumentation

- [docs/REVIEW-2026-09.md](docs/REVIEW-2026-09.md) – Review September 2026: Stand, Sofortmaßnahmen, offene Probleme, nächste Schritte
- [docs/RUNBOOK-PILOT.md](docs/RUNBOOK-PILOT.md) – Pilotbetrieb: Identity Provider, Compose-Stack, Backup, Fehlerbilder
- [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md) – ehrliche Bestandsaufnahme
- [docs/ROADMAP.md](docs/ROADMAP.md) – Vorschlag für die nächsten Schritte
- [docs/CODEBASE-AUDIT-2026-04.md](docs/CODEBASE-AUDIT-2026-04.md) – Audit-Feststellungen und Folgepakete nach der Revitalisierung
- [docs/ICORE.md](docs/ICORE.md) – dokumentierter Kernvertrag und minimaler Integrationspfad
- [docs/DEMO.md](docs/DEMO.md) – Console-Demo, Startbefehl und erwartete Ausgabe
- [docs/GLIEDERUNG-TEILMENGE.md](docs/GLIEDERUNG-TEILMENGE.md) – Gliederungsansicht neben dem Diagramm: abgedeckte BPMN-Teilmenge und wie Verluste verhindert werden
- [docs/BPMN-CAPABILITIES.md](docs/BPMN-CAPABILITIES.md) – versionierter Vertrag zwischen Modeler, Parser, Validierung und Runtime
- [docs/RUNTIME-DIAGRAM.md](docs/RUNTIME-DIAGRAM.md) – objektberechtigte, versionstreue Laufzeitprojektion und datensparsame Engine-Ereignisspur
- [docs/AI-CONNECTIONS.md](docs/AI-CONNECTIONS.md) – sichere KI-Verbindungsmetadaten, Secret-Store und Rollen
- [docs/AI-TASKS.md](docs/AI-TASKS.md) – versionierter KI-Aufgabenvertrag und bewusste Runtime-Grenze
- [docs/FORM-SECTIONS.md](docs/FORM-SECTIONS.md) – versionierte, serverseitig gebundene Formularabschnitte
- [docs/USER-TASK-DRAFTS.md](docs/USER-TASK-DRAFTS.md) – private, revisionsgeschützte Aufgabenentwürfe
- [docs/HUMAN-TASK-LIFECYCLE.md](docs/HUMAN-TASK-LIFECYCLE.md) – Übernahme, Freigabe, Zuweisung und Delegation
- [docs/HUMAN-TASK-DEADLINES.md](docs/HUMAN-TASK-DEADLINES.md) – serverseitige Fristen, Wiedervorlagen und deduplizierte Benachrichtigungen
- [packages/flowzer-sdk/README.md](packages/flowzer-sdk/README.md) – hostneutraler TypeScript-Client für Aufgaben- und Formularintegration
- [packages/flowzer-react/README.md](packages/flowzer-react/README.md) – optionale darstellungsfreie React-Hooks und Controller
- [docs/HOST-INTEGRATION.md](docs/HOST-INTEGRATION.md) – Eigentums-, Authentisierungs-, Cache- und Integrationsgrenzen
- [src/FlowzerConsole/README.md](src/FlowzerConsole/README.md) – Oberfläche: Konfiguration, lokale Starts, Aufbau
- [CONTRIBUTING.md](CONTRIBUTING.md) – Leitfaden für Beiträge über GitHub
- [AGENTS.md](AGENTS.md) – Hinweise für KI, Codex und Copilot
- [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md) – Entwicklungsprinzipien
- [.github/copilot-instructions.md](.github/copilot-instructions.md) – GitHub-Copilot-spezifische Hinweise

## Empfohlene nächste Schritte

Die sinnvolle Reihenfolge ist aktuell:

1. **Pilot starten** nach [docs/RUNBOOK-PILOT.md](docs/RUNBOOK-PILOT.md): Identity Provider registrieren, `.env` füllen, Stack hinter dem Reverse Proxy betreiben
2. **Rollen und Zuweisungen** für Aufgaben und Diagnose — für Definitionen liegt die Zuständigkeit inzwischen an den Ordnern
3. **PostgreSQL-Persistenz** hinter `IStorageSystem`
4. **Fehler-, Eskalations- und Kompensationssemantik** in der Engine

Details dazu stehen in [docs/ROADMAP.md](docs/ROADMAP.md) und [docs/REVIEW-2026-09.md](docs/REVIEW-2026-09.md).

## Minimaler `ICore`-Nutzungsweg

Für Integrationen ohne WebAPI oder Storage liegt jetzt ein bewusst kleiner Kernvertrag vor:

1. BPMN-Datei laden
2. initiale Start-Subscriptions lesen
3. Event über `BpmnNodeId` verarbeiten
4. aktive Interaktionen aus dem Ergebnis ableiten

Ein vollständiger Ablauf ist dokumentiert in [docs/ICORE.md](docs/ICORE.md).  
Eine konkrete Beispiel-Datei liegt unter [`examples/SimpleEngineExample.cs`](examples/SimpleEngineExample.cs).

## Console-Demo starten

Die Demo-Anwendung lässt sich lokal mit einem Befehl starten:

```bash
dotnet run --project src/FlowzerDemoConsole/FlowzerDemoConsole.csproj
```

Eine Schritt-für-Schritt-Erklärung und die erwartete Ausgabe stehen in [docs/DEMO.md](docs/DEMO.md).

## Oberfläche per UI-Smoke testen

Die Playwright-Smokes starten API und Konsole selbst und prüfen die Kernrouten im Browser.

```bash
dotnet build core-engine.sln --configuration Release
npm --prefix src/FlowzerConsole ci
npm --prefix tests/ui-smoke ci
npm --prefix tests/ui-smoke run install:browsers
npm --prefix tests/ui-smoke run test
```

Zusätzlich vergleicht `tests/ui-smoke/check-gateway-routes.sh` die Weiterleitungsliste des
Konsolen-Gateways mit den tatsächlichen API-Routen — fehlt dort eine Route, beantwortet die
Konsole sie mit ihrer eigenen Startseite und der Aufruf bekommt 200 statt der erwarteten Antwort.

## Lokaler Stack per Docker Compose

Für einen reproduzierbaren API-Start gibt es zusätzlich einen kleinen lokalen Compose-Stack (die Oberfläche läuft daneben mit `npm run dev`):

```bash
./scripts/local/start-stack.sh
./scripts/local/check-stack.sh
./scripts/local/stop-stack.sh
```

Weitere Betriebs- und Diagnosehinweise stehen in [docs/OPERATIONS.md](docs/OPERATIONS.md).

### Optionale OpenTelemetry-Exporter

Für produktionsnahe Umgebungen kann die Web-API die vorhandenen Signale jetzt optional an Console- oder OTLP-Exporter weiterreichen.

Beispiel:

```bash
ASPNETCORE_ENVIRONMENT=Development \
FLOWZER_STORAGE_ROOT="$(pwd)/.data/flowzer-storage" \
Observability__Enabled=true \
Observability__UseConsoleExporter=true \
Observability__OtlpEndpoint=http://localhost:4318 \
Observability__OtlpProtocol=http/protobuf \
dotnet run --project src/WebApiEngine/WebApiEngine.csproj --configuration Release
```

Welche Exporter aktiv sind, zeigt zusätzlich `GET /operations/diagnostics`.

## Timer-Scheduler im Web-API-Host

Die Web-API enthält jetzt zusätzlich einen kleinen Hintergrund-Poller für fällige Timer-Subscriptions.

Persistierte wiederkehrende Start-Timer werden dabei inklusive verbleibender Wiederholungen nachgezogen und nach einem Neustart wieder sauber auf den nächsten Fälligkeitszeitpunkt vorgeschoben.

Relevante Konfiguration:

```json
"TimerScheduler": {
  "Enabled": true,
  "PollIntervalSeconds": 5
}
```

Zusätzlich sichtbar sind Timer-Subscriptions jetzt über:

- `GET /timer`
- `GET /instance/{instanceId}/subscription/timers`

Die API-DTOs für Timer enthalten dabei jetzt auch `RemainingOccurrences`, wenn ein Start-Timer über ein BPMN-`timeCycle` mit begrenzter Wiederholung definiert wurde.

## Runtime-Container für lokale Release-Checks

Zusätzlich zur Dev-Compose-Variante gibt es jetzt auch eine runtime-nahe Containerbasis:

```bash
./scripts/runtime/start-runtime-stack.sh
./scripts/runtime/check-runtime-stack.sh
./scripts/runtime/stop-runtime-stack.sh
```

Der Runtime-Gateway-Stack ist anschließend standardmäßig unter [http://localhost:5288](http://localhost:5288) erreichbar.

## Beispiele

Ein kleines Nutzungsbeispiel der Engine-Bibliothek liegt in
[`/examples/SimpleEngineExample.cs`](examples/SimpleEngineExample.cs).

Ein vollstaendiger Prozess ueber die API — Formulare, parallele Zweige, menschliche
Entscheidungen und Service-Tasks — liegt in
[`/examples/urlaubsantrag/`](examples/urlaubsantrag/README.md). Er laesst sich mit einem
Befehl einspielen und mit dem mitgelieferten Demo-Worker durchspielen.

## Lizenz

Siehe [LICENSE](LICENSE).
