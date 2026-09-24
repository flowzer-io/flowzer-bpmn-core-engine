# Runbook: Pilotbetrieb im Unternehmen

**Stand:** 8. September 2026 – BFF-Slice ist noch nicht nach `main` gemergt und
nicht als vollständiger M0-Abschluss abgenommen.

Dieses Runbook beschreibt den Zielbetrieb eines Einzelknotens hinter einem
TLS-terminierenden Reverse Proxy. Die Browser-Konsole verwendet dabei den
serverseitigen BFF; direkte/externe API-Konsumenten behalten den Bearer-Vertrag.
Produktivkonfiguration und ein konkretes Deployment brauchen weiterhin die dafür
vorgesehene Freigabe.

## Zielbild

```text
Browser ──TLS──▶ Reverse Proxy ──▶ Gateway (nginx, Port 5288)
                                      └── Konsole (nginx)
                                          ├── /bff, /definition, /instance, /usertask,
                                          │   /form, /message, /timer, /job, /operations/, /health ──▶ Web-API
                                          └── übrige Pfade ──▶ statisches Bundle
Web-API ──▶ persistenter Data-Protection-Keyring (nur API-Volume)
Web-API ──▶ Dateiablage oder PostgreSQL
Web-API ──▶ OIDC-Provider (Authorization Code + PKCE, vertraulicher Client)
Externe Clients ──Bearer──▶ Web-API
```

Die Aufteilung zwischen Oberfläche und API macht der Konsolen-Container selbst
(`deploy/console/entrypoint.sh`). Das Gateway leitet nur weiter. Der BFF setzt
`__Host-Flowzer-Session` und `__Host-Flowzer-Csrf` als `HttpOnly`/`Secure` Cookies;
deshalb ist HTTPS bis zum Browser zwingend. Der Reverse Proxy muss den originalen
Host und das HTTPS-Schema weitergeben, damit Origin-Prüfung und Callback-URL stimmen.

Das Zielbild zeigt einen API-Prozess. Mehrere API-Prozesse auf demselben PostgreSQL-Schema
sind unter den Bedingungen in [Betrieb – Mehrprozessbetrieb](OPERATIONS.md#mehrprozessbetrieb)
freigegeben; maßgeblich ist jener Abschnitt. Coolify nutzt PostgreSQL; die dateibasierte
Ablage bleibt ein lokaler Einzelprozesspfad.

## 1. Voraussetzungen

- Docker mit Compose auf dem Zielhost sowie ein Reverse Proxy mit gültigem TLS-
  Zertifikat, der auf das Gateway weiterleitet
- OIDC-Provider mit einem **vertraulichen** Client für die Flowzer-Web-API
- API-Audience und gegebenenfalls Rollen-/Audience-Mapper
- persistentes Storage-Volume sowie ein getrenntes, nur vom API-Container
  beschreibbares Data-Protection-Volume
- ein Secret-Store, aus dem `FLOWZER_BFF_CLIENT_SECRET` nur zur Prozesslaufzeit
  in die API-Umgebung injiziert wird; niemals in `.env`, Git, Logs oder Browser

## 2. Identity Provider einrichten

### Entra ID

1. API-Registrierung anlegen, Application ID URI `api://<api-client-id>` und Scope
   `access_as_user` einrichten. Die API-Audience muss mit
   `FLOWZER_AUTH_AUDIENCE` übereinstimmen.
2. Einen **Web-/vertraulichen Client** für den BFF registrieren. Ausschließlich die
   Redirect-URI `https://<flowzer-host>/bff/signin-oidc` hinterlegen (Ausnahme nur für
   den Provider-Logout, siehe Schritt 5). Ein Client-
   Secret im Secret-Store speichern, nicht in der Konsole.
3. Dem vertraulichen Client die delegierte API-Berechtigung `access_as_user` geben
   und erforderlichen Admin-Consent erteilen.
4. Authority: `https://login.microsoftonline.com/<tenant-id>/v2.0`. Flowzer benötigt
   eine GUID aus `nameidentifier`, `sub` oder `oid`; für Entra ist üblicherweise
   `oid` die passende GUID.
5. Nur wenn die Abmeldung auch die Entra-Sitzung beenden soll
   (`FLOWZER_BFF_PROVIDER_LOGOUT=true`): `https://<flowzer-host>/` zusätzlich als
   Redirect-URI der Plattform Web eintragen; Entra akzeptiert nur registrierte
   Adressen als `post_logout_redirect_uri`. Die API-Audience muss eine eigene
   API-Registrierung (Schritt 1) sein, nicht die Client-ID des BFF; sonst startet die API
   mit dem Schalter nicht. Dieser Weg ist nicht durch die Abnahme belegt.

### Keycloak

1. Im Ziel-Realm einen Client für den BFF als **confidential** anlegen, Standard Flow
   und PKCE aktivieren, Redirect-URI exakt auf
   `https://<flowzer-host>/bff/signin-oidc` begrenzen. Das Client-Secret im
   Secret-Store halten.
2. Einen Audience-Mapper einrichten, der `flowzer-api` in das Access-Token schreibt;
   dann lautet `FLOWZER_AUTH_AUDIENCE=flowzer-api`. Rollen bei Bedarf unter
   `resource_access.flowzer-api.roles` ausgeben.
3. Authority: `https://<keycloak-host>/realms/<realm>`. Die Benutzer-ID muss als
   GUID im `sub`-Claim vorliegen.
4. Soll die Abmeldung auch die Keycloak-Sitzung beenden, am BFF-Client unter
   *Valid post logout redirect URIs* (Attribut `post.logout.redirect.uris`) genau
   `https://<flowzer-host>/` eintragen und danach `FLOWZER_BFF_PROVIDER_LOGOUT=true`
   setzen. Ohne Eintrag zeigt Keycloak bei der Abmeldung eine Fehlerseite; ohne den
   Schalter bleibt die Abmeldung lokal und die SSO-Sitzung bestehen (Details in
   [OPERATIONS.md](OPERATIONS.md#abmeldung-und-provider-logout)).
5. Die Access-Token-Laufzeit (Realm oder Client, „Access Token Lifespan“) höchstens auf
   5 Minuten stellen, wie im Keycloak-Standard: Ein Rollenentzug wirkt in laufenden
   Sitzungen erst mit der nächsten Token-Erneuerung (siehe `docs/OPERATIONS.md`,
   „Sitzungsdauer und Erneuerung“).

Keine SPA-Registrierung, keine Browser-Client-ID, keine `FLOWZER_OIDC_*`-Variablen
und keine stille Browser-Token-Erneuerung konfigurieren.

## 3. Konfigurieren

```bash
cp .env.example .env
```

`.env` enthält nur nicht geheime Bereitstellungswerte:

| Variable | Bedeutung |
|---|---|
| `FLOWZER_RUNTIME_BIND_ADDRESS` | Bindeadresse des lokalen Gateways; sicherer Default `127.0.0.1`, nur für einen bewusst angebundenen externen Container-Proxy öffnen |
| `FLOWZER_RUNTIME_PORT` | Host-Port des Gateways (Default 5288) |
| `FLOWZER_AUTH_SCHEME` | `Bff` für diesen Betrieb; `None` nur lokal, `JwtBearer` für direkte Bearer-Kompatibilität |
| `FLOWZER_BFF_ENABLED` | `true` zusammen mit `Bff`; nur beim lokalen `None`-HTTP-Modus ebenfalls auf `false` setzen |
| `FLOWZER_AUTH_AUTHORITY` | OIDC-Issuer der API und des BFF |
| `FLOWZER_AUTH_AUDIENCE` | erwartete Audience im Access-Token |
| `FLOWZER_BFF_CLIENT_ID` | Client-ID des vertraulichen BFF-Clients |
| `FLOWZER_BFF_SCOPE_0` bis `_2` | zusätzliche Scopes, bei Entra typischerweise der API-Scope |
| `FLOWZER_BFF_PROVIDER_LOGOUT` | Default `false`; `true` beendet bei der Abmeldung auch die SSO-Sitzung beim Identity Provider, setzt die registrierte Post-Logout-Redirect-URI voraus |
| `FLOWZER_BFF_POST_LOGOUT_PATH` | Default `/`; lokaler Pfad, auf den der Identity Provider nach der Abmeldung zurückleitet |
| `FLOWZER_TRUSTED_PROXY_NETWORK` | privates CIDR, aus dem die API Forwarded-Header akzeptiert; muss zum tatsächlichen Container-Netz passen |
| `FLOWZER_FORWARDED_HEADER_LIMIT` | Zahl der vertrauenswürdigen Proxy-Stufen; Default `3` für TLS-Proxy, Gateway und Konsolen-nginx |
| `FLOWZER_ACCENT` | globale Akzentfarbe der Konsole |

`FLOWZER_BFF_CLIENT_SECRET` wird **nicht** in `.env` eingetragen: vor dem Start aus
dem Secret-Store in die Umgebung des API-Containers injizieren. Der Compose-Stack
reicht ihn ausschließlich als `Authentication__Bff__ClientSecret` an `api` weiter.
Die Konsole erhält keine OIDC-/Rollen-/Secret-Variablen und `config.json` enthält nur
API-Basis, Akzent und BFF-Schalter.

Der TLS-Proxy muss eingehende `X-Forwarded-For`- und `X-Forwarded-Proto`-Werte
ersetzen, den externen Host **einschließlich eines Nichtstandardports** weitergeben
und selbst im konfigurierten Vertrauensnetz liegen. Gateway und Konsolen-nginx
erhalten Schema und Host unverändert. Ein breiteres Vertrauensnetz oder ein höheres
Forward-Limit als die tatsächliche Kette würde dagegen fälschbare Clientdaten
akzeptieren.

Das Runtime-Gateway bindet standardmäßig nur an `127.0.0.1`. So kann ein direkter
Netzwerkclient keinen Forwarded-Header am TLS-Proxy vorbei einschleusen. Liegt der
TLS-Proxy in einem anderen Container, muss dessen gemeinsames privates Netz bevorzugt
werden; eine abweichende `FLOWZER_RUNTIME_BIND_ADDRESS` ist nur zusammen mit einer
Firewallregel und einem Proxy zulässig, der Client-Header zuverlässig ersetzt.

Der Runtime-Stack legt den Keyring unter `.data/runtime-data-protection` an. Dieses
Verzeichnis nicht löschen, teilen oder in Backups vergessen; sonst verlieren alle
bestehenden BFF-Sitzungen, OIDC-Korrelationen und Antiforgery-Token ihre Gültigkeit.

## 4. Bauen und starten

Vor dem Start muss der Reverse Proxy für eine HTTPS-Adresse eingerichtet sein. Der
mitgelieferte lokale Gateway-Port ist für Healthchecks geeignet; über reines HTTP
funktionieren `Secure`-Cookies absichtlich nicht.

```bash
./scripts/runtime/start-runtime-stack.sh
./scripts/runtime/check-runtime-stack.sh
```

Manuell:

```bash
docker compose -f compose.runtime.yml build
docker compose -f compose.runtime.yml up -d --wait
```

## 5. Prüfen

0. `dotnet WebApiEngine.dll --check-config` liefert eine Tabelle ohne Fehlerzeile
   (Exit-Code 0; 2 bedeutet „startbar, aber Hinweise klären“, 1 „nicht startbereit“).
1. `curl -s https://<flowzer-host>/health/ready` liefert `"Status":"Healthy"` und unter
   `details` die konfigurierte Ablage samt `"migrationState":"UpToDate"`.
2. `curl -s -o /dev/null -w '%{http_code}' https://<flowzer-host>/definition/meta`
   liefert **401**, solange keine Sitzung/Bearer vorhanden ist.
3. Browser auf `https://<flowzer-host>/` öffnen: Die Konsole leitet über
   `/bff/login` zum Identity Provider, danach erscheint die Sitzung in der Konsole.
4. Einen schreibenden Fachvorgang ausführen; der Browser sendet dazu automatisch
   `X-Flowzer-CSRF`. Ein Cross-Origin-POST oder POST ohne Header muss mit 400
   abgewiesen werden.
5. Einen direkten Diagnoseaufruf mit einem autorisierten externen Bearer prüfen:

   ```bash
   curl -s -H "Authorization: Bearer <token>" https://<flowzer-host>/operations/diagnostics
   ```

   Der Bearer-Aufruf bleibt ohne CSRF-Header gültig. Einen absichtlich ungültigen
   Bearer bei bestehender Browser-Sitzung als 401 prüfen; er darf nicht auf Cookie
   zurückfallen.

Diese Prüfungen sind für einen Keycloak-Aufbau als reproduzierbare Abnahme automatisiert:
`tests/installation-auth/run.sh` baut API und Konsole aus dem Repository, startet sie mit
eigener Test-CA und isoliertem Keycloak (synthetischer Realm, ohne Produktionszugang) und
prüft `--check-config`, Anmeldung über den BFF, Cookie- und CSRF-Vertrag, Rollen- und
Audience-Ablehnungen, den Verzeichnisabgleich sowie einen API-Neustart. Ergebnis, Grenzen und
beobachtete Abweichungen des letzten Laufs stehen in
[docs/acceptance/auth.md](acceptance/auth.md); die Abnahme ersetzt nicht die Prüfung gegen
den tatsächlichen Identity Provider der Zielumgebung.

## 6. Betrieb

### Backup und Restore

Zwei Skripte decken beide Ablagen ab. Sie schreiben nach `./backups` (anpassbar über
`FLOWZER_BACKUP_DIR` oder `--out`) und nehmen Zugangsdaten **nur** über die Umgebung
entgegen: `STORAGE_MIGRATION_CONNECTION_STRING`, ersatzweise `STORAGE_CONNECTION_STRING`,
oder `--connection`. In der Prozessliste des Hosts steht dadurch nie ein Passwort; intern
werden `PGPASSWORD`/`PGHOST`/`PGUSER` gesetzt, alternativ greift `~/.pgpass`. Fehlen
`pg_dump`/`pg_restore`/`psql` lokal, laufen sie in einem Wegwerf-Container
(`FLOWZER_PG_IMAGE`, Standard `postgres:17-alpine`; bei einer Datenbank im Compose-Netz
zusätzlich `FLOWZER_PG_DOCKER_NETWORK`). Sind die lokalen Clientwerkzeuge **älter** als der
Server, verweigert `pg_dump` die Arbeit; dann `FLOWZER_PG_CLIENT=docker` setzen (erzwingt den
Container, `local` erzwingt die lokalen Werkzeuge).

**Sichern** – Stack vorher stoppen oder zumindest ohne Schreiblast fahren:

```bash
./scripts/runtime/stop-runtime-stack.sh
export STORAGE_MIGRATION_CONNECTION_STRING='Host=…;Port=5432;Database=…;Username=…;Password=…'
export FLOWZER_APP_VERSION="$FLOWZER_IMAGE_TAG"   # optional, landet in der .meta
./scripts/runtime/backup.sh --schema flowzer
```

Ergebnis je Lauf (`<ts>` = UTC-Zeitstempel):

| Datei | Inhalt |
|---|---|
| `<ts>.dump` | `pg_dump -Fc`, nur das Flowzer-Schema, ohne Eigentümer- und Rechtezuweisungen. Wird erst in `<ts>.dump.tmp` geschrieben, mit `pg_restore --list` geprüft (lesbar, enthält `schema_migrations` samt Daten) und dann umbenannt; bei einem Abbruch bleibt nichts liegen |
| `<ts>.dump.sha256` | SHA-256 im Format von `sha256sum`; von Hand prüfbar mit `sha256sum -c` bzw. `shasum -a 256 -c` im Sicherungsverzeichnis |
| `<ts>-files.tgz` und `.sha256` | Dateiablage **und** Data-Protection-Keyring, sofern vorhanden, jeweils unter ihrem Verzeichnisnamen |
| `<ts>.meta` | Herkunft und Stand: `host`, `port`, `database`, `schema`, `postgres_server_version`, `pg_dump_version`, `app_version` (`FLOWZER_APP_VERSION`, ersatzweise `FLOWZER_IMAGE_TAG`, sonst `unknown`), `schema_migrations_count`/`_max` sowie die absoluten Quellpfade `files_storage_dir`/`files_keyring_dir`. Wird zuletzt geschrieben |

`FLOWZER_STORAGE_DIR` und `FLOWZER_KEYRING_DIR` dürfen absolut sein (etwa der Pfad eines
Volumes); relative Angaben gelten relativ zur Repository-Wurzel, Standard
`.data/runtime-storage` und `.data/runtime-data-protection`. Der Keyring gehört zwingend in
dieselbe Sicherung: Ohne ihn verlieren nach einem Restore alle BFF-Sitzungen,
OIDC-Korrelationen und Antiforgery-Token ihre Gültigkeit. Wer nur die Dateiablage betreibt,
ruft `--files-only` auf; wer nur die Datenbank sichert, `--no-files`. Die Sicherung ist nur
vollständig, wenn Dump, `.sha256` und `.meta` zusammen weggesichert werden.

**Zurückspielen in eine andere Datenbank** (Klon, Test, Umzug) – Stack gestoppt, Ziel ist
eine mit `deploy/postgresql/01-datenbank-und-rollen.sql` angelegte (oder eine leere) Datenbank.
Auf demselben Host liegen die Dateien der Quellinstallation noch an ihren Pfaden; deshalb
gehört `--files-root` dazu, sonst bricht das Skript am Dateikonflikt ab (Prüfung 3):

```bash
export STORAGE_MIGRATION_CONNECTION_STRING='Host=…;Database=<ziel>;Username=<migrationsrolle>;Password=…'
./scripts/runtime/restore.sh backups/20260919T043206Z.dump --schema flowzer \
  --runtime-role <laufzeitrolle> \
  --files backups/20260919T043206Z-files.tgz --files-root /srv/flowzer-restore
```

**Zurückspielen in die Originaldatenbank** (Rückweg nach Datenverlust oder gescheitertem
Update) – Stack gestoppt; das Schema ist belegt und die Dateien liegen an ihren Pfaden, also
sind alle drei Schalter nötig, und die Rechte-Neuvergabe läuft über `--runtime-role` gleich mit:

```bash
export STORAGE_MIGRATION_CONNECTION_STRING='Host=…;Database=<original>;Username=<migrationsrolle>;Password=…'
./scripts/runtime/restore.sh backups/20260919T043206Z.dump --schema flowzer \
  --allow-same-database --force --runtime-role <laufzeitrolle> \
  --files backups/20260919T043206Z-files.tgz --overwrite-files
```

Das Skript arbeitet in dieser Reihenfolge. Die Prüfungen 1 bis 4 laufen vollständig, bevor
es das Ziel verändert; jeder Befund dort ist ein Abbruch ohne Änderung:

1. **Prüfsumme.** `<dump>.sha256` (und `<archiv>.sha256`) müssen passen. Fehlt die Datei,
   gibt es eine Warnung; mit `--require-checksum` ist das ein Abbruch.
2. **Ziel ≠ Quelle.** Nennt die `.meta` denselben Host und Datenbanknamen wie das Ziel,
   verweigert das Skript den Restore – auch mit `--force`. Nur `--allow-same-database` lässt
   das bewusst zu, etwa beim Zurückspielen in die Originaldatenbank nach einem Datenverlust.
   Der Vergleich ist rein textuell: Wer dieselbe Datenbank über einen anderen Namen oder eine
   IP-Adresse anspricht, wird nicht erkannt. Fehlt die `.meta`, verlangt `--force`
   zusätzlich `--allow-same-database`.
3. **Dateien.** `--files` spielt Dateiablage und Keyring an die absoluten Pfade aus der
   `.meta` zurück – auf demselben Host sind das die Pfade der Quellinstallation. Würde dabei
   eine vorhandene Datei ersetzt, bricht das Skript ab; `--files-root <verzeichnis>`
   (Ziel `<verzeichnis>/<name>`) wählt einen anderen Ort, `--overwrite-files` erlaubt das
   Ersetzen bewusst. Zusätzliche Dateien im Ziel bleiben liegen.
4. **Leeres Ziel.** Als leer gilt auch ein Schema, in dem nur die vom Rollenskript vorab
   angelegte, leere `schema_migrations` liegt; sie wird vor dem Restore entfernt. Enthält das
   Schema Daten, bricht das Skript ab; `--force` verwirft es vorher per
   `DROP SCHEMA … CASCADE`. Dieses Verwerfen ist eine eigene Transaktion **vor** dem
   Einspielen: Scheitert Schritt 5 danach, ist der alte Zielbestand bereits weg und nur ein
   leeres Schema übrig. Der Lauf lässt sich dann wiederholen; der alte Bestand kommt nur aus
   einer Sicherung zurück. `--force` ist also bewusst zerstörend.
5. **Einspielen** mit `pg_restore --single-transaction --exit-on-error`: Scheitert ein
   Objekt, bleibt vom Dump nichts halb eingespielt (das Schema ist dann leer, siehe 4).
6. **Rechte der Laufzeitrolle neu vergeben** – siehe unten.
7. **Abschlussprüfung.** Migrationsstand des Ziels = Stand in der `.meta`; die
   Laufzeitrolle hat `USAGE` auf dem Schema, `SELECT/INSERT/UPDATE/DELETE` auf allen Tabellen
   und auf `schema_migrations` nur `SELECT`. Ist `FLOWZER_RUNTIME_PASSWORD` gesetzt, liest das
   Skript zusätzlich über eine eigene Verbindung als Laufzeitrolle. Jede Abweichung endet mit
   Exit 1.

**Rechte nach dem Restore neu vergeben – Pflichtschritt.** Schema-Rechte und
Default-Privileges hängen am Schema selbst. `--force` wirft sie mit dem Schema weg (danach
meldet die API `permission denied for schema`), und ein Restore in ein vom Rollenskript
vorbereitetes Schema gibt `schema_migrations` über die Default-Privileges mehr als `SELECT`.
`restore.sh` führt deshalb nach dem Einspielen `deploy/postgresql/02-laufzeitrechte.sql` mit
der Migrationsverbindung aus, wenn die Laufzeitrolle bekannt ist (`--runtime-role` oder
`FLOWZER_RUNTIME_ROLE`). Ohne sie warnt das Skript deutlich und nennt den Befehl; dann den
Schritt **vor dem Start der API** von Hand nachholen, verbunden mit der Zieldatenbank als
Migrationsrolle oder Superuser:

```bash
psql -h <host> -p 5432 -U <migrationsrolle> -d <zieldatenbank> \
  -v migrationsrolle=<migrationsrolle> -v laufzeitrolle=<laufzeitrolle> -v schema=flowzer \
  -f deploy/postgresql/02-laufzeitrechte.sql
```

Ohne `-d` trifft `psql` die Voreinstellung des Aufrufers (meist die Datenbank `postgres`),
nicht das Restore-Ziel; das Skript gibt den Befehl deshalb mit Verbindungsangaben aus.

Das Skript ist idempotent; `01-datenbank-und-rollen.sql` bindet es beim Einrichten selbst ein.

Danach:

```bash
dotnet WebApiEngine.dll --migrate        # nur wenn das Paket neuer ist als die Sicherung
dotnet WebApiEngine.dll --check-config   # „Migrationen OK aktuell“, Ablage mit der Laufzeitkennung OK
./scripts/runtime/start-runtime-stack.sh # danach /health/ready prüfen
```

Der Ablauf ist als Skripttest automatisiert (`scripts/runtime/tests/backup-restore.test.sh`,
CI-Job `backup_restore_scripts`); Ergebnis und Grenzen stehen in
[docs/acceptance/restore.md](acceptance/restore.md).

**Aufbewahrung.** Die Skripte legen nur ab und löschen nichts. Wie lange Sicherungen liegen
bleiben, entscheidet die Aufbewahrungsregel der Installation (#325). Wichtig dabei: Eine
Sicherung, die **vor** Ablauf einer Aufbewahrungsfrist entstanden ist, enthält die
inzwischen gelöschten Vorgänge weiterhin. Sicherungen unterliegen deshalb derselben Frist
wie der Produktivbestand und sind am Ende der Frist zu vernichten; `backups/` ist aus
demselben Grund über `.gitignore` ausgeschlossen und wird mit `chmod 700` angelegt, die
Dateien darin mit `umask 077`.

### Logs und Diagnose

```bash
docker compose -f compose.runtime.yml logs -f api
docker compose -f compose.runtime.yml logs -f console
docker compose -f compose.runtime.yml logs -f gateway
```

Logs nie mit `Authentication__Bff__ClientSecret`, Authorization-Headern, Cookies
oder CSRF-Request-Tokens teilen. `GET /operations/diagnostics` verlangt im BFF-
Betrieb Sitzung oder autorisierten Bearer; OpenTelemetry-Export ist über
`Observability__*` aktivierbar (siehe `docs/OPERATIONS.md`).

### Aktualisieren

Laufende Instanzen überstehen ein Update: Schema-Migrationen sind Vorwärtsmigrationen und
lassen wartende Aufgaben, Aufträge und Timer stehen. Belegt ist das durch
`Upgrade_ShouldKeepRunningInstancesUsableAcrossAllMigrations`
(`src/WebApiEngine.Tests/PostgreSqlStorageIntegrationTest.UpgradeWithRunningInstances.cs`):
Instanzen, die auf dem Schemastand 012 oder 019 mit allen drei Wartezuständen gespeichert
wurden, laufen nach dem vollständigen `--migrate`-Schritt (Migrationen und
Formularbindungs-Upgrade) unverändert weiter.

Reihenfolge – **erst Migration, dann Replikate**:

```bash
# 1. Sichern (siehe „Backup und Restore“); Exit 0 heißt: Dump geprüft, .sha256 und .meta liegen
FLOWZER_APP_VERSION="<bisheriger Image-Tag>" ./scripts/runtime/backup.sh --schema flowzer

# 2. Neues Paket bauen
git pull
docker compose -f compose.runtime.yml build

# 3. Konfiguration des neuen Stands prüfen, bevor etwas gestartet wird
dotnet WebApiEngine.dll --check-config

# 4. Migration als eigener, einmaliger Schritt
dotnet WebApiEngine.dll --migrate

# 5. Erst jetzt die API-Replikate auf das neue Paket heben
docker compose -f compose.runtime.yml up -d --wait
```

`--check-config` meldet vor Schritt 4 „n ausstehend“ und nach Schritt 4 „aktuell“; nach
Schritt 5 zeigt `GET /health/ready` denselben Stand unter `details.migrationState`. Der
Migrationsschritt nimmt für den ganzen Lauf einen Advisory-Lock und wendet alle ausstehenden
Migrationen in einer Transaktion an; zwei gleichzeitige Läufe kommen sich also nicht in die
Quere, und ein zweiter Lauf wendet nichts erneut an. Im Coolify-Stack erledigt das der Dienst
`migrate`, der vor `api` laufen muss (`condition: service_completed_successfully`).

Scheitert das Update, ist der Rückweg der Restore der Sicherung aus Schritt 1 **mit dem
bisherigen Paket** (Abschnitt „Backup und Restore“, Beispiel „Zurückspielen in die
Originaldatenbank“). In die Produktionsdatenbank zurückzuspielen verlangt bei gestopptem Stack
`--allow-same-database` und – weil das Schema belegt ist – `--force`; dazu `--runtime-role`,
damit die Rechte der Laufzeitrolle gleich neu vergeben werden (sonst nur eine Warnung mit dem
Nachholbefehl), und bei Dateien `--files … --overwrite-files`, weil Ablage und Keyring an
ihren Pfaden liegen. Genau diesen Aufruf prüft der Skripttest (Fall „Rückweg in die Quelle“).

Vor einem Update Storage und Keyring sichern. Das Keyring-Volume behalten, damit ein
Redeploy nicht alle Sitzungen und OIDC-Korrelationen ungültig macht.

## 6b. Variante Coolify mit GitHub Container Registry

Für Maaß IT verwendet `compose.coolify.yaml` PostgreSQL und ein benanntes,
ausschließlich an `api` gemountetes Volume `flowzer-bff-data-protection`. Der
Release-Workflow baut Images bei einem Push auf `release`, pinnt
`FLOWZER_IMAGE_TAG` und löst anschließend das Deployment aus. Ein Feature-PR allein
ist kein Deployment.

Coolify benötigt mindestens `STORAGE_CONNECTION_STRING`,
`STORAGE_MIGRATION_CONNECTION_STRING`, `FLOWZER_AUTH_AUTHORITY`,
`FLOWZER_AUTH_AUDIENCE`, `FLOWZER_BFF_CLIENT_ID` und
`FLOWZER_BFF_CLIENT_SECRET`. Letzteres ist als Coolify-Secret zu pflegen. Optional
sind `FLOWZER_BFF_SCOPE_0` bis `_2`, Rollen, Storage-Schema und Image-Tag. Es gibt
keine `FLOWZER_OIDC_*`- oder Konsolen-Secret-Variablen.

## 7. Bekannte Grenzen des Piloten

- Zugangs-, Modeler-, Operator- und Worker-Rolle müssen bei `JwtBearer`/`Bff` gesetzt
  sein; fehlt ein Name, startet die API nicht (fail-closed). Nur Bestandsinstallationen
  dürfen mit `Authentication__JwtBearer__LegacyPermissiveRoles=true` ausdrücklich beim
  alten Verhalten bleiben – dann erhält jede angemeldete Person die Fähigkeit des
  fehlenden Namens, und `--check-config` warnt in der Zeile `Rollen`.
- Mehrprozessbetrieb ist ausschließlich mit PostgreSQL und unter den Bedingungen in
  [Betrieb](OPERATIONS.md#mehrprozessbetrieb) freigegeben; die Dateiablage bleibt
  Einzelprozess.
- Recovery, Fehler-/Eskalations-/Kompensationssemantik, Alarmierung und vollständige
  Secret-Store-/TLS-Automatisierung bleiben weitere Pakete.
- Sicherung und Wiederherstellung sind Momentaufnahmen: Es gibt **kein**
  Point-in-Time-Recovery und keine automatische Aufbewahrungs- oder Löschregel. Beides
  entscheidet der Datenbankbetrieb der Installation. Die Skripte sind per Skripttest gegen
  einen Wegwerf-Container belegt, nicht durch eine Übung gegen die Produktionsumgebung.
- `backup.sh`/`restore.sh` werden **nicht** von Coolify aufgerufen. Im Produktivbetrieb
  ist der Aufruf zu planen (Cron/Systemd-Timer) und das Zielverzeichnis vom Host
  wegzusichern; die Skripte selbst kopieren nichts an einen zweiten Ort.
- `--check-config` prüft Erreichbarkeit, nicht Berechtigung: Aus der OIDC-Discovery liest
  es nur `issuer` und `token_endpoint`; fehlt eines, warnt es. Ein Issuer, der von der
  Authority abweicht, ist nur ein Hinweis (Entra `common`/`organizations`, Proxy), weil zur
  Laufzeit der Issuer aus den Metadaten gilt. Das belegt nicht, dass Client-Secret, Scopes und
  Audience zusammenpassen. Ein nicht erreichbarer Identity Provider ist deshalb eine
  Warnung, kein Fehler.
- Ein Rückwärts-Update (älteres Paket auf neueres Schema) ist nicht vorgesehen; es gibt
  keine Abwärtsmigrationen. Der Rückweg ist der Restore einer Sicherung.

## 8. Fehlerbilder

| Symptom | Ursache | Abhilfe |
|---|---|---|
| API startet nicht mit BFF-Konfigurationsfehler | Authority, Audience, Client-ID, Secret oder Keyring-Pfad fehlt | BFF-Werte und Secret-Injektion nur am API-Container prüfen |
| Browser bleibt nach Login abgemeldet | URL ist HTTP oder Proxy meldet nicht HTTPS/Host weiter | TLS, `X-Forwarded-Proto` und Host-Weitergabe prüfen; `Secure`-Cookies sind unter HTTP absichtlich unwirksam |
| Schreibaufruf liefert 400 `CSRF validation failed` | Header/Origin fehlt oder Request-Token ist abgelaufen | Sitzung/`GET /bff/csrf` erneuern; nur same-origin schreiben |
| Externer Client erhält 401 | Bearer trägt nicht die erwartete Audience oder ist ungültig | Audience/Issuer/Claims prüfen; der BFF ersetzt den externen Bearer-Vertrag nicht |
| Nach Redeploy sind alle Sitzungen ungültig | Data-Protection-Keyring wurde nicht persistent übernommen | getrenntes Keyring-Volume wiederherstellen und künftig sichern |
| `docker compose build` scheitert am SDK | Falsches Feature-Band | Images nutzen `sdk:10.0.103`; `global.json` verlangt 10.0.1xx |
| API meldet nach einem Restore `permission denied for schema` bzw. `/health/ready` zeigt `migrationState` `Unknown` | Rechte der Laufzeitrolle nicht neu vergeben (Restore ohne `--runtime-role`, v. a. nach `--force`) | `deploy/postgresql/02-laufzeitrechte.sql` mit der Migrationsrolle ausführen (Abschnitt „Backup und Restore“) |
| `restore.sh`: „ist die Quelle dieser Sicherung“ | Zielverbindung zeigt auf die gesicherte Datenbank | Zielverbindung prüfen; nur für einen bewussten Restore in die Originaldatenbank `--allow-same-database` |
| `restore.sh`: Prüfsumme „weicht ab“ | Dump beim Kopieren beschädigt oder verändert | Sicherung erneut vom Sicherungsziel holen; nie mit einer beschädigten Datei weiterarbeiten |
| `backup.sh`: `server version mismatch` | lokale PostgreSQL-Clients älter als der Server | `FLOWZER_PG_CLIENT=docker` setzen |
