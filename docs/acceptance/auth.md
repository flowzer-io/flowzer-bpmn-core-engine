# Abnahme: Installation und Anmeldung (R1b)

Protokoll der reproduzierbaren Installations- und Auth-Abnahme aus Issue #256 (zu #94/#95).
Aufbau, Voraussetzungen und Aufruf beschreibt
[tests/installation-auth/README.md](../../tests/installation-auth/README.md); der Lauf selbst ist
`tests/installation-auth/run.sh`. Die Abnahme belegt das Zusammenspiel der mitgelieferten
Container mit einem echten Keycloak – sie ersetzt **nicht** die Prüfung gegen den
tatsächlichen Identity Provider einer Zielumgebung.

## Umgebung des lokalen Laufs

| Angabe | Wert |
|---|---|
| Datum | 2026-09-24 |
| Code-Stand | `main` (c1a65bb, einschließlich #353) zuzüglich dieses Pakets |
| Host | macOS, arm64, Docker Desktop (Engine 29.8.0, Compose v5.5.1) |
| Identity Provider | Keycloak **26.7.4** (`start-dev --import-realm`, Realm `flowzer-test`) |
| Weitere Images | PostgreSQL 17-alpine, Caddy 2, alpine/openssl 3.5.8, API/Konsole aus `Dockerfile.api`/`Dockerfile.console` |
| Testläufer | Node.js 22, Playwright 1.59.1 (Chromium) |
| TLS | eigene Test-CA; Serverzertifikat mit SAN `flowzer.test`, `auth.flowzer.test` |
| Tokenlaufzeit | Access-Token 30 s (Realm-Einstellung, für den Refresh-Fall) |

## Ergebnis der Läufe

| Lauf | Zeitpunkt | Dauer gesamt | Ergebnis |
|---|---|---|---|
| 3 | 2026-09-24 00:56 | 122 s | 14 bestanden, 0 fehlgeschlagen, 0 `fixme` |
| 4 | 2026-09-24 00:58 | 114 s | 14 bestanden, 0 fehlgeschlagen, 0 `fixme` |
| 5 | 2026-09-24 01:02 | 123 s | 14 bestanden, 0 fehlgeschlagen, 0 `fixme` |
| 6 | 2026-09-24 01:06 | 123 s | 14 bestanden, 0 fehlgeschlagen, 0 `fixme` (Hauptagent, main c1a65bb mit #353) |
| 7 | 2026-09-24 01:12 | 123 s | 14 bestanden, 0 fehlgeschlagen, 0 `fixme` (Hauptagent, mit verschärften check-config-Prüfungen) |
| 8 | 2026-09-24 01:26 | 125 s | 16 bestanden, 0 fehlgeschlagen, 0 `fixme` (Nacharbeit nach Review: neue Origin-/Bearer-Fälle, Reihenfolge Deaktivierung, `fullScopeAllowed=false`) |
| 9 | 2026-09-24 01:28 | 115 s | 16 bestanden, 0 fehlgeschlagen, 0 `fixme` |
| 10 | 2026-09-24 01:38 | 126 s | 16 bestanden, 0 fehlgeschlagen, 0 `fixme` (endgültiger Stand mit unterbrechbarem `run.sh`) |
| 11 | 2026-09-24 01:40 | 164 s | 16 bestanden, 0 fehlgeschlagen, 0 `fixme` (Start 76 s statt 37 s bei vollständigem Build-Cache; Specs unverändert 85 s) |

Die Läufe liefen paarweise unmittelbar nacheinander, jeweils vom leeren Zustand (`down -v`)
bis zum Aufräumen; vor Lauf 5 wurden nur ein Kommentar und ein Import umgestellt, vor Lauf 10
am ausgeführten Code nur die Signalbehandlung in `run.sh`. Davon entfallen rund 30–40 s auf Bauen (mit Build-Cache) und
Starten, rund 85 s auf die Specs; die längsten Fälle warten bewusst eine Tokenlaufzeit ab.
Zwei frühere Läufe mit einem Zwischenstand (123 s, 113 s) waren ebenfalls grün. Ein Bau ohne
Build-Cache dauerte lokal rund 50 s (Basis-Images bereits vorhanden).

## Golden Path

| Schritt | Erwartung | Ergebnis |
|---|---|---|
| `docker compose up --build --wait` | alle Dienste gesund, `certs` und `migrate` mit Exit 0 | erfüllt |
| `--check-config` im API-Container | Exit 0; Konfiguration, Ablage, Migrationen, Authentifizierung (Discovery per GET, `token_endpoint` vorhanden, Issuer passt), Rollen (alle vier Namen), Schlüsselring (beschreibbar), Ausdrücke OK; keine Secrets in der Ausgabe | erfüllt: „Ergebnis: keine Beanstandungen.“ |
| Discovery | `issuer` entspricht exakt der konfigurierten Authority, S256 angeboten | erfüllt |
| `GET /health/ready` über TLS-Proxy und Konsolen-nginx | `Healthy`, `migrationState` `UpToDate`, 0 ausstehend | erfüllt |
| `GET /definition/meta` anonym | 401 ohne Umleitung | erfüllt |
| `GET /bff/login?returnTo=/` | Weiterleitung zu Keycloak, Anmeldeformular, Rücksprung auf `https://flowzer.test:8443/` | erfüllt; die Autorisierung läuft als Pushed Authorization Request (siehe Beobachtungen) |
| `GET /bff/session` (alice) | `access`, `modeler`, `operator`, `worker`; `id` = Keycloak-`sub` (GUID) | erfüllt |
| Sitzungscookie | `__Host-Flowzer-Session`: HttpOnly, Secure, SameSite=Lax, `Path=/`, ohne Domain; kein JWT in Cookies | erfüllt |
| Antiforgery-Cookie | `__Host-Flowzer-Csrf`: HttpOnly, Secure, SameSite=Strict | erfüllt |
| `POST /bff/logout` ohne `X-Flowzer-CSRF` | 400 `application/problem+json`, Sitzung bleibt | erfüllt |
| `POST /bff/logout` mit Token aus `/bff/csrf` | Erfolg | erfüllt mit **204** (Vertrag des Controllers; die Vorgabe nannte 200) |
| `GET /bff/session` nach Abmeldung | 401 | erfüllt |
| `POST /bff/logout` mit gültigem CSRF-Token und Cookies, aber `Origin: https://evil.example` bzw. ohne `Origin` (Aufruf außerhalb des Browsers) | 400 „Invalid request origin.“, Sitzung bleibt; derselbe Token aus der Seite meldet danach ab (204) | erfüllt |
| Bearer mit kaputter Signatur bei bestehender Cookie-Sitzung | 401, kein Rückfall auf das Cookie; Cookie allein weiterhin 200 | erfüllt |

## Negativfälle

| Fall | Erwartung | Ergebnis |
|---|---|---|
| Bearer von `other-audience-cli` (`aud=other-api`, alice) | 401 | erfüllt |
| Bearer carol (keine Rolle) | 403, `X-Flowzer-Access-Denied: application` | erfüllt |
| Bearer bob (nur `access`): `GET /definition/meta` | 200 (Kontrollfall) | erfüllt |
| Bearer bob: `GET /form/compatibility` (Modeler-Policy) | 403 `capability` | erfüllt |
| Bearer bob: `POST /definition/new` auf oberster Ebene | 403 `capability` | erfüllt |
| alice `modeler` per Admin-API entzogen, neuer Bearer | 403 `capability` | erfüllt |
| BFF-Sitzung von alice nach > 30 s | `modeler` false, übrige Fähigkeiten und Sitzung bleiben | erfüllt; Rolle danach wieder erteilt und bestätigt |

## Verzeichnisabgleich

| Fall | Erwartung | Ergebnis |
|---|---|---|
| Servicekonto `flowzer-directory` (`fullScopeAllowed=false`, Leserechte per Scope-Zuordnung) | Token trägt nur `view-users`, `query-groups` (und das darin enthaltene `query-users`); lesen 200, Benutzer anlegen 403 | erfüllt |
| `POST /identity-directory/sync` (alice, Operator) | 202, danach neuer erfolgreicher Stand: 4 Personen, 2 Gruppen, 2 Mitgliedschaften | erfüllt |
| `POST /identity-directory/sync` (bob) | 403 `capability` | erfüllt |
| `GET /identity-directory/workflows/{id}/subjects?query=dave` | dave aktiv gefunden | erfüllt |
| dave per Admin-API deaktiviert | kein neues Token (400 `invalid_grant`) | erfüllt |
| daves vorher ausgestellter Bearer, sofort nach der Deaktivierung | wird weiter angenommen (200) – dokumentierte Grenze, kein Fehlschlag | erfüllt |
| BFF-Sitzung von dave | endet beim nächsten Refresh (401) | erfüllt |
| erneuter Abgleich, erneute Suche | dave nicht mehr angeboten | erfüllt |

Zusätzlich protokolliert der Test ohne Zusicherung, wie die API daves alten Bearer 2 s nach
`exp` beantwortet; in allen Läufen war das 200 (siehe Beobachtung 5).

## Neustart

| Fall | Erwartung | Ergebnis |
|---|---|---|
| `docker compose restart api` mit bestehender Cookie-Sitzung | alte Sitzung 401, nicht 500 | erfüllt |
| `/health/ready` nach Neustart | wieder `Healthy` | erfüllt (wenige Sekunden) |
| Keyring-Volume | dieselben `key-*.xml` wie vor dem Neustart | erfüllt |
| erneute Anmeldung | gelingt; wegen bestehender Keycloak-SSO-Sitzung ohne Formular | erfüllt |

## Zusätzlicher Negativlauf: Proxy-Netz außerhalb der Vertrauensgrenze

Einmalig mit `FLOWZER_INSTALLATION_AUTH_SUBNET=192.168.250.0/24` gestartet, also außerhalb von
`ForwardedHeaders:KnownNetworks=172.16.0.0/12`. Wie in OPERATIONS.md beschrieben, glaubt die
API den Forwarded-Headern dann nicht und baut `http://flowzer.test:8443/bff/signin-oidc`;
Keycloak lehnt den Pushed Authorization Request mit `invalid_redirect_uri` ab, beide
Browser-Login-Fälle schlugen fehl, alle Bearer-Fälle blieben grün. `run.sh` sammelte Logs
(ohne Testsecrets) und räumte auf. Dieser Lauf ist bewusst nicht Teil der Standardabnahme.

## Abbruch des Laufs

`run.sh` wurde dreimal gezielt unterbrochen, zweimal unter Runner-Bedingungen (Schritt-Shell
mit `exec`, SIGINT, nach 7,5 s SIGTERM, nach 10 s SIGKILL):

| Signal | Zeitpunkt | Ergebnis |
|---|---|---|
| SIGINT | während `docker compose up --build --wait` | Exit 130 nach 2,9 s; Logs gesammelt, Stack und Volumes entfernt |
| SIGINT | während der Specs | Exit 130 nach 1,9 s; Playwright beendet, Logs gesammelt, aufgeräumt |
| SIGTERM | während der Specs | Exit 143 nach 2 s; Logs gesammelt, aufgeräumt |

Die Falle greift damit weit vor dem SIGTERM des Runners. Ohne die Ausführung im Hintergrund
(`run_child`) hätte Bash das Signal erst nach dem Ende von Playwright behandelt. Ohne `exec`
im CI-Schritt erreichte das Signal `run.sh` gar nicht. Einen HTML-Report schreibt Playwright
bei einem Abbruch nicht; die Compose-Logs liegen trotzdem vor.

## Beobachtungen und Abweichungen

1. **Pushed Authorization Requests:** Der OIDC-Handler von .NET 10 nutzt PAR automatisch, weil
   Keycloak den Endpunkt anbietet. `response_type`, `redirect_uri` und die PKCE-Challenge
   stehen deshalb nicht in der Browser-URL; der Test prüft dann `request_uri` und wertet den
   erfolgreichen Rücksprung als Beleg, weil Keycloak S256 und die exakte Redirect-URI erzwingt.
2. **Logout-Status:** `POST /bff/logout` antwortet 204, nicht 200.
3. **`--check-config` und Webhooks:** Seit #353 (R1a) prüft `--check-config` Rollen, Schlüsselring
   und das Discovery-Dokument selbst; die Specs belegen Issuer und S256 zusätzlich gegen den
   echten Provider. `ServiceTaskWebhooks__Enabled=false` ist im Teststack nötig, sonst endet die
   Prüfung mit Exit 2 (Webhooks aktiviert, aber kein freigegebenes Ziel).
4. **Refresh-Vorlauf fest 60 s:** Der BFF erneuert, sobald das Access-Token in weniger als
   60 s abläuft. Bei 30 s Laufzeit erneuert er deshalb bei **jeder** Anfrage (Rollenentzug
   sofort sichtbar, dafür ein Token-Aufruf pro Anfrage). Bei einer üblichen Laufzeit von
   5 Minuten wird ein Rollenentzug in einer bestehenden Sitzung erst nach bis zu 4 Minuten wirksam.
5. **Beobachtung – Bearer nach `exp`:** Die JWT-Bearer-Prüfung setzt keine eigene
   Uhrentoleranz, also gilt vermutlich der Standard von 5 Minuten. Beobachtet (nicht
   zugesichert): Der Bearer der deaktivierten Person wurde 2 s nach `exp` noch mit 200
   beantwortet. Die BFF-Prüfung selbst verwendet 1 Minute.
6. **Fehlerbild bei abgelehnter Autorisierung:** Scheitert der PAR-Aufruf an Keycloak, liefert
   `/bff/login` 500 mit generischer Meldung; die Ursache steht nur im API- und Keycloak-Log.
7. **Npgsql-Hinweis:** `--migrate` und `--check-config` schreiben „Cannot load library
   libgssapi_krb5.so.2“ nach stderr, weil das Runtime-Image die Kerberos-Bibliothek nicht
   enthält. Folgenlos, aber irritierend.
8. **Keyring unverschlüsselt:** Data Protection warnt „No XML encryptor configured“; der
   Schutz beruht wie dokumentiert allein darauf, dass nur die API das Volume sieht.
9. **Test-CA für .NET:** `SSL_CERT_FILE` auf das CA-Zertifikat genügt; `RequireHttpsMetadata`
   bleibt `true`, ein Entrypoint-Wrapper ist nicht nötig.

Keiner dieser Punkte ließ einen Testfall scheitern; es gibt keine `test.fixme`-Fälle.

## Grenzen

- **Verzeichnis-Deaktivierung entzieht keinen Zugang.** Sie entfernt die Person aus Auswahl
  und Suche. Den Zugang beendet erst Keycloak: kein neues Token, BFF-Sitzung endet beim
  nächsten Refresh.
- **Bearer gelten bis zum Ablauf**, beobachtet auch kurz darüber hinaus (Uhrentoleranz, siehe
  Beobachtung 5). Introspection oder Backchannel-Widerruf gibt es nicht.
- **Logout ist lokal.** `POST /bff/logout` beendet nur die Flowzer-Sitzung; die SSO-Sitzung bei
  Keycloak bleibt, ein erneuter Login kommt ohne Formular zurück. IdP-Logout ist offen (R1c).
- **Sitzungen sind prozesslokal.** Ein API-Neustart beendet alle Browser-Sitzungen (401);
  mehrere Replikate brauchen Sitzungsaffinität.
- Geprüft wird ein Minimal-Realm mit Caddy als TLS-Proxy, nicht Entra ID, Coolify, echte
  Zertifikate oder eine konkrete Proxy-Kette der Zielumgebung.
