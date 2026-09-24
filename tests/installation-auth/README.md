# Installations- und Auth-Abnahme (R1b)

Reproduzierbarer „Golden Path“ und Negativlauf für Installation und Anmeldung – lokal und in
der CI, **ohne Produktionszugang**. Der Stack wird aus diesem Repository gebaut und läuft mit
eigener Test-CA und eigenem Keycloak. Das Protokoll des letzten lokalen Laufs steht in
[docs/acceptance/auth.md](../../docs/acceptance/auth.md).

## Voraussetzungen

- Docker mit Compose v2 (Engine oder Docker Desktop), Port `127.0.0.1:8443` frei
- Node.js 22 und npm
- Netzwerkzugriff auf Docker Hub, quay.io, mcr.microsoft.com und npm für den ersten Build

Einträge in `/etc/hosts` sind nicht nötig: Chromium bildet `flowzer.test` und
`auth.flowzer.test` über `--host-resolver-rules` auf `127.0.0.1` ab, die Node-Seite der Tests
über einen eigenen DNS-Lookup (`support/http.js`).

## Ablauf

```bash
tests/installation-auth/run.sh          # bauen, starten, prüfen, aufräumen (down -v)
tests/installation-auth/run.sh --keep   # Stack nach dem Lauf stehen lassen
```

`run.sh` installiert bei Bedarf die Node-Abhängigkeiten und Chromium, entfernt Reste eines
früheren Laufs, startet den Stack mit `docker compose up -d --build --wait` und führt danach
`npx playwright test` aus. Bei einem Fehler landen `docker compose ps` und die Logs aller
Dienste unter `logs/` (Testsecrets aus `env/*.env` ersetzt), der HTML-Report unter
`playwright-report/`.

Ein Abbruch per Strg+C oder `SIGTERM` beendet Stack-Start bzw. Playwright sofort; auch dann
werden die Logs gesammelt und der Stack abgeräumt (Exit 130 bzw. 143). Die CI ruft das Skript
deshalb mit `exec` auf: Der Runner schickt sein Abbruchsignal nur an den direkten Kindprozess.

Bei stehendem Stack (`--keep`) lassen sich einzelne Specs direkt starten:

```bash
cd tests/installation-auth
npx playwright test specs/negative.spec.js --project=flows --no-deps
```

## Aufbau

| Dienst | Zweck |
|---|---|
| `certs` | Init-Container: Test-CA und Serverzertifikat (SAN `flowzer.test`, `auth.flowzer.test`) in Volumes; der CA-Schlüssel wird verworfen |
| `tls` | Caddy als TLS-Proxy auf `127.0.0.1:8443`; Netzwerk-Aliase, damit auch Container die externen Namen über den Proxy erreichen |
| `keycloak` | Keycloak 26.7.4 (`start-dev --import-realm`) mit dem synthetischen Realm `keycloak/flowzer-test-realm.json` |
| `db` | PostgreSQL 17; Datenbank und getrennte Migrations-/Laufzeitrolle über `deploy/postgresql/01-datenbank-und-rollen.sql` (bindet `02-laufzeitrechte.sql` ein; deshalb ist das ganze Verzeichnis eingehängt) |
| `migrate` | `--migrate` mit der Migrationsrolle |
| `api` | Web-API im BFF-Modus, Rollen `access`/`modeler`/`operator`/`worker`, Verzeichnisabgleich aktiv; vertraut der Test-CA über `SSL_CERT_FILE`, `RequireHttpsMetadata` bleibt `true` |
| `console` | React-Konsole mit nginx, leitet API-Pfade an `api:8080` |

Testkonten (Passwort jeweils `<name>-test-password`): `alice` alle vier Rollen und Gruppe
`/team/review`, `bob` nur `access`, `carol` ohne Rolle, `dave` `access` und `/team/review`.
Weitere Clients: `flowzer-test-cli` (Bearer per Direct Grant), `other-audience-cli` (falsche
Audience), `flowzer-directory` (Servicekonto, nur lesend). Alle Secrets und Passwörter sind
offensichtliche Testwerte und gehören in keine echte Installation.

## Specs

| Datei | Inhalt |
|---|---|
| `specs/check-config.spec.js` | `--check-config` im API-Container endet mit 0; Discovery-Issuer entspricht der Authority |
| `specs/golden-path.spec.js` | Readiness, anonym 401, BFF-Anmeldung über Keycloak, Fähigkeiten, Cookie-Attribute, CSRF, fremder Origin, ungültiger Bearer neben Cookie, Abmeldung |
| `specs/negative.spec.js` | fremde Audience 401, ohne Zugangsrolle 403 `application`, ohne Modeler 403 `capability`, Rollenentzug für Bearer und BFF-Sitzung |
| `specs/directory.spec.js` | Servicekonto nur mit explizit zugeordneten Leserechten, manueller Abgleich, workflowgebundene Suche, Deaktivierung (inklusive dokumentierter Grenze) |
| `specs/restart.spec.js` | `docker compose restart api`: alte Sitzung 401, Health wieder gesund, Keyring unverändert, erneute Anmeldung |

Die Playwright-Projekte erzwingen die Reihenfolge `check-config` → Abläufe → Neustart.
Specs, die Keycloak verändern, stellen den Ausgangszustand vorher und nachher wieder her.

## Grenzen

- Der Stack prüft die mitgelieferten Container und einen Keycloak mit Minimal-Realm, keine
  konkrete Zielumgebung (Entra ID, Coolify, echte Zertifikate, echte Proxy-Kette).
- Port 8443 ist fest, weil er Teil von `KC_HOSTNAME` und der Redirect-URI ist.
- Das Compose-Netz ist fest auf `172.30.231.0/24` gesetzt (überschreibbar über
  `FLOWZER_INSTALLATION_AUTH_SUBNET`), damit die Proxy-Vertrauensgrenze `172.16.0.0/12` der
  API unabhängig vom Adresspool des Docker-Hosts stimmt. Ein Netz außerhalb davon führt – wie
  dokumentiert – zu einer `http://`-Callback-URI und einem abgewiesenen Login.
- Keycloak läuft im Entwicklungsmodus mit eingebetteter Datenbank; Leistung und Hochverfügbarkeit
  sind nicht Gegenstand dieser Abnahme.
- Die Access-Token-Laufzeit ist mit 30 Sekunden absichtlich kurz. Weil der BFF 60 Sekunden vor
  Ablauf erneuert, erneuert er hier bei jeder Anfrage.
