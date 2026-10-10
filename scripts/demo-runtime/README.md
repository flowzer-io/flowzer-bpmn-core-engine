# Temporärer Ressourcenpilot — isolierter Abnahmevorschlag

## Neue feste Eigen-Ref – ausschließlich Quellenvorbereitung

`codex/flowzer-runtime-pilot-diagnostics` beginnt im eigenen frischen Worktree vom
frisch gefetchten Archiv-Commit `31ae808cd47107b6d295813d488816460491b45d`.
Nur diese exakte Ref ist im Workflow, im Vor-I/O-Kontextguard und in der aktuellen
GitHub-Run-Prüfung gebunden. Main, Release, fremde Refs, der alte Ressourcenbranch
und der bloße Archivbranch bleiben in diesem Kandidaten ausgeschlossen. Keine
Wildcard und keine zusätzliche alternative Ref. Alte positive Testfixtures ändern
ausschließlich ihre feste Eigenidentität; keine Testfälle oder Erwartungen entfallen.

Drei neue rein synthetische Ref-Tests belegten zunächst sieben echte Assertion-REDs.
Sie prüfen auch die unabhängige aktuelle API-Run-Identität vor Publisher-I/O und
die vollständige Workflow-Konjunktion statt nur passende Teilstrings. Alle bisherigen
48 Python-/8 Node-Fälle bleiben erhalten; Schutzgrenzen, Publisherquellen, originale
Authspecs und Ressourcenlimits sind unverändert. Die bisherigen Archiv-/Pilotbelege
werden weder verändert noch als Nachweis eines neuen Laufs umgedeutet.
Der vollständige synthetische Lauf besteht jetzt mit **51 Python-/8 Node-Tests**
ohne Skips; das sind keine tatsächlichen Hosted- oder Authausführungen.

**Bis zur Anmeldung und separaten Freigabe kein Push dieses Kandidaten. Kein
Dispatch, Pilot, Retry, Publisher, SSH/Docker/Runtime, Install oder Produktmerge.**
Ein tatsächlicher einzelner Hosted-Versuch bleibt separat freizugeben und zentral
zu koordinieren. Quellentests sind keine Ressourcen-, Auth-, Cleanup- oder Liveabnahme.

## Archivierter lokaler Diagnosekandidat (10. Oktober 2026)

`codex/flowzer-runtime-diagnostics` startet vom frisch gelesenen Runtime-Stand
`4725477244b2c948d3181b19721da127a586f704`. Der alte Stand, seine 40 Python-/8 Node-
Belege und der fehlgeschlagene Pilot `38016986837/a1` bleiben unverändert. Dieser
Kandidat gestattet **keinen** neuen Pilot, Dispatch, Retry, Publisher oder Deploy.
Seine unveränderte alte Workflow-Ref-Grenze ließ den bloßen Archivbranch nicht laufen.

Diagnosen enthalten ausschließlich feste `phase`-/`error`-Werte und einen wirklich
beobachteten POSIX-`exit_code`; `null` bedeutet ausdrücklich **unbekannt**, nicht 0.
Auch eine tatsächlich unerwartete 0 und negative Signalwerte bleiben unverändert.
Maximal drei Ursachen erhalten den ersten Fehler sowie zusätzliche Stop-/Cleanup-
Fehler. Innere Messfehler werden beim Weiterreichen nicht zu Startfehlern umbenannt.
Roh-Ausnahmen, Argumente, stdout/stderr, URLs, Umgebungswerte und Secrets erscheinen
weder im Zusatzbericht noch in der Konsole. Zeitlimits, Prozessgruppen-Stoppfolge,
Eigentümer-/Freshness-/Auth-/RAM-Grenzen und Originalspecs bleiben unverändert.

Neue fünf Diagnose-Unitfälle hatten vor Umsetzung neun wirkliche Assertion-REDs.
Die zwei unabhängigen Gesamtreviews fanden drei weitere Diagnosefehler: Gesamt-
deadline wurde als Validierung gemeldet, ein Abschlussread konnte trotz Fehler
`success=true` hinterlassen, und Bericht-I/O-Fehler konnten aus der Konsole fehlen.
Drei zusätzliche Unitfälle belegten diese zuerst mit acht Assertion-REDs. Zwei
weitere Assertion-REDs sichern ab, dass ein fehlgeschlagenes Rechte-Setzen noch
keine Erfolgsbytes schreibt: Der ausschließlich eigene Bericht wird mit `xb`
angelegt und erhält 0600 **vor** seinem ersten Byte. Docker-/Stop-Reihenfolge und
alle Zeit-/Ressourcen-/Autorisierungsgrenzen bleiben unverändert.

Jetzt bestehen insgesamt **48 Python-Tests** und **8 Node-Tests** ohne Skips;
alle Prozesse, Docker-/Auth- und Netzwerkzugriffe der neuen Tests sind synthetisch
gemockt. Das belegt weder die
Ursache des alten Piloten noch Runtime-, Cleanup-, Ressourcen- oder Installationsreife.

```sh
python3 -B -m unittest discover -s scripts/demo-runtime -p 'test_*.py'
```

**Aktueller Kandidat ausschließlich `codex/flowzer-runtime-pilot-diagnostics`, nicht nach Main, Release,
PR379 oder Candidate-bca übernehmen.** Die registrierte `ci.yml` ist nur in diesem
Wegwerfbranch durch einen manuellen Vorschlag ersetzt. Alle anderen Original-
Workflows sowie Produktquelle CF und Original-Harness bleiben bytegleich.

**Noch kein Dispatch oder Container-/Flowzer-/PG-/Keycloak-/Ressourcenlauf.**
Der koordinierende Root prüft den fertigen Head/Tree und gibt gegebenenfalls genau
einen eigenen Hosted-Abnahmelauf separat frei. Kein Installations-, Merge-, DNS-,
Proxy-, Realm-, Resize- oder anderer Demo-/Productionpfad entsteht dadurch.

## Kleiner geschlossener Runner

`runner.py` ist der einzige Runtime-Einstieg. Vor jedem I/O bindet er exaktes Repo,
eigenen Branch, manuellen Erstlauf, vollen bestätigten Workflow-SHA und Hosted
Linux/X64. Fremde Docker-/Browser-/Proxytransportvariablen sind Fehler. Zusätzlich
bestätigt `proof.py` den tatsächlich laufenden GitHub-Run und frisch:

- Original-CI37994630569/a3 mit acht erfolgreichen Jobs, Preview399543/CF-Tree
  und erhaltenem Testartefakt über die feste bereits geprüfte Publisherbibliothek;
- Publisher38009656496/a1, Headbcaeef/Treef573, sieben erfolgreiche Jobs und
  sieben exakt hash-/inhaltgebundene Receipt-ZIPs (`publish-bindings.json`);
- API-/Console-Finalindexes, gesamte native Builddescriptorunion einschließlich
  Provenance, beide Linuxplattformen und vollständige CF-Configrevision;
- alle vier festen Baseimage-Indexes/native-amd64-Manifeste und deren Configdigests.

Token bleiben im Prozess. HTTPS-Redirects sind standardmäßig geschlossen; ein
explizit erlaubter GitHub-Artifact-/Registryblobredirect erhält **keine Authheader**.
Roh-OCI-Configs, signierte Download-URLs und Fehlertexte werden nicht gespeichert.
Vorläufige Dockerzugänge nutzen nur einen eigenen nichtgeheimen Credentialhelper
und eine Pipe; kein `docker login`, kein Token in Argumenten oder Authsdateien.
Nach Pull endet der Helper, Browser-/Start-/Testprozesse bekommen nur eine
Umgebungs-Whitelist ohne GitHub-/Cloud-/IdP-/Vaulttoken.

`prepare.py` kopiert ausschließlich 25 Dateien/Modes aus festen CF-Gitblobs in
einen neuen externen Ordner. Keine versteckten Worktreebytes oder untracked
Materialien. Nur in der Kopie: eigener Namespace und zusätzliche Eigentümerlabels
für Container/Netz/Volumes, sechs feste Images für sieben Dienste, native Plattform,
harte RAM-/CPU-/Noswap-Limits, internes Netz und geprüfte Browser-/Reporterhelfer.
Kein Build, kein Tag, kein Original-`run.sh`, kein fremdes `down`/`prune`.

Vor jedem Messen und Cleanup kontrolliert der Runner tatsächliche Labels, CIDs,
Image/Configdigest, Memory/MemorySwap/NanoCpus; eigenes Netz muss intern sein.
Die Namespace-Leerprüfung berücksichtigt auch Netze und Volumes. Startup und alle
**16 Original-Authspecs** laufen mit identischen Assertions, Reihenfolge, null
Retries und unveränderten Spectimeouts. Original-Specs werden nur durch ihre
zusätzliche Fixture vermittelt. Auch der `--check-config`-Container gehört
zur Messung, statt dessen Zusatzbedarf auszublenden. Genau sein `--rm` wird nur in
der Kopie entfernt: CID, Exit und OOM bleiben bis zum validierten Gesamtcleanup
erhalten. Die Original-Specassertions bleiben bytegleich. Bereits beendete
Containerwerte sind keine künstlichen Nullmessungen. Ein OOM ist ein Fehlbefund. Jeder vollständig eigentümer-/image-/limitgebundene
Zustandsread merkt OOM monoton, auch ein später Inspect oder Cleanup nach frühem
Startfehler; fremde Zustände verändern die Diagnose nicht.

Gemessen werden diskrete cgroup-Gesamt-RAMwerte über den eigenen lokalen
Docker-Daemonsocket, CLI-CPU/PIDs, tatsächliche Limits/OOM/Exit/Restartzustände,
Docker-Partition vor/nach Pull/Start/Tests/Cleanup und eigene allozierte PG-Verzeichnisbytes als explizite KiB-Rundungsnäherung
(`du -sk`, BusyBox-portabel, keine exakte logische DBgröße).
CLI-RAM bleibt separat `memory_bytes_approx`: Docker zieht unter Linux Cache ab,
also ist dies **nicht** der vollständige RAMbedarf. [Docker-Stats-Dokumentation](https://docs.docker.com/reference/cli/docker/container/stats/)

Nur ein begrenzter, geschlossener `resource-result.json` mit Zahlen, festen
Enumwerten und Quellen-/Digestbelegen ist uploadbar. Keine Env-, Token-, URL-,
Browserexception-, Titel-, Attachment-, Trace-, Screenshot-, Video-, HTML- oder
Rohlogartefakte. Fehler/Abbruch schließen nur eigene Kindprozessgruppen und
validierten Ressourcenbesitz. Auch ein Cleanupfehler bleibt `success=false`.
Images werden nicht global gepruned; ihre Cachelebensdauer endet mit dem
GitHub-hosted Wegwerfrunner, nicht durch Löschen möglicherweise geteilter Basisimages.

## Browsernetzgrenze und wirkliche lokale Proben

`restricted-test.js` vermittelt **Browserprozess und alle Contexts** über denselben
worker-eigenen CONNECT-Proxy. Nur `flowzer.test:8443` und
`auth.flowzer.test:8443` sind erlaubt; TCP führt immer nach Loopback8443, nie durch
angefragten DNS. `<-loopback>` verhindert direkten Loopbackbypass. QUIC und
nichtvermittelter WebRTC-UDPverkehr sind geschlossen. Originroute ist nur ein
zusätzlicher früher Schutz; insbesondere Redirects benötigen den Proxy.
WebSockets und ServiceWorker sind gesperrt. Jeder unerlaubte Versuch schlägt fehl.

`egress-probe.js` beweist mit **echtem Chromium1.59.1** und ausschließlich eigenen
kurzlebigen Loopback-TLS-/Markerlistenern: erlaubte Seite200; Redirect auf IP und
verbotenen Namen, direkte IP, WS und SW blockiert; verbotener Marker0Requests.
Ein vorab erreichbarer Marker liefert genau1 Positivkontrollrequest, danach wird
sein Zähler zurückgesetzt. Synthetischer Testschlüssel bleibt nur im Speicher.

`--calibrate-local-only` verändert ausschließlich eine IN-MEMORY-Proxykopie für
**genau den eigenen Marker**. Keine DNS-/WAN-Route. Der wirkliche Markerassert
muss rot werden (1Request, `marker_assertion_red=1`), sonst ist die Messung kein
beweisender Negativtest. Ein Setup-/Browserfehler zählt ausdrücklich nicht.
`fixture-probe.spec.js` prüft außerdem zwei echte Playwright-Contexttests mit
worker-/launchOptions-/Contextproxybindung. Eigene Browser/Listener werden geschlossen.

Ein initialer Folgefehler nach absichtlich abgebrochener Navigation war
`ERR_ABORTED`, kein Egressnachweis. Der Folgetest verwendet deshalb eine neue
eigene Seite; keine Schutzregel, Origin oder Assertion wurde dafür gelockert.

## Ausgeführt versus noch offen

Offlinebefehle (keine Container oder externen Dienste):

```sh
python3 scripts/demo-runtime/test_prepare.py
python3 scripts/demo-runtime/test_proof.py
python3 scripts/demo-runtime/test_runner.py
python3 scripts/demo-runtime/test_workflow.py
node --test scripts/demo-runtime/test_helpers.js scripts/demo-runtime/test_proxy.js
```

Aktuell **40 Python- plus8 Node-Unitfälle**, echte YAML-Parsing-/JS-Syntaxprüfungen,
keine Skips. Test-first belegte Assertion-REDs für Limits/Eigentümerlabels,
Browserflags, manuelle Workflowgrenzen, aktuelle Runbindung, RAM-/Leerprüfung und
Egressreportprojektion. Frühe Proof-/Runner-Stubs hatten zusätzlich erwartete
noch-nicht-implementiert-Fehler; diese werden nicht als reine Assertion-REDs gezählt.
Lifecycle-/Cleanupfälle sind ergänzende Offline-Regressionen, kein echter Dockerlauf.
Der Review-P2 zur automatisch entfernten Oneoff-CID wurde mit realem Mocktransport-
Assertion-RED geschlossen; daneben beendete Null-Stats/Oneoff-Projektion und die
BusyBox-KiB-Projektion jeweils zuerst tatsächliches Assertion-RED, danach grün.
Vier zusätzliche OOM-Regressionsfälle waren mit sechs wirklichen Assertion-REDs
belegt: späte CLI-/Daemonreads, letzte Zustandsprüfung und Cleanup nach frühem
Startfehler. Nach vollständiger Bindung bleibt der Befund monoton im Fehlerbericht;
ein später eigener OOM kann kein erfolgreiches Artefakt mehr ergeben.

Separat tatsächlich lokal ausgeführt: echte Chromium-Positiv-/Mutation-/Negativ-
Proben und zwei echte Fixturetests mit synthetischen eigenen Listenern. **Kein**
Flowzer, PostgreSQL, Keycloak, realer TT/IdP, Imagepull, Docker oder Workflowdispatch.
Die Registry-/GitHub-Freshnessfunktion und Runnerorchestrierung sind noch **nicht
live ausgeführt**; ihre Offlinefälle mocken Metadaten beziehungsweise Docker.

API512MiB/PG256MiB/Console64MiB (832MiB) sind Schutzlimits, **kein Bedarf**.
Migration512MiB, Test-Keycloak768MiB, Proxy64MiB, Init32MiB sowie der zusätzliche
Check-config-Oneoff werden getrennt betrachtet. Diskrete und pro Container sequenzielle Samples sind keine
synchrone kontinuierliche Peakmessung; kurze Init-/Endphasen können zwischen Samples liegen.
Hostdiskdelta ist keine isolierte DBwachstums- oder Backup-/Restoremessung.
Dieser Authpilot allein ersetzt weder Backup-/Restorepeak, TT-Spitzenlast,
45-Minuten-Formularabnahme noch gemeinsame reale Keycloak-Gruppen. Alle bleiben
Abnahmegates vor einer Installation auf dem derzeit RAM-knappen dev01 oder einer
menschlichen Ressourcenentscheidung. Schutzlimits werden bei roten Tests nicht
still erhöht, Sandbox-/TLS-/Authschutz wird nicht gelockert.
