# Runbook: Pilotbetrieb im Unternehmen

**Stand:** 5. September 2026

Dieses Runbook beschreibt, wie Flowzer als Einzelknoten-Pilot hinter einem TLS-terminierenden Reverse Proxy mit Anmeldung über den Identity Provider des Unternehmens betrieben wird. Es setzt den Stand der Branches `claude/produktivreife-2026-09` und `claude/pilotbetrieb-2026-09` voraus.

## Zielbild

```text
Browser ──TLS──▶ Reverse Proxy (Unternehmen) ──▶ Gateway (nginx, Port 5288)
                                                  ├── /definition, /instance, /usertask, /form, /message, /timer, /operations, /health ──▶ Web-API
                                                  └── alles andere ──▶ Blazor-Frontend (statisch, nginx)
Web-API ──▶ Dateiablage (persistentes Volume .data/runtime-storage)
Browser ──▶ Identity Provider (OIDC, Authorization Code + PKCE)
```

Ein API-Prozess, eine Ablage. Mehrere API-Instanzen auf derselben Ablage sind nicht unterstützt.

## 1. Voraussetzungen

- Docker mit Compose auf dem Zielhost, Zugriff auf `mcr.microsoft.com` und Docker Hub (`nginx:1.27-alpine`)
- Ein Reverse Proxy mit TLS-Zertifikat, der auf `http://<host>:5288` weiterleitet
- Ein Identity Provider (Entra ID oder Keycloak) mit zwei Registrierungen (siehe Schritt 2)
- Persistentes Volume oder Verzeichnis für `.data/runtime-storage`

## 2. Identity Provider einrichten

### Entra ID

1. **API-Registrierung** (z. B. „Flowzer API“): Application ID URI `api://<api-client-id>`, ein Scope `access_as_user`. Die Audience der API ist `api://<api-client-id>` (oder die Client-Id, je nach Tokenversion; im Zweifel beide Varianten im Token prüfen).
2. **SPA-Registrierung** (z. B. „Flowzer Console“): Plattform „Single-page application“, Redirect-URIs `https://<flowzer-host>/authentication/login-callback` und `https://<flowzer-host>/authentication/logout-callback`. Der SPA-Registrierung die API-Berechtigung `access_as_user` erteilen und Admin-Consent geben.
3. Authority: `https://login.microsoftonline.com/<tenant-id>/v2.0`. Die Benutzer-Id kommt als `oid`-Claim (GUID).

### Keycloak

1. Realm anlegen, Client `flowzer-console` als Public Client mit PKCE, gültige Redirect-URIs wie oben, Web Origins `https://<flowzer-host>`.
2. Audience-Mapper, der `flowzer-api` in das Access-Token schreibt; die API erwartet diese Audience.
3. Authority: `https://<keycloak-host>/realms/<realm>`. Die Benutzer-Id kommt als `sub`-Claim (GUID).

## 3. Konfigurieren

```bash
cp .env.example .env
```

`.env` ausfüllen:

| Variable | Bedeutung |
|---|---|
| `FLOWZER_RUNTIME_PORT` | Host-Port des Gateways (Default 5288) |
| `FLOWZER_AUTH_SCHEME` | `JwtBearer` für den Pilot, `None` nur für lokale Prüfungen |
| `FLOWZER_AUTH_AUTHORITY` | OIDC-Issuer der API |
| `FLOWZER_AUTH_AUDIENCE` | erwartete Audience im Access-Token |
| `FLOWZER_OIDC_AUTHORITY` | Issuer für die Oberfläche (in der Regel identisch) |
| `FLOWZER_OIDC_CLIENT_ID` | Client-Id der SPA-Registrierung |
| `FLOWZER_OIDC_SCOPES` | zusätzlich zu `openid profile`, z. B. `api://<api-client-id>/access_as_user` |

Die Oberfläche läuft hinter dem Gateway unter derselben Origin wie die API; CORS ist deshalb nicht nötig. Wird die API unter einer anderen Origin betrieben, zusätzlich `Cors__AllowedOrigins__0` an der API setzen (siehe `docs/OPERATIONS.md`).

## 4. Bauen und starten

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
2. `curl -s https://<flowzer-host>/definition/meta` liefert **401** (Anmeldung wirkt).
3. Im Browser `https://<flowzer-host>/` öffnen: Weiterleitung zum Identity Provider, danach Dashboard mit Benutzername im Seitenmenü.
4. Workflow anlegen, deployen, Instanz starten, Aufgabe in der Task-Inbox abschließen.
5. `https://<flowzer-host>/operations/diagnostics` (angemeldet) zeigt Scheduler- und Ablage-Zustand.

## 6. Betrieb

### Backup

Tägliches Backup der Ablage, idealerweise bei geringer Last:

```bash
tar -czf flowzer-runtime-backup-$(date +%F).tgz .data/runtime-storage
```

Restore: Stack stoppen, Verzeichnis ersetzen, Stack starten. Die Ablage schreibt atomar; ein Backup während des Betriebs ist konsistent auf Dateiebene, kann aber eine gerade laufende Instanzänderung noch nicht enthalten.

### Instanz abbrechen

Im Frontend auf der Instanzseite „Cancel instance“ oder per API:

```bash
curl -X POST -H "Authorization: Bearer <token>" https://<flowzer-host>/instance/<instance-id>/cancel
```

Aktive Tokens werden terminiert, offene Aufgaben entfernt. Bereits erledigte Aktivitäten werden nicht kompensiert.

### Logs und Diagnose

```bash
docker compose -f compose.runtime.yml logs -f api
docker compose -f compose.runtime.yml logs -f frontend
docker compose -f compose.runtime.yml logs -f gateway
```

`GET /operations/diagnostics` liefert Scheduler-Status, Ablage-Snapshot und Observability-Konfiguration. OpenTelemetry-Export ist über `Observability__*` aktivierbar (siehe `docs/OPERATIONS.md`).

### Aktualisieren

```bash
git pull
docker compose -f compose.runtime.yml build
docker compose -f compose.runtime.yml up -d --wait
```

Die Ablage bleibt erhalten. Vor einem Update ein Backup ziehen.

## 7. Bekannte Grenzen des Piloten

- Kein Rollenmodell: jede angemeldete Person sieht alle Workflows, Instanzen, Aufgaben und die Diagnose.
- Zuweisungen (`assignee`, `candidateGroups`) aus dem BPMN werden nicht ausgewertet.
- Dateiablage: ein API-Prozess, keine Historie, keine Abfragen über Instanzen hinaus.
- Fälligkeiten werden angezeigt, nicht ausgewertet.
- Service-Tasks haben keinen Worker-Vertrag; sie warten, bis ein Ergebnis über `POST /usertask` bzw. die Engine gemeldet wird.
- Fehler-, Eskalations- und Kompensationsereignisse führen nur in einen Best-Effort-Fehlerzustand.

## 8. Fehlerbilder

| Symptom | Ursache | Abhilfe |
|---|---|---|
| Frontend lädt, API antwortet 401 | Access-Token trägt nicht die erwartete Audience | `FLOWZER_OIDC_SCOPES` um den API-Scope ergänzen, `FLOWZER_AUTH_AUDIENCE` prüfen |
| Alle benutzerbezogenen Aufrufe 401 trotz Login | `sub`/`oid` ist keine GUID | Entra: `oid` verwenden; Keycloak: `sub` ist GUID; andere IdPs brauchen einen GUID-Claim |
| API startet nicht: „Authentication:JwtBearer:Authority must be set“ | `FLOWZER_AUTH_SCHEME=JwtBearer` ohne Authority/Audience | `.env` vervollständigen |
| Frontend zeigt „Sign-in failed“ | Redirect-URI oder Client-Id passt nicht zur Registrierung | Redirect-URIs `/authentication/login-callback` und `/authentication/logout-callback` prüfen |
| `docker compose build` scheitert am SDK | Falsches Feature-Band | Images nutzen `sdk:10.0.103`; `global.json` verlangt 10.0.1xx |
