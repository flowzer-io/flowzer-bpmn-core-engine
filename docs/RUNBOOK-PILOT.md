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

Ein API-Prozess, eine Ablage. Mehrere API-Instanzen auf derselben Ablage sind nicht
unterstützt. Coolify nutzt PostgreSQL; die dateibasierte Ablage bleibt ein lokaler
Einzelprozesspfad.

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
   Redirect-URI `https://<flowzer-host>/bff/signin-oidc` hinterlegen. Ein Client-
   Secret im Secret-Store speichern, nicht in der Konsole.
3. Dem vertraulichen Client die delegierte API-Berechtigung `access_as_user` geben
   und erforderlichen Admin-Consent erteilen.
4. Authority: `https://login.microsoftonline.com/<tenant-id>/v2.0`. Flowzer benötigt
   eine GUID aus `nameidentifier`, `sub` oder `oid`; für Entra ist üblicherweise
   `oid` die passende GUID.

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

1. `curl -s https://<flowzer-host>/health/ready` liefert `"Status":"Healthy"`.
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

## 6. Betrieb

### Backup und Restore

Bei Dateiablage täglich Storage **und** den Data-Protection-Keyring sichern:

```bash
tar -czf flowzer-runtime-backup-$(date +%F).tgz \
  .data/runtime-storage .data/runtime-data-protection
```

Für Restore Stack stoppen, beide Verzeichnisse konsistent zurückspielen und erst dann
starten. Eine Wiederherstellung ohne Keyring invalidiert Sessions und OIDC-
Korrelationen; das ist sicherer als Schlüssel neu zu erzeugen, aber im Runbook als
beabsichtigter Logout zu behandeln. PostgreSQL-Backups folgen dem Datenbankbetrieb;
der Keyring bleibt trotzdem ein separates Volume.

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

```bash
git pull
docker compose -f compose.runtime.yml build
docker compose -f compose.runtime.yml up -d --wait
```

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

- Der BFF-Slice ist noch nicht nach `main` gemergt und kein vollständiger
  M0-/Produktionsabschluss.
- Rollen müssen produktiv explizit gesetzt werden; leere Fähigkeitsrollen bleiben
  im bestehenden Vertrag permissiv.
- Dateiablage und auch PostgreSQL sind noch nicht für Mehrprozessbetrieb freigegeben.
- Recovery, Fehler-/Eskalations-/Kompensationssemantik, Alarmierung und vollständige
  Secret-Store-/TLS-Automatisierung bleiben weitere Pakete.

## 8. Fehlerbilder

| Symptom | Ursache | Abhilfe |
|---|---|---|
| API startet nicht mit BFF-Konfigurationsfehler | Authority, Audience, Client-ID, Secret oder Keyring-Pfad fehlt | BFF-Werte und Secret-Injektion nur am API-Container prüfen |
| Browser bleibt nach Login abgemeldet | URL ist HTTP oder Proxy meldet nicht HTTPS/Host weiter | TLS, `X-Forwarded-Proto` und Host-Weitergabe prüfen; `Secure`-Cookies sind unter HTTP absichtlich unwirksam |
| Schreibaufruf liefert 400 `CSRF validation failed` | Header/Origin fehlt oder Request-Token ist abgelaufen | Sitzung/`GET /bff/csrf` erneuern; nur same-origin schreiben |
| Externer Client erhält 401 | Bearer trägt nicht die erwartete Audience oder ist ungültig | Audience/Issuer/Claims prüfen; der BFF ersetzt den externen Bearer-Vertrag nicht |
| Nach Redeploy sind alle Sitzungen ungültig | Data-Protection-Keyring wurde nicht persistent übernommen | getrenntes Keyring-Volume wiederherstellen und künftig sichern |
| `docker compose build` scheitert am SDK | Falsches Feature-Band | Images nutzen `sdk:10.0.103`; `global.json` verlangt 10.0.1xx |
