# Betriebs- und Deployment-Basis

**Stand:** 8. September 2026 (Aufgabenabschluss aktualisiert)

Dieses Dokument beschreibt den derzeit realistischen Betriebsrahmen für `main`: lokale Starts, Health-Signale, einfache Diagnose-Endpunkte, Compose-Setup und sinnvolle Prüfpfade.

> Wichtig: Das ist **noch keine produktionsfertige Deployment-Story**. Ziel dieses Pakets ist ein reproduzierbarer, dokumentierter Start- und Prüfpfad für API und Frontend.

## Enthaltene Bausteine

- dokumentierte Health-Endpunkte der Web-API
- dokumentierter Operations-/Diagnose-Endpunkt der Web-API
- lokaler Startpfad per `dotnet run`
- lokaler Startpfad per Docker Compose
- runtime-nahe Release-Container für API + Frontend + Gateway
- kleine Shell-Skripte zum Starten, Stoppen und Prüfen des lokalen Stacks
- definierter Storage-Pfad für dateibasierte Persistenz
- kleine Metrics-/Tracing-Grundlage über `Meter` und `ActivitySource`
- optionale OpenTelemetry-Exporter für Console und OTLP

## Authentifizierung (JWT Bearer / OIDC)

Abschnitt `Authentication` in `appsettings.json` bzw. per Environment-Variablen:

| Schlüssel | Bedeutung |
|---|---|
| `Authentication__Scheme` | `None` (Default, kein Schutz) oder `JwtBearer` |
| `Authentication__JwtBearer__Authority` | OIDC-Issuer, z. B. `https://login.microsoftonline.com/<tenant>/v2.0` oder `https://keycloak.example/realms/flowzer` |
| `Authentication__JwtBearer__Audience` | erwartete Audience (Client-/App-Id der API) |
| `Authentication__JwtBearer__RequireHttpsMetadata` | Default `true`; nur für lokale IdPs ohne TLS auf `false` |
| `ForwardedHeaders__KnownNetworks__0` | Netz des Reverse Proxy in CIDR-Schreibweise, z. B. `10.0.0.0/8`. Ohne Angabe werden Weiterleitungsheader ignoriert und alle anonymen Aufrufer teilen sich hinter dem Proxy ein Kontingent |
| `ForwardedHeaders__KnownProxies__0` | einzelne Proxy-Adresse, alternativ zum Netz |
| `RateLimiting__Enabled` | Default `true`; Kontingent je Aufrufer. Health-Endpunkte sind ausgenommen |
| `RateLimiting__PermitLimit` / `RateLimiting__WindowSeconds` | Default 300 Anfragen je 60 Sekunden. Gezählt wird je angemeldeter Person; ohne Anmeldung je Adresse, die nur mit gesetztem `ForwardedHeaders` hinter einem Proxy stimmt |
| `Limits__MaxUploadBytes` | Default 8 MiB, abgestimmt auf `client_max_body_size` des mitgelieferten Gateways; darüber antwortet die API 413 |
| `Authentication__JwtBearer__Roles__Modeler` | optional; Rolle für das Anlegen, Ändern und Veröffentlichen von Definitionen und Formularen. Leer heißt: für alle Zugelassenen offen |
| `Authentication__JwtBearer__Roles__Worker` | optional; Rolle für die Endpunkte unter `/job`, mit denen externe Worker Service-Tasks abholen. Leer heißt: für alle Zugelassenen offen |
| `Authentication__JwtBearer__Roles__Operator` | optional; Rolle für Diagnose, Instanzabbruch und die Sicht auf alle Aufgaben. Leer heißt: für alle Zugelassenen offen |
| `Authentication__JwtBearer__RequiredRole` | optional; Pflichtrolle für jeden Fachendpunkt. Erfüllt durch eine Keycloak-Clientrolle unter `resource_access.<Audience>.roles` oder eine Entra-App-Rolle im Claim `roles`; ohne die Rolle antwortet die API 403 |

Verhalten bei `JwtBearer`:

- Alle Endpunkte verlangen ein gültiges Token (Fallback-Policy). `GET /health` und `GET /health/ready` bleiben anonym für Orchestrator-Probes.
- Die Benutzer-Id wird aus den Claims `nameidentifier`, `sub` oder `oid` gelesen (Originalnamen, kein Inbound-Claim-Mapping) und muss eine GUID sein. Entra ID liefert `oid` als GUID, Keycloak `sub`. Andere Formate führen zu 401 auf benutzerbezogenen Pfaden. Der gültige Issuer stammt aus den OIDC-Metadaten der Authority.
- Der Development-Header `X-Flowzer-UserId` öffnet nichts mehr: Ohne Token greift die Fallback-Policy, mit Token wird der Header ignoriert.
- Fehlt `Authority` oder `Audience`, bricht der Host-Start mit einer klaren Meldung ab.

Die Konsole meldet sich über ihre zur Laufzeit geladene `config.json` (`oidcAuthority`, `oidcClientId`, `oidcAudience`, `oidcScopes`) beim selben Identity Provider an und sendet das Access-Token als Bearer an die API. Bei aktivem `JwtBearer` müssen diese Werte gesetzt sein; eine halb gefüllte Konfiguration bricht den Start der Konsole bewusst mit einer Fehlermeldung ab, statt stillschweigend ohne Anmeldung weiterzulaufen.

### Oberfläche

Es gibt genau eine: die React-Konsole (Image `flowzer-console`). Die frühere Blazor-Oberfläche
und ihr Image `flowzer-frontend` sind entfernt.

| Oberfläche | Image | Adresse bei Maass IT |
| --- | --- | --- |
| React-Konsole | `flowzer-console` | `flowzer.maass.it` |

Die Konsole richtet ihre Anzeige nach den Rollen im Token: Was eine Rolle verlangt, die jemand
nicht hat, bietet sie gar nicht erst an. Die Entscheidung trifft in jedem Fall die API — die
Oberfläche erspart nur den Weg zu einer Ablehnung. Ihr Aufbau ist in
`src/FlowzerConsole/README.md` beschrieben.

Der Umstieg ist am 6. September 2026 abgeschlossen: Der Coolify-Stack bildet nur noch den
Dienst `console` auf `https://flowzer.maass.it` ab, der Client `flowzer-maass-it` im Realm
MaassIT kennt genau drei Rückleitungen unter dieser Adresse (Anmeldung, Abmeldung, stille
Erneuerung), und das GHCR-Paket `flowzer-frontend` ist gelöscht. Die Adresse
`console.flowzer.maass.it` existiert nicht mehr.

Die Keycloak-Seite ist deklarativ in
`roles/keycloak/files/ensure-flowzer-maassit-client.sh` des Repositories
`MaassIT/Serverkonfiguration` beschrieben; ein Lauf mit `FLOWZER_KEYCLOAK_DRY_RUN=1` meldet
jede geplante Änderung, ohne zu schreiben.

### API-Vertrag

Alle JSON-Antworten tragen denselben Umschlag: `{ "successful": true, "result": …, "errorMessage": null }`. Ein Client liest Erfolg und Fehler damit an derselben Stelle, unabhängig vom Endpunkt. Ausgenommen sind bewusst nur `GET /definition/xml/{guid}`, das ein XML-Dokument liefert, und die Health-Endpunkte mit ihrem schlanken Probe-Vertrag.

Die Außenansicht liegt als Schnappschuss in `docs/openapi.json` und wird von einem Test gegen die erzeugte Beschreibung verglichen. Eine gewollte Änderung wird mit `scripts/ci/update-openapi-snapshot.sh` neu festgeschrieben und mit eingecheckt; eine ungewollte fällt in der CI auf, statt beim Client.

### Service-Tasks

Service-Tasks werden von eigenen Diensten abgearbeitet, nicht von der Engine. Der Vertrag steht in [SERVICE-TASK-WORKER.md](SERVICE-TASK-WORKER.md).

### Rollen und Zuweisungen

Vier Ebenen, die unabhängig voneinander wirken:

1. **Zugang** (`RequiredRole`): Wer Flowzer überhaupt benutzen darf. Ohne die Rolle antwortet jeder Fachendpunkt 403.
2. **Fähigkeiten** (`Roles:Modeler`, `Roles:Operator`): Wer veröffentlichen und wer den Betrieb einsehen darf. Endpunkte mit einer dieser Rollen verlangen weiterhin Anmeldung und Zugangsrolle.
3. **Zuständigkeit für einen Ordner**: Wer einen Ausschnitt des Katalogs bearbeiten und weiterreichen darf, auch ohne die Rolle fürs Modellieren. Siehe [Ordner und Delegation](#ordner-und-delegation).
4. **Zuweisung im Modell**: Welche Aufgaben eine Person sieht.

Die Aufgabenliste wertet `zeebe:assignmentDefinition` aus: `assignee`, `candidateUsers` und `candidateGroups`. Eine Aufgabe ohne jede Angabe bleibt für alle Zugelassenen sichtbar. Ist etwas angegeben, sieht sie nur, wer genannt ist oder zu einer genannten Gruppe gehört; wer die Operator-Rolle trägt, sieht alle.

Für den Abgleich zählt jede Kennung, die im Token steht: `preferred_username`, `email`, `upn`, `unique_name`, `name`, `sub`, `oid`. Gruppen kommen aus dem `groups`-Claim. Keycloak liefert Gruppen als Pfad (`/abteilungen/buchhaltung`); im Modell genügt der Gruppenname. Nennt das Modell selbst einen Pfad, muss dieser genau stimmen, damit `/extern/buchhaltung` nicht auf `/intern/buchhaltung` passt. Groß- und Kleinschreibung spielt keine Rolle, ein Teiltreffer zählt nicht.

Aufgaben, die vor der Einführung dieser Auswertung entstanden sind, tragen die Zuweisungsfelder nicht. Sie werden beim Lesen aus dem BPMN-Element im gespeicherten Token nachgezogen; eine Datenwanderung in der Ablage ist nicht nötig.

Jede Ablehnung mit 403 trägt den Header `X-Flowzer-Access-Denied`: `application` heißt, dass das Konto Flowzer nicht benutzen darf, `capability` heißt, dass nur diese eine Handlung fehlt. Die Oberfläche zeigt nur im ersten Fall den Hinweis auf die fehlende Freischaltung.

Feldbezogene Rechte innerhalb einer Aufgabe und objektbezogene Instanzprojektionen fehlen noch. Aufgaben sind anhand der Zuweisung gefiltert; Diagnose verlangt die konfigurierte Operator-Fähigkeit. Instanzdaten sind dagegen noch nicht nach Antragsteller/Bearbeiter eingeschränkt. Was jemand am Katalog *ändern* darf, richtet sich zusätzlich nach den Ordnern (nächster Abschnitt). Wer zugelassen ist, entscheidet bei gesetzter `RequiredRole` der Identity Provider über die Rollenzuweisung (bei Maass IT: Clientrolle `access` des Clients `flowzer-api`, vergeben über Gruppen im Realm `MaassIT`). Ohne `RequiredRole` genügt jedes gültige Token des Issuers, was in Realms mit Selbstregistrierung zu weit ist.

### Sicherer Aufgabenabschluss (M0-Teilpaket)

`POST /usertask` und der kompatible Altpfad `POST /form/result` verwenden denselben
`UserTaskCompletionService`. Der Body bleibt unverändert: `processInstanceId`,
`tokenId`, `flowNodeId` und optionale `data`. Akteur und Operator-Fähigkeit kommen
ausschließlich aus dem serverseitig geprüften Request-Kontext.

Im bestehenden Engine-Mutationszyklus werden Subscription, aktives menschliches
Token, Instanz-/Definitionsbindung und Zuweisung erneut geprüft. Fehlende,
mehrdeutige, bereits abgeschlossene und fremde Aufgaben liefern denselben `404`-
Umschlag (`successful: false`); fehlende Anmeldung liefert `401`. Eine fehlende
Subscription ist auch für Operatoren keine Erlaubnis. Die bestehende Operator-
Ausnahme sowie unzugewiesene, für alle Zugelassenen offene Aufgaben bleiben erhalten.
**In Produktionskonfigurationen `Roles:Operator` ausdrücklich setzen:** Ein leerer
Rollenname gewährt die Fähigkeit nach dem bisherigen Konfigurationsvertrag allen
Zugelassenen, also auch den Zugriff auf fremd zugewiesene Aufgaben.

`TokenDto.completedByUserId` und das persistierte Token enthalten den Akteur
getrennt von Formulardaten. `data.UserId` wird als Kompatibilitätswert mit dem
verifizierten Akteur überschrieben. Historische Tokens ohne diese neue Eigenschaft
bleiben lesbar (`null`/nicht vorhanden); historische Formulardaten werden **nicht**
nachträglich als verifizierter Akteur übernommen. Es ist keine Schemaänderung an
bestehenden JSON-Token-Dokumenten erforderlich.

Grenzen: Der Zyklus verwendet das vorhandene Storage-Transaktionsinterface und
eine prozesslokale Sperre. Dateiablage hat weiterhin **keinen Rollback**; der Schutz
ist kein Nachweis für mehrere API-Prozesse. Persistente Idempotenzschlüssel,
und eine append-only Audit-Historie
sind weitere M0/M6-Pakete. Instanzrechte und das begrenzte Formular-Prüfprofil
werden in eigenen Abschnitten beschrieben. Wiederholter Abschluss wird momentan mit `404`
abgelehnt, nicht als gespeicherte identische Erfolgsantwort wiederholt.

### Ordner und Delegation

Workflows liegen in einem Ordnerbaum (`/folder`, Feld `folderId` am Katalogeintrag). An jedem Ordner hängt die Zuständigkeit für alles, was darin liegt:

| Rolle am Ordner | Darf |
|---|---|
| `editor` — Bearbeiten | Workflows im Ordner anlegen, ändern, veröffentlichen, löschen und in einen anderen Ordner verschieben, für den dieselbe Person ebenfalls berechtigt ist |
| `steward` — Fachverantwortung | alles davon, zusätzlich Unterordner anlegen, den Ordner umbenennen und verschieben sowie die Zuweisungen des Ordners pflegen |

Regeln, die im Betrieb zählen:

- **Vererbung nach unten.** Eine Zuweisung gilt für ihren Ordner und für alle Unterordner. Nach unten kann sie nur stärker werden, nie schwächer — sonst ließe sich ein geerbtes Recht durch einen Unterordner aushebeln.
- **Die oberste Ebene bleibt der Rolle fürs Modellieren vorbehalten.** Sie gehört niemandem im Besonderen; wer nur einen Ordner verantwortet, soll nicht nebenbei neue Wurzeln anlegen können.
- **Die Rolle `Roles:Modeler` gilt weiterhin überall.** Ist kein Rollenname konfiguriert, ist sie für alle Zugelassenen erfüllt — dann ändert sich gegenüber der bisherigen Installation nichts, und alle Ordner stehen allen offen.
- **Lesen und Starten bleiben offen.** Ordner schränken die Sicht auf den Katalog nicht ein und verhindern auch keinen Instanzstart; sie regeln ausschließlich das Ändern.
- **Verschieben braucht beide Enden.** Ein Workflow lässt sich nur bewegen, wenn die Berechtigung sowohl im Herkunfts- als auch im Zielordner besteht.
- **Löschen nur, wenn leer.** Ein Ordner mit Unterordnern oder Workflows antwortet mit 409 und nennt die Anzahl.

Zuweisungen nennen Personen (`subjectKind: "user"`) und Gruppen (`subjectKind: "group"`) mit denselben Kennungen wie die Zuweisung im Modell — dieselbe Auswertung von `preferred_username`, `email` und `groups`, dieselbe Behandlung von Keycloak-Gruppenpfaden.

Bestehende Katalogeinträge tragen kein `folderId` und liegen damit auf der obersten Ebene; ein Umzug ist nicht nötig.

Für den Pilotbetrieb mit Identity Provider und Frontend-Anmeldung siehe [RUNBOOK-PILOT.md](./RUNBOOK-PILOT.md).

## CORS

Abschnitt `Cors:AllowedOrigins` (Array). Konfigurierte Origins werden exakt zugelassen. Ohne Konfiguration erlaubt der Development-Modus weiterhin jede Origin (Vite-Dev-Server, Playwright); alle anderen Umgebungen setzen keine CORS-Header. Hinter dem Konsolen-nginx laufen API und Oberfläche unter derselben Origin und brauchen kein CORS.

```bash
Cors__AllowedOrigins__0=https://flowzer.example.com
```

## Instanzen abbrechen

`POST /instance/{instanceId}/cancel` terminiert aktive und wartende Tokens und entfernt offene Subscriptions. Beendete Instanzen antworten mit 409, unbekannte mit 404. Der Aufruf verlangt einen aufgelösten Benutzerkontext. Eine BPMN-Kompensation bereits ausgeführter Aktivitäten findet nicht statt.

## Workflow starten

### Wiederholte HTTP-Aufrufe

Direkte Starts und Aufgabenabschlüsse können mit `Idempotency-Key` abgesichert werden.
Identische Wiederholungen liefern dasselbe Ergebnis; anderer Inhalt 409. Der Schlüssel
muss bereits beim ersten Versuch gesetzt sein, ist 1–200 sichtbare ASCII-Zeichen lang
und sieben Tage gültig. Details, PostgreSQL-Migration und Grenzen:
[HTTP-Idempotenz](IDEMPOTENCY.md).


`POST /definition/meta/{definitionId}/instance` startet eine Instanz. Der Rumpf ist optional:

```json
{ "variables": { "antragsteller": "Christian" } }
```

Ohne Rumpf startet der Workflow wie bisher ohne Angaben. Trägt sein reines Startereignis ein
Startformular (`zeebe:formDefinition/@formKey`, siehe unten), verlangt die API das
`variables`-Objekt und antwortet sonst mit 400 und
`The workflow "…" requires its start form. Send the form data as "variables".` Ein leeres
Objekt `{}` wird anschließend wie jede andere Eingabe validiert.

Pflichtwerte, Typen, statische Auswahlwerte, deklarative Sichtbarkeits- und Datumsregeln
prüft der Server im [Formular-Prüfprofil 1](FORM-VALIDATION-PROFILE.md). Ungültige Eingaben
liefern `422 application/problem+json` mit feldbezogenen Codes. Nicht unterstützte
Geschäftsregeln blockieren das Deployment, statt nur im Browser zu gelten.
Die Konsole zeigt Serverfehler unter Erhalt der Eingaben an. Vollständige Form.io-
Kompatibilität und gemeinsame Client-/Server-Konformitätsvektoren stehen noch aus.

`GET /definition/meta/{definitionId}/start-form` liefert das Startformular als
`ApiStatusResult<FormDto>` — aufgelöst über die Kennung der deployten Version, damit auch ein
im Diagramm eingebettetes Formular gefunden wird. Ohne Startformular antwortet der Endpunkt
mit **204 No Content**; ein unbekannter Workflow mit 404, ein Workflow ohne deployte Version
oder mit einem nicht auflösbaren Form-Key mit 400.

Ein Startformular gilt nur am **reinen** Startereignis unmittelbar im Prozess. An einem
Timer-, Nachrichten- oder Signalstart wird ein `formDefinition` still übergangen: bpmn-js
behält die `extensionElements`, wenn man den Ereignistyp wechselt, und ein Modell soll dadurch
nicht unspeicherbar werden. Das Startereignis eines Subprozesses zählt ebenfalls nicht — es
startet den Subprozess und nie den Workflow. Der Start über `/message` bleibt unberührt.

Genau ein Startereignis eines Prozesses darf ein Formular tragen; bei mehreren wird der Start
abgelehnt, statt eines davon zu raten.

Der Rumpf wird auch bei einem Workflow **ohne** Startformular übernommen — so lässt sich ein
Workflow von außen mit Startvariablen anstoßen, ohne dass er dafür ein Formular braucht.

## Formulare im Workflow

Ab PR #181 erhalten **alle** beim Deployment referenzierten Start-/Aufgabenformulare
einen festen Snapshot an der Definitionsversion, auch externe Formulare ohne
Versionssuffix. Eine neue Formularfassung wirkt erst mit einem neuen Workflow-Deployment.
Umbenennen oder Wiederaktivieren einer bestehenden Workflow-Version bindet nicht neu.
Nicht auflösbare Referenzen verhindern die Aktivierung; die bisher aktive Fassung bleibt.

**Upgradehinweis:** Historische externe Referenzen ohne gespeicherten Snapshot werden
bei Laufzeitabrufen nicht mehr automatisch auf `latest` aufgelöst. Neue Instanzen
brauchen ein neues Deployment; laufende Altinstanzen eine ausdrücklich geprüfte
Formularzuordnung im noch ausstehenden Migrationspaket. Eingebettete historische
Formulare bleiben aus ihrer BPMN-Version lesbar. Vor einem Upgrade solche Referenzen
inventarisieren; dieses Paket führt keine produktive Migration aus.
Siehe [Formularbindungen](FORM-DEPLOYMENT-BINDINGS.md).

Ein Formular kann aus zwei Quellen kommen. Der Form-Key
(`zeebe:formDefinition/@formKey`) sagt, aus welcher — am User-Task wie am Startereignis:

| Form-Key | Herkunft |
|---|---|
| `Urlaubsantrag` bzw. `Urlaubsantrag:1.0` | Formularbestand — geteilt über Workflows hinweg, eigene Versionen |
| `camunda-forms:bpmn:Form_…` | im Workflow selbst, als `zeebe:userTaskForm` in den `extensionElements` des Prozesses |

Ein Formular im Workflow ist mit ihm versioniert: Eine neue Workflow-Version bringt ihr
eigenes Formular mit, laufende Instanzen behalten das ihre. `GET /usertask/{id}/form`
(und für das Startformular `GET /definition/meta/{id}/start-form`)
liest es aus dem Diagramm genau der Version, an der die Aufgabe hängt, und antwortet ohne
`formId` — es steht in keinem Bestand. Zeigt der Schlüssel auf eine Kennung, die der
Workflow nicht enthält, ist das ein Modellierungsfehler und kommt als 400 mit der
gesuchten Kennung zurück.

Das Präfix ist bewusst Camundas: Ein im Camunda Modeler erstelltes Diagramm mit
eingebettetem Formular läuft ohne Umbau.

## Formulare löschen

`DELETE /form/meta/{formId}` entfernt ein Formular samt allen seinen Versionen. Der Aufruf verlangt die Modelliererrolle.

Braucht ein Workflow das Formular, antwortet die API mit 409 und nennt die betroffenen Workflows. Grund: Ein Formular wird über seinen *Namen* aufgelöst (`zeebe:formDefinition/@formKey`, wahlweise `Name:1.0`) oder über seine Kennung (`formId`). Wäre es weg, liefe jede Aufgabe dieses Schrittes in „No form named …" — und ein Startformular nähme dem Workflow den Start. Gezählt werden deshalb sowohl die menschlichen Aufgaben als auch die Startereignisse. Formulare, die im Workflow selbst liegen, stehen in keinem Bestand und sind hier deshalb nicht betroffen.

Geprüft werden zwei Dinge, und beide zählen:

- die **deployte** Fassung jedes Katalogeintrags — daraus entstehen künftige Instanzen;
- jede Fassung, auf der noch eine Instanz **läuft**. Wird eine Version abgelöst, die das Formular benutzt, warten ihre Instanzen weiter auf die Aufgabe.

Gesucht wird in allen Prozessen einer Definition, auch in Subprozessen. Ein Modell, das sich nicht lesen lässt, gilt als möglicher Benutzer und blockiert — im Zweifel zu löschen wäre die falsche Richtung.

Ein unbekanntes Formular antwortet mit 404, damit ein Löschen ins Leere nicht als Erfolg durchgeht.

## Health-Signale

Die Web-API stellt aktuell folgende Endpunkte bereit:

- `GET /health` – Liveness
- `GET /health/ready` – Readiness inkl. Storage-Prüfung
- `GET /operations/diagnostics` – Scheduler-Status, Storage-Snapshot, Instrumentierungsnamen und aktive Observability-Konfiguration

Typische URLs lokal:

- [http://localhost:5182/health](http://localhost:5182/health)
- [http://localhost:5182/health/ready](http://localhost:5182/health/ready)
- [http://localhost:5182/operations/diagnostics](http://localhost:5182/operations/diagnostics)

Der Diagnose-Endpunkt ist bewusst **pragmatisch statt vollständig**. Er liefert aktuell:

- aktuellen Environment- und Zeitstempel
- Storage-Snapshot mit Definitionen, Formularen, Instanzen und offenen Subscriptions
- Timer-Scheduler-Status inkl. letztem Tick, Fehlerstatus und verarbeiteter Timerzahl
- Namen des lokalen `Meter`- und `ActivitySource`-Setups
- Snapshot, ob Console- und/oder OTLP-Exporter aktiviert sind
- redigierte OTLP-Endpunkt- und Header-Hinweise für Betriebsprüfungen

## Lokaler Start ohne Docker

### Web-API

```bash
ASPNETCORE_ENVIRONMENT=Development \
FLOWZER_STORAGE_ROOT="$(pwd)/.data/flowzer-storage" \
dotnet run --project src/WebApiEngine/WebApiEngine.csproj \
  --configuration Release \
  --no-launch-profile \
  --urls http://localhost:5182
```

### Oberfläche

```bash
npm --prefix src/FlowzerConsole ci
npm --prefix src/FlowzerConsole run dev
```

Der Entwicklungsserver hört auf `http://localhost:5273` und leitet `/api` an die oben
gestartete API weiter. Ein anderes Ziel setzt `FLOWZER_API_URL`.

## Lokaler Start per Docker Compose

Für einen reproduzierbaren Entwicklungsstack liegt `compose.local.yml` im Repository-Root. Er startet nur die API.

### Starten

```bash
./scripts/local/start-stack.sh
```

Das Start-Skript wartet, bis die API ihren Health-Status erreicht hat. Die Oberfläche läuft daneben mit `npm --prefix src/FlowzerConsole run dev`.

Alternativ direkt:

```bash
docker compose -f compose.local.yml up -d --wait api
```

### Prüfen

```bash
./scripts/local/check-stack.sh
```

### Stoppen

```bash
./scripts/local/stop-stack.sh
```

## Runtime-nahe Containerbasis

Für lokale Release-Checks liegt zusätzlich `compose.runtime.yml` mit echten Runtime-Images und vorgeschaltetem Gateway im Repository-Root.

### Starten

```bash
./scripts/runtime/start-runtime-stack.sh
```

Das Skript baut API- und Frontend-Images, startet anschließend den Gateway-Stack und wartet auf grüne Healthchecks.

### Prüfen

```bash
./scripts/runtime/check-runtime-stack.sh
```

Typische URLs:

- [http://localhost:5288](http://localhost:5288)
- [http://localhost:5288/health](http://localhost:5288/health)
- [http://localhost:5288/health/ready](http://localhost:5288/health/ready)
- [http://localhost:5288/operations/diagnostics](http://localhost:5288/operations/diagnostics)

Bei Portkonflikten kann der Host-Port über `FLOWZER_RUNTIME_PORT` überschrieben werden.

### Stoppen

```bash
./scripts/runtime/stop-runtime-stack.sh
```

## Storage- und Dateipfade

Der Compose- und Local-Run-Pfad nutzt bewusst denselben Storage-Ort:

```text
.data/flowzer-storage
```

Dadurch bleiben Definitionen, Instanzen, Subscriptions und Formulare lokal reproduzierbar an einer bekannten Stelle liegen.

Der runtime-nahe Stack nutzt bewusst einen separaten Pfad:

```text
.data/runtime-storage
```

Damit bleiben lokale Dev-Daten und runtime-nahe Containerdaten getrennt.

### Zulässige Katalog-Kennungen

Die Kennung eines Workflows (`definitionId`) wird in der Dateiablage Teil von Dateinamen. Sie
darf deshalb nicht leer sein, nicht `.` oder `..` lauten, kein `/`, `\` und keine Steuerzeichen
enthalten und höchstens 200 Zeichen lang sein. Alles andere bleibt erlaubt — vorhandene
Kataloge mit Leerzeichen oder Umlauten in der Kennung bleiben lesbar.

Geprüft wird an jedem Eingang: `POST /definition/meta` und `PUT /definition/meta` nehmen die
Kennung aus dem Rumpf, `POST /definition` und `POST /definition/deploy` aus dem hochgeladenen
BPMN-XML (`definitions/@id`). Eine unzulässige Kennung wird mit **400** und der Meldung
`"…" is not a valid definition id. …` abgelehnt; gespeichert wird nichts.

## Logs und Diagnose

### Request- und Scheduler-Diagnose

Die Web-API protokolliert zentrale Request- und Scheduler-Signale jetzt strukturierter:

- mutierende Requests sowie langsame oder fehlerhafte API-Aufrufe werden mit Statuscode, Dauer und `TraceId` geloggt
- der Timer-Scheduler meldet Start, Tick-Erfolg, Tick-Fehler und zuletzt verarbeitete Timer
- Health-Aufrufe bleiben bewusst aus dieser zusätzlichen Request-Protokollierung ausgenommen, damit die Logs nicht mit Probe-Traffic überlaufen

### Meter-, Activity- und Exporter-Namen

Für die optionale OpenTelemetry-Anbindung sind jetzt stabile lokale Namen vorhanden:

- `Meter`: `Flowzer.WebApi`
- `ActivitySource`: `Flowzer.WebApi`

### OpenTelemetry per Konfiguration aktivieren

Die Exporter bleiben standardmäßig bewusst **deaktiviert**, damit lokale Dev- und CI-Pfade unverändert klein bleiben.

Relevante Konfiguration in `src/WebApiEngine/appsettings.json`:

```json
"Observability": {
  "Enabled": false,
  "UseConsoleExporter": false,
  "OtlpEndpoint": "",
  "OtlpHeaders": "",
  "OtlpProtocol": "grpc",
  "ServiceName": "Flowzer.WebApi"
}
```

Typische Overrides per Environment-Variablen:

```bash
Observability__Enabled=true
Observability__UseConsoleExporter=true
Observability__OtlpEndpoint=http://localhost:4318
Observability__OtlpProtocol=http/protobuf
```

Optional können zusätzlich Header für OTLP-Backends gesetzt werden:

```bash
Observability__OtlpHeaders='authorization=Bearer <token>'
```

`/operations/diagnostics` zeigt dann:

- ob Observability insgesamt aktiv ist
- ob Console-Exporter aktiv sind
- ob ein OTLP-Exporter aktiv ist
- welchen redigierten OTLP-Endpunkt die API nutzt
- welches Service-Name/-Version-Paar exportiert wird

Die OTLP-Konfiguration redigiert dabei Benutzerinformationen, Query-Parameter und Headerinhalte bewusst, damit der Diagnose-Endpunkt keine Secrets zurückspiegelt.

### Container-Logs

```bash
docker compose -f compose.local.yml logs -f api

docker compose -f compose.runtime.yml logs -f api
docker compose -f compose.runtime.yml logs -f console
docker compose -f compose.runtime.yml logs -f gateway
```

### Lokale UI-Smokes gegen laufenden Stack

Wenn API und Konsole bereits laufen, können die Playwright-Smokes gezielt gegen den bestehenden Stack ausgeführt werden:

```bash
PLAYWRIGHT_SKIP_WEBSERVERS=1 \
FLOWZER_API_URL=http://localhost:5182 \
FLOWZER_CONSOLE_URL=http://localhost:5273 \
npm --prefix tests/ui-smoke run test
```

Der `npm test`-Pfad enthält zusätzlich den Prozesswächter für verwaiste `ms-playwright`-/`chrome-headless-shell`-Prozesse.

Ohne diese Variablen startet Playwright API und Konsole selbst. Die Smokes laufen bewusst gegen
den Vite-Entwicklungsserver: Nur dort meldet die Konsole ohne Identity Provider einen
technischen Benutzer an; ein Produktionsbündel zeigte stattdessen die Anmeldeseite.

## Ablage: Dateisystem oder PostgreSQL

Abschnitt `Storage`:

| Schlüssel | Bedeutung |
|---|---|
| `Storage__Provider` | `Filesystem` (Default, JSON-Dateien unter `FLOWZER_STORAGE_ROOT`) oder `PostgreSql` |
| `Storage__PostgreSql__ConnectionString` | Laufzeitverbindung (Rolle ohne DDL) |
| `Storage__PostgreSql__MigrationConnectionString` | Verbindung mit DDL-Rechten für Migrationen; leer = Laufzeitverbindung |
| `Storage__PostgreSql__Schema` | Schema, Default `flowzer` |
| `Storage__PostgreSql__ApplyMigrationsOnStartup` | nur für einfache Umgebungen; produktiv läuft der Migrationsschritt getrennt |

PostgreSQL ist der Betriebspfad: Engine-Operationen (Deploy, Start, User-Task, Message, Timer, Abbruch) sowie das Speichern von Definitionen und Formularversionen laufen je in einer Datenbanktransaktion und werden atomar sichtbar; die übrigen Katalog- und Formular-Metadatenpfade schreiben je Aufruf in einer kurzen Transaktion. Die Dokumente werden mit derselben JSON-Serialisierung wie in der Dateiablage abgelegt; ein Wechsel zwischen beiden Ablagen ist damit ein reiner Kopiervorgang.

Migrationen liegen eingebettet in `src/PostgreSqlStorageSystem/Migrations/NNN_name.sql` und werden mit

```bash
dotnet WebApiEngine.dll --migrate
```

genau einmal angewendet (Historie in `<schema>.schema_migrations`). Im Compose-Stack übernimmt das der Dienst `migrate` vor dem Start der API. Datenbank und Rollen legt `deploy/postgresql/01-datenbank-und-rollen.sql` einmalig an (Migrations- und Laufzeitrolle getrennt).

## Recovery- und Backup-Hinweise für die dateibasierte Persistenz

Die dateibasierte Persistenz ist aktuell weiterhin die maßgebliche lokale Betriebsquelle. Für Diagnose, Backup und Restore gelten deshalb ein paar einfache Regeln:

### Nebenläufigkeit

Die Ablage kennt keine Transaktionen. Die Web-API serialisiert deshalb alle Engine-Mutationen (Deploy, Start, User-Task, Message, Timer) über eine prozessweite Sperre, schreibt Dateien atomar (Temporärdatei plus Umbenennen) und toleriert beim Lesen parallel gelöschte Dateien. Das macht einen **einzelnen API-Prozess** robust. Mehrere API-Instanzen auf derselben Ablage werden nicht unterstützt.

### Relevante Verzeichnisse

- lokale Dev-/Compose-Daten: `.data/flowzer-storage`
- runtime-nahe Containerdaten: `.data/runtime-storage`

### Sicheres Backup

Am zuverlässigsten ist ein Backup bei gestopptem Stack oder zumindest ohne parallele Schreiblast:

```bash
./scripts/local/stop-stack.sh
tar -czf flowzer-storage-backup.tgz .data/flowzer-storage
```

Für den runtime-nahen Stack entsprechend:

```bash
./scripts/runtime/stop-runtime-stack.sh
tar -czf flowzer-runtime-storage-backup.tgz .data/runtime-storage
```

### Restore

1. Stack stoppen
2. Zielverzeichnis leeren oder ersetzen
3. Backup entpacken
4. Stack neu starten
5. `/health/ready` und `/operations/diagnostics` prüfen

Beispiel lokal:

```bash
rm -rf .data/flowzer-storage
mkdir -p .data
tar -xzf flowzer-storage-backup.tgz -C .data
./scripts/local/start-stack.sh
```

### Sinnvolle Recovery-Checks nach einem Restore

- `/health/ready` liefert `Healthy`
- `/operations/diagnostics` zeigt plausible Definitionen-, Instanz- und Timer-Zahlen
- UI-Smokes gegen den laufenden Stack laufen ohne fatale Requests
- Timer-Scheduler steht nicht dauerhaft auf `Faulted`

## Bewusst noch offen

### Instanzdaten und Rollen

`Roles:Operator` muss für produktive Installationen explizit auf eine eng vergebene
Rolle gesetzt werden: Eine leere Fähigkeitsrolle ist im bestehenden Vertrag permissiv.
Die neuen Instanzansichten in PR #179 liefern ohne diese Rolle nur eine Übersicht
für den authentifizierten Initiator oder einen aktuell berechtigten Bearbeiter.
Technische Subscription-Routen und Tokenscopes bleiben der Diagnose vorbehalten.
Historische `variables.UserId`-Werte werden nicht als Besitznachweis übernommen.
Siehe [Instanzrechte](INSTANCE-ACCESS.md), insbesondere Grenzen der Formularprojektion.

### Noch fehlende Betriebspakete

Folgende Betriebsaspekte sind mit diesem Paket **noch nicht abgeschlossen**:

- strukturierte Produktions-Logformate über die Standard-Konsole hinaus
- vollständige Dashboard-/Collector-Landschaft rund um die jetzt vorhandenen OTLP-Hooks
- produktionsnahe Reverse-Proxy- oder TLS-Story
- Secret-/Configuration-Story jenseits lokaler Entwicklungswerte

## Sinnvolle nächste Ausbauschritte

1. Reverse-Proxy-/Gateway-Konfiguration für echte Zielumgebungen weiter härten
2. Collector-, Dashboard- und Alerting-Pfade auf Basis der jetzt vorhandenen Exporter ergänzen
3. Secret-/Konfigurationsstory für Nicht-Entwicklungsumgebungen schärfen
4. Reverse-Proxy-/TLS-Härtung und Backup-Automatisierung vertiefen
