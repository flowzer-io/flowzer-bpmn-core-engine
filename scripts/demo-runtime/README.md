# Temporärer Ressourcenpilot — nur vorbereiteter Quellenslice

Dieser Ordner gehört ausschließlich zum eigenen temporären Branch
`codex/flowzer-demo-runtime-budget`. **Nicht nach Main, Release oder PR379
übernehmen.** Original-CI, Produktquelle CF und Standard-/Langzeit-Harness bleiben
unverändert. Noch kein Workflowdispatch, Containerstart oder Ressourcenmesswert.

## Vorbereiteter Teil

`prepare.py` kopiert nur die 25 git-versionierten Installations-/Auth- und
PostgreSQL-Fixtures des bekannten CF-Trees in einen neuen externen Wegwerfpfad.
Das Modul startet keine Prozesse außer begrenzten lesenden Git-Aufrufen.
Vor jedem I/O prüft es den bestätigten manuellen Erstlauf auf dem eigenen Ref,
GitHub-hosted Linux/X64 und einen vollständigen Workflow-SHA.

In der Kopie ändern sich ausschließlich:

- eigener Projektname aus Run-ID/Attempt, keine geteilten Container/Volumes;
- direkte feste API-/Console-Digests aus Publish38009656496/a1 sowie frisch
  gelesene PG-/Test-Keycloak-/Caddy-/OpenSSL-Indexdigests; keine Builds/Tags;
- harte vorläufige CPU-/RAM-Schutzlimits für sieben Dienste, internes Compose-Netz;
- dieselben Original-Testassertions, Testreihenfolge, null Retries und unveränderte
  Timeouts; zusätzliche Originprüfung und eigener pro-Test-CONNECT-Proxy nur für die
  zwei lokalen HTTPS-Authorities; physisch ausschließlich Loopback8443, kein
  DNS des angefragten Hosts, kein direkter Loopback-/Redirectbypass;
- WebSocket- und ServiceWorker-Sperre, keine Trace-/Screenshot-/Video-/HTML-Ausgabe;
- sicherer Reporter nur mit numerischen Ergebniszählern, keine Titel, Attachments,
  URL-, Exception-, Env-, Session-, Token- oder Rohlogausgabe.

Nur Specs-Importe binden die zusätzliche Fixture ein; fachliche Assertions bleiben
bytegleich. Der Node-HTTPS-Client des Originalrigs hat bereits eine feste lokale
Host-Map ohne Redirect-Following. Browser-Negativziele werden blockiert und führen
zu einem geschlossenen Testfehler statt echter externer Kommunikation.

Der Stats-Parser akzeptiert nur bekannte Dienste, konkrete numerische
Docker-Einheiten und den exakt vorgeschlagenen RAM-Limitwert. Gerundete
CLI-Verbrauchsanzeigen werden mit Ceiling ausdrücklich als
`memory_bytes_approx` projiziert; der Limitteil wird weiterhin exakt geprüft. Fremde/ungültige
Messwerte sind Fehler, keine Nullen. Er misst selbst nichts.

## Grenzen und noch erforderlicher nächster Slice

Dieser Stand enthält **keinen Runtime-Runner und keinen geänderten CI-Workflow**.
Vor einem späteren separat freigegebenen Erstdispatch sind erforderlich:

1. Quellen-CI37994630569/a3, eigener Publish38009656496/a1 und erhaltene Receipts
   frisch prüfen; Registryindexes/Runtime-Manifeste/Configrevision verifizieren.
2. Für alle Basisimages die gepinnten Digests/native Linux-amd64-Zuordnung frisch
   lesen. `base-images.json` ist der tatsächliche Registry-Metadatenstand vom
   10.10.2026, kein Container-/Config-/Laufzeitnachweis.
3. Geschlossener Runner: nur auf eigenem Hosted-Runner eigene Ressourcen starten,
   reale Containerlabels/Image-/Limit-/OOM-/Exit-Bindung prüfen, sichere Stats/
   Diskmessung sammeln und ausschließlich eigene Ressourcen wieder entfernen.
   Kein Original-`run.sh`: dessen fixer Name, Build und Rohlogs sind hier ungeeignet.
4. Begrenzte Artefakt-Whitelist und eigener manueller SHA-/Attemptguard im
   temporären Workflow; packages-read, kein Publish/Deploy/Cloud/Vault-/TT-/n8n-/
   PBX-/echter IdP-Aufruf. Exakter neuer Head und unabhängige Reviews vor einem Go.

API512MiB/PG256MiB/Console64MiB (832MiB) sind **Vorschlagslimits**, kein Bedarf.
Migration512MiB, Test-Keycloak768MiB, Proxy64MiB und Init32MiB werden getrennt
berücksichtigt; ihre Parallelität/Spitzen darf nicht als Produktbedarf ausgeblendet
werden. Dieser Auth-Pilot allein attestiert weder Backup-/Restore-/DBwachstums-
Diskbedarf noch TT-Spitzenlast, 45-Minuten-Formularabnahme oder gemeinsame reale
Keycloak-Gruppen. Diese bleiben eigene Abnahmen vor einer Installation.

Keine Installation auf dem derzeit RAM-knappen dev01, kein Resize, neuer Host,
DNS, Proxy, Realm, Merge oder anderer Rollout aus dieser Vorbereitung.

## Wirklich ausgeführte Offline-Tests

```sh
python3 scripts/demo-runtime/test_prepare.py
node --test scripts/demo-runtime/test_helpers.js scripts/demo-runtime/test_proxy.js
```

Vier positive Vertragsfälle zunächst tatsächliches Assertion-RED, danach grün.
Zusätzliche ServiceWorker-, Kontext-, feste-Git-Blob- und zwei Netzgrenzfälle ebenfalls zuerst real rot.
Aktuell zwölf Python- plus sieben Node-Unitfälle ohne Skips. Diese Tests verwenden
keinen Docker-/Browser-/Netz-/Realmzugriff; Node-Fixture-/Proxytests mocken nur den
Playwright- und Transportvertrag; sie starten keine Listener oder TCP-Verbindungen. Sie sind kein echter Browser-/Ressourcen-Abnahmenachweis.

### Unabhängige Quellenreview-Nacharbeit

Vier konkrete P2 wurden fachlich geschlossen: CF-Dateiliste/Modes und kopierte
Bytes direkt aus festen Git-Blobs statt potentiell versteckter Worktreeänderungen
sowie Erhalt der geprüften Dateimodi (insbesondere ausführbare PostgreSQL-Init-Hooks);
zulässige gerundete Stats als Schätzung bei weiterhin exakten Limits; redirectfeste
CONNECT-Verbindungsgrenze statt alleiniger Erstrequest-Route. Regressionsproben
zuerst real rot, anschließend alle19 Unitfälle grün. Der Browser wird durch
explizites Contextproxy- und `<-loopback>`-Binding vermittelt; fremde Connect-/
Reuse-/Loopback-Disable-Umgebungen werden vor Listenerstart abgewiesen.
**Echte Chromium-/Redirect-/IP-Durchsetzung ist weiterhin unerfülltes
Erstdispatch-Gate**, nicht aus den Transportmocks abgeleitet.
