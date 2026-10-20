# Isolierter Bootstrap-Test-Feeder – Source, noch keine Runtimeabnahme

Genau zwei eigene wegwerfbare Container: PG/SCRAM am gemeinsamen Unixsocket und
Feeder/API, kein Hostport/Hostnetz/Dockersocketmount. Die Adapterquelle ist vorhanden,
aber **in diesem Stand nicht tatsächlich ausgeführt**. Alle aktuellen Tests ersetzen
Docker-/DB-/Prozess-/HTTP-/Clockränder durch Fakes. Keine Pipeline oder Produktdatei
wird hierfür fachlich verändert. Der tatsächliche Source-Lauf39434711735/a1
am0a4fe7a enthält die Feeder-Fakes und ist8/8grün; das ist kein Positivpilot.

## Reine Source-Tests

```sh
python3 -I -B tests/runtime-bootstrap-smoke/test_run_smoke.py
# Nach gebundenem eigenem Restore/Build nur dieses Projektes:
/absoluter/dotnet /eigener/output/SmokeFeeder.dll --contract-test
```

Das kleine net10.0-Projekt referenziert nur Npgsql10.0.3 wie der bestehende Provider;
kein Testframework, API-/DB-/Dockerstart, keine Installation oder Auflösung privater
Feeds. Der C#-Testeinstieg erfolgt vor jeder Eingabe-/Transportinitialisierung.
Source-Tests geben nur feste IDs/Counts aus, nie rohe Exceptions/Sentinelwerte.
Der Npgsql-ConnectionStringBuilder wird ausschließlich als reine Datenoperation geprüft.

## Späterer ausschließlich ausdrücklich gebundener Aufruf

Ohne vollständige Argumente bleibt die CLI mit `FLOWZER_SMOKE_SOURCE_ONLY`/Exit78
zu. **Die folgenden Aufrufe sind Dokumentation, keine Ausführungsfreigabe:**

```sh
python3 -I -B tests/runtime-bootstrap-smoke/run_smoke.py \
  --bound-run /absolut/root-opening.json <sha256-der-exakten-datei> <nonce32hex>
# Nur der besitzende Parent konstruiert danach innerhalb des eigenen Containers:
# dotnet /app/SmokeFeeder.dll --bound-fixture <nonce32hex> <api-dll-sha256> <feeder-dll-sha256>
```

Die nichtgeheime JSON-Bindung enthält exakt die in `validate_binding` aufgeführten
Felder: Version1/Root-Owner/Nonce/maximal300sGültigkeit, eigenen Unix-Dockerhost mit
Daemon-ID, absoluten gehashten Dockerclient, unveränderliche PG-/eigene APIimage-
Identitäten, UID/GID, API-/Feeder-/Native-SHA, tatsächliche Runtime und erwartete
**numerische** Migration. Hash/Ownerlabel sind Identitäts-/Verwechslungsschutz,
**keine Signatur und keine neue Autorisierung**: nur ein vertrauenswürdiger Operator
mit separat dokumentiertem Root-Go darf diese lokale CLI bedienen, niemals ein
unvertrauenswürdiger Request oder öffentlich angebotener Dienst.

Der Parent erzeugt nur neue synthetische Passwörter im RAM. Passwortweitergabe
nur per stdin/EOF, keine ENV/Argument-/Secretdatei. PG-Volume ist eigener tmpfs,
Rootfs des APIcontainers readonly, Logtreiber none, keine Credentials/Loaderhooks
geerbt. Images müssen bereits vorhanden sein: kein Pull/Login/Buildcode. Das eigene
Socketvolume erhält seine numerischen Rechte über lokale tmpfs-Optionen, nicht
über privilegierte/chown-Hilfscontainer. Jeder Inspect wird gegen den tatsächlichen
CID und feste Flags/Command/Env/Mounts geprüft; Unknown erzeugt keinen Retry.
Vor Volume-create muss der exakte Name nachweislich fehlen; ein passender Altbestand
wird weder übernommen noch gelöscht. Die spätere Rootöffnung muss zusätzlich einen
exklusiven Writer-Slot für diese frische Nonce garantieren: Docker bietet hierfür
kein atomares Create-if-absent. CLI-Handles werden vor dem Start registriert;
Fehler während der Pipe-/Selector-Einrichtung führen zu begrenztem eigenem Cleanup.
Ein unbekannt beendeter Handle bleibt registriert und verhindert die Erfolgsmeldung.

C# prüft innerhalb dieses eigenen Containers Artefakt-/Runtime-/Nativebindung,
legt mit einem einmaligen Fixtureadmin genau die festen Rollen/DB/Schema an und
führt genau einen Migrator und eine API über getrennte stdin-Pakete aus.
Rollen-/Historienvertrag folgt `deploy/postgresql/01…`/`02…`: Runtime kein DDL/
Superuser/CreateRole/CreateDB; Migrationshistorie nur lesbar, auch bei Defaultrechten.
JSON-Readiness ist nicht bloß Healthy: PostgreSQL/UpToDate/Pending0/erwarteteVersion/
Feel und geschützter anonymer GET401 zählen. Authority ist ausschließlich synthetisch,
HTTPS-Metadaten/JwtBearer/Rollen bleiben an; externe Effekte sind explizit aus.

SDK-Auswahl und Runtimeauflösung sind getrennte Fragen. Bestehende erfolgreiche
Linux-CI-/Imagedigests sind Ausgangsbelege, nicht der neue stdin-Positivnachweis.
Kein Patchgleichheitsziel und keine RollForward-Änderung: deklarierte Frameworks,
tatsächlich vorhandene Runtime, bestehende Auflösungsregel und native SHA werden
vor dem künftigen Start geprüft. Die lokale Mac-Kompilation ist keine Linux-ABIabnahme.

## Grenzen und Fehlerabschluss

Kanalbudgets/Fristen/EOF sind harte Gates, inklusive Klartext/Base64/doppeltem Base64
auf beiden Kanälen. Frühes PG-stdin-EOF ist **kein** Beleg für terminalen PGoutput;
dieser wird bis zum Cleanup kontrolliert. Jeder unbekannte Ressourcen-/Gruppen-
abschluss bleibt FAIL/UNKNOWN. Nur nach tatsächlichem eigenen Gruppen-/CID-/Volume-
abschluss wird der eigene Parent-Tempbereich entfernt, sonst mit sicherem Handle-
beleg erhalten. Kein Adoptieren fremder Prozesse/CIDs oder automatisches Force-Retry.
Fachlicher Fehler und Schutzabbruch sind kein Rollbackversprechen. .NET-/Python-
Strings sind nicht sicher löschbar; nur begrenzte Bytebuffer werden bestmöglich genullt.

**Offener Reviewpunkt P3:** Der C#-Feeder sendet TERM nach `HasExited` an eine
numerische Kind-PID. PID-Wiederverwendung zwischen Prüfung und Signal ist nicht
ausgeschlossen. Das Risiko liegt ausschließlich im eigenen exklusiven APIcontainer
mit privatem PID-Namensraum, nicht als Hostsignal belegt. Ein stabiler Handle ist
damit nicht bewiesen. Vor einem Live-Go muss der Koordinator diesen Restpunkt mit
dem tatsächlich eigenen CID-/Timeout-/Cleanupvertrag ausdrücklich entscheiden.
Der Quellabschluss ist keine Live-Risikoübernahme; ein neuer nativer Supervisor
oder eine vermeintliche Behebung durch `Process.Kill` wurde bewusst nicht ergänzt.

Vor einem wirklichen Positivlauf fehlen weiterhin unabhängiger neuer Source-Review,
Linuxpublish-/Image-/Framework-/ABI-/Ressourcenattestierung, frische Rootöffnung und
reale EOF-/Own-IPC-/Leak-/Cleanup-Einzelabnahmen. Die konkreten Adapter sind bislang
nur kompiliert beziehungsweise statisch und mit Fake-JSON/FDs geprüft, **nicht live**.
#375, nativer Coolify-RED, produktiver Feeder/Restart, TT-Imageparität und echte
Keycloak-/HTTPS-/45-Minuten-Formular-/Demoabnahme bleiben separat offen.

## Implementierter CI-Anschluss, separat qualifizierte Source-CI

Im bestehenden dotnet-Job ist der schlanke Anschluss über
`scripts/ci/run_runtime_smoke_contracts.py` implementiert: die tatsächlichen
19 Python-Fakes sowie ein isolierter öffentlicher Restore/enger Build dieses
kleinen unveränderten Projekts und `--contract-test` mit Count15/Exit0/NoSkip.
Der lokale tatsächliche Helferlauf hat Python19/19 und C#15/15 bestanden; die
separaten Helper-/Verdrahtungstests verwenden reine Mocks. Source-CI39434711735/a1
und CodeQL39434706160/a1 sind am0a4fe7a tatsächlich8/8 beziehungsweise4/4grün.
Das qualifiziert nicht die danach ergänzten Pilotträgerbytes. Die bisherigen
13-/17-Bootstrapverträge bleiben unverändert.
Kein neuer Job/Workflow/Matrix, kein automatischer Container-/Positivlauf.
Details und Frameworkauflösungsvertrag stehen in `scripts/ci/README.md`.
Das ist ausdrücklich kein Runtime-/Demo-Go; P3 bleibt offen.

## Optionaler späterer öffentlicher Hosted-Pilot – noch nicht ausgeführt

Der vorhandene `dotnet`-Job kann die einzige lokale Composite-Assemblierung und
den unveränderten `run_smoke.py --bound-run` tragen. Kein weiterer Workflow/Job,
kein neuer Runtime-Runner. Routine-PRs führen ausschließlich die zusätzlichen
synthetischen Trägertests aus; der normale Dispatch bleibt ebenfalls geschlossen.
Ein eigener Test führt nur die tatsächliche Bash-Dateikollisionsprüfung gegen
temporäre öffentliche Bytes aus, niemals Docker, SDK oder Netz.

Nur ein gesondert freigegebener `workflow_dispatch` mit nichtleerem Input
`stdin_pilot_opening` aktiviert den Träger. Die nichtgeheime JSON-Lease verlangt
exakt `version`, `owner`, frische `nonce`, `sourceSha`, `sourceTree`,
`workflowSha256`, `expiresUnix` (höchstens30Minuten) und `p3Accepted=true`.
Nur dieser öffentliche Repository-/Topic-Kontext, Linux/X64, GitHub-hosted und
Attempt1 sind zugelassen. Diese Angaben sind keine Signatur/Autorisierung;
der konkrete Root-Slot bleibt vor jedem Dispatch erforderlich. Kein stiller
Wechsel auf Dev02, private Runner, Coolify oder Production.

`Dockerfile.pilot` nutzt fest gepinnte öffentliche SDK-/ASP-Basen, veröffentlicht
API und Feeder separat und vereinigt gemeinsame Dateien ausschließlich bei
Bytegleichheit. Keine Produkt-, Auth-, Migrations-, RollForward- oder
ENV-Allowliständerung. Der Träger liest die tatsächlichen Build-Pins und neue
lokale Image-ID, validiert Runtimeconfigs und bildet erst dann die persönliche
19-Feld-Openingdatei des bestehenden Parents mit maximal300sTTL. Der erwartete
Migrationsstand war auf main **20**. In der jetzigen TT-Komposition ist er
quellgebunden **22**, nicht der unabhängige21-Beispielwert der Fakes.
Der positive Lauf39482867524/a1 bleibt ein historischer main-/Head20-Beleg,
kein tatsächlicher Positivlauf dieser neuen Komposition.
Die gepinnten Images verwenden PG-UID999 und ASP-UID1654, gemeinsamen GID22000;
Root muss diese konkrete Auswahl mit dem Image-/Socketvertrag freigeben.

Vor Writes:2CPU,2GiB MemAvailable-Reserve,4GiB freier Arbeitsraum, exakte Namen
abwesend. Genau drei öffentliche digestgebundene Pulls, ein Compositebuild,
ein bestehender Parentaufruf; kein Login/Push/Retry. Die Pulllimits sind je120s,
Build300s, Parent420s und Namensreads10s; die Schrittfrist beträgt20Minuten.
BuildKit wird nicht als separat cgroup-begrenzter Build behauptet. Runtimecaps
bleiben PG256MiB/64PIDs, API768MiB/128PIDs und je1CPU; kein Null-Swapversprechen.
Kein Timeout ist ein Cleanupbeleg. Bei Fehlern bleibt der eigene Arbeitsbereich
erhalten; der Träger liest ausschließlich die eigenen Namen nach und stoppt
niemals unklare/fremde CIDs. Erfolg setzt unabhängig gelesene Namensabwesenheit
voraus. Die eigene lokale Compositeimage-/Basisimagecachebelegung bleibt bis zur
Wegwerf-Runnerentsorgung erhalten, nicht als Registryveröffentlichung.

Die bislang unbekannten tatsächlichen Linux-DLL-/Native-/Inventar-/Imagepins
entstehen erst bei einer später freigegebenen Assemblierung. Kein Hash wird aus
Macoutputs oder Riglogs als neuer Pin eingesetzt. Das offene numerische PID-P3
ist ausschließlich für den später frisch erzeugten exklusiv eigenen APIcontainer
mit privatem PID-Namensraum eng akzeptiert, nicht erledigt und kein stabiler
Handle. Ohne konkrete Run-Lease keine Ausführung. Dieser Source-Stand ist lokal
synthetisch zu testen/reviewen, aber weder gepusht noch Hosted-/Runtime-abgenommen.

## Tatsächlicher Hosted-Erstpilot und enge Diagnosekorrektur

Der genau einmal gestartete Hosted-Lauf **39470222794/a1** am Quellstand
`0a7e103a41284bcad7818b424c695c1e46a40b6e` ist **fehlgeschlagen**. Die beiden
vorgelagerten Fake-/Startvertragsschritte waren erfolgreich; der Pilot selbst
meldete nur `PILOT_REJECTED`, Exit2 und `cleanup=CHECK_REQUIRED`. Aus dieser
pauschalen Ausgabe sind Fehlerphase, tatsächliche Compositeimage-/Nativepins,
Migration20, EOF, Leakprüfung und eigener CID-/Volumeabschluss **nicht belegt**.
Runner-Postjob-Cleanup ist kein Ersatzbeleg. Kein Retry und keine Demoabnahme.

Der folgende enge Sourcefix ergänzt ausschließlich die Diagnose im vorhandenen
Träger: feste Phasencodes, numerische Exit-/Timeout-/Ausgabelimitmetadaten,
`runtimeState=NOT_STARTED|MAY_HAVE_STARTED|PARENT_SUCCEEDED` sowie unabhängig
gelesenes `ownNames=NOT_CHECKED|ABSENT|UNKNOWN`. Vor dem Parentaufruf bezeichnet
`NOT_STARTED` ausschließlich den noch nicht aufgerufenen Parent/Fixturepfad,
nicht die bereits möglichen öffentlichen Pull-/Buildvorgänge. Schon vor dessen
CLIstart wird konservativ `MAY_HAVE_STARTED` gesetzt. `ABSENT` allein ist weder
CLIgruppenabschluss noch EOF-/Leak-/fachlicher Erfolg. Ein primärer Fehler bleibt
bei zusätzlichem Readbackfehler erhalten; unbekannte Ressourcen werden nicht
adoptiert, gestoppt oder gelöscht. Es gibt keine neue Cleanup-Schreiboperation.

Bereits begrenzt erfasste öffentliche Buildfehler werden nur anhand fester
Kategorien gezählt (NuGetquelle/-Audit, Compiler, SDK, Dateikollision). Unbekannte,
mehrdeutige oder übergroße Ausgaben erhalten `UNKNOWN` beziehungsweise `AMBIGUOUS`;
die Kategorien sind Hinweise, keine Ursachenfeststellung. Keine Rohzeile,
Exceptionnachricht, komplette Befehlszeile, Umgebung oder Konfiguration wird
ausgegeben. Die bestehende harte64KiB-Grenze und alle Fristen bleiben unverändert.
Canary-/Secret-ähnliche Nebeninhalte werden durch synthetische Tests abgesichert.
SharedHelper, Parent, C#-Feeder, Dockerrecipe und Produkt-/Auth-/Schema bleiben
bytegleich. Diese lokalen Diagnose-/Regressionstests führen keine Container aus.
Ein weiterer tatsächlicher Linuxpilot benötigt erneut unabhängigen Sol-Review,
veröffentlichten exakten Freeze und eine frische koordinierte Run-Lease.

Der finale `SYNTHETIC_PILOT_OK`-Beleg wird erst von `main` **nach** erfolgreicher
Entfernung des eigenen Arbeitsbereichs ausgegeben. Scheitert diese Entfernung,
bleiben Exit2 und ausschließlich `PILOT_REJECTED` mit Phase `WORKSPACE_REMOVE`;
vorheriger Parentabschluss/Namensabwesenheit werden getrennt dokumentiert und
nicht zu einem erfolgreichen Gesamtabschluss umgedeutet. Ein synthetischer
Main-Faultfall und ein Execute-Test sichern diese Reihenfolge ab.

## Zweiter getrennter Hosted-Pilot und Parent-/Feederdiagnose

Der neue, separat koordinierte einmalige Lauf **39473467380/a1** auf
`df200e0077c86208436e7aaceefd470c63aabb03` ist ebenfalls **FAILURE**. Der äußere
Träger belegt jetzt `failurePhase=PARENT`, Kind-Exit2 und konservativ
`MAY_HAVE_STARTED`. Der abschließende eigene Namensreadback blieb `UNKNOWN`
(`VALIDATION`), damit ist auch der Cleanup **nicht** bestätigt. Eine konkrete
Kindfehlerursache, tatsächliche Artefaktpins, Native-/Migration20-/EOF-/Leak-
Abnahmen sind aus diesem Lauf weiterhin nicht veröffentlicht/belegt. Beide
alten Nonces/Leases sind verbraucht; kein Retry oder Zugriff auf alte Runner.

Der folgende lokale **Acht-Pfad-Diagnoseschnitt** ändert keine Produkt-, Auth-,
Signal-/PID-, Rechte-, Timeout-, Ressourcen- oder Cleanupregel und senkt keinen
Erfolgs-/Leakguard ab. Der Python-Parent beobachtet feste vorhandene Schritte
(Preflight, Volume, beide Create-/Inspectränder, PG-stdin, Feeder und Cleanup)
sowie ausschließlich bestehende `SmokeFailure`-Kategorien. Vor **jedem** möglichen
Volume-/Container-/Startwrite steht konservativ `MAY_HAVE_STARTED`: Auch ein
unklarer Createausgang ohne zurückgelieferten CID wird niemals `NOT_STARTED`.
Primärfehler und Cleanupzustand bleiben getrennt. Inspectabweichungen nennen nur
eine endliche Vertragsgruppen-ID, niemals Istwerte aus Docker-/ENV-/Configdaten.
Maximal zwei syntaktisch geprüfte CIDkandidaten sind an die festen Rollen `pg`
und `api` gebunden; `VERIFIED` beschreibt eine belegte Inspectprüfung, nicht
fortbestehende Existenz oder eine spätere Cleanupberechtigung. Bei erneut unklarer
Inspectprüfung wird der Kandidat wieder `UNKNOWN`. Kein Adoptieren/Force-Cleanup.

Der C#-Feeder projiziert ausschließlich vorhandene Failure-Enums und feste
Phasen (`Input`, `Binding`, `Provision`, `Migration`, `ApiStart`, `Readiness`,
`Cleanup`) sowie einen getrennten Cleanupzustand. Eine zusätzliche Cleanupstörung
überschreibt die Diagnose des Primärfehlers nicht; die bisherigen Exception-/Exit-
und Erfolgsbedingungen bleiben erhalten. Es gibt keine Roh-Exceptiontexte oder
Paket-/Prozess-/Providerinhalte. Der Parent prüft zuerst vollständige begrenzte
Kanäle und alle Klartext-/Base64-/Doppelbase64-Canaries; fehlendes EOF/Terminal,
Timeout, mehrdeutiges/abgeschnittenes JSON oder unbekannte Kategorien erlauben
keine validierte Fehlerprojektion. Ein projizierter Fehler bleibt ein Fehler.

Der äußere Träger übernimmt ausschließlich die strikte endliche Parentprojektion
mit passender eigener Nonce, festen Rollen und maximal zwei CIDkandidaten.
Unbekannte/mehrdeutige Formen bleiben `UNKNOWN`. Schon vollständig geprüfte
Image-/API-/Feeder-/Native-/Inventar-/Dockerbinarypins werden auch im Fehlerfall
als formatgebundene Provenienz verfügbar; die Daemonidentität wird nur gehasht
geführt. Keine Rohconfigs/ENV/Logs/Befehle. Diese Pins belegen Assemblierung,
nicht tatsächliche Nativeausführung oder fachlichen Prozessfortschritt.

Die bestehenden **19 Python-Parent-IDs und 15 C#-Vertrags-IDs** bleiben erhalten;
neue Negativ-/Canary-/Primärfehler-/Cleanupfälle erweitern ihre Assertions.
Die C#-Verträge werden tatsächlich mit vorhandenem SDK10.0.100 aus
`/usr/local/share/dotnet` im eigenen isolierten Artefakt-/Paketbereich ausgeführt.
Die zunächst irrtümliche SDK10.0.401-Auswahl endete vor Build mit Exit155 und ist
**kein** fachlicher RED. Der folgende echte RED mit kompatiblem SDK fehlte an den
noch nicht implementierten Diagnosetypen, danach sind neue Builds/Verträge separat
zu belegen. Keine Installation, global.json-/RollForward- oder Systemänderung.
Quellreview/Veröffentlichung und ein weiterer tatsächlicher Linuxpilot benötigen
jeweils neue konkrete Freigaben. Der frühere numerische C#-PID-P3 bleibt offen.

Die unabhängige erste Acht-Pfad-Sol-Prüfung fand drei reine Diagnose-P2:
überschriebene CLIexitmetadaten, verspätet festgehaltene Cleanupfehlerphase und
ein fälschliches `COMPLETE` nach unklarem Volume-Create. Drei echte Python-Fake-
REDs sichern nun die Korrektur: erster primärer Toolbeleg und erster Cleanup-
Toolbeleg tragen jeweils ihre feste Phase getrennt; die erste Cleanupstörung
wird unmittelbar festgehalten; ohne belegten Volume-Ownership-/Abschluss bleibt
Cleanup `UNKNOWN`. Die eigentlichen Cleanupaktionen/Guards bleiben unverändert.

## Enger Docker-NotFound-Kompatibilitätsfix

Der dritte getrennte Lauf **39478305682/a1** auf `fd22271` ist **FAILURE**.
Jetzt ist der Parentfehler konkret `CLEANUP_REMOVE / CLEANUP`, CLIexit1,
beide CIDkandidaten/Own-Namensreadback und Gesamtcleanup `UNKNOWN`. Es ist kein
vorheriger fachlicher Parentfehler protokolliert: Aus dem unveränderten Sourcepfad
folgt als **Inferenz**, dass der feste Feeder-Erfolg/Exit0/EOF-/Canaryguard vor dem
Cleanup erreicht wurde. Das ist kein direkt emittierter Einzelnachweis und keine
Gesamt-/Native-/Migration20-/Demoabnahme. Der tatsächliche Fehlertext fehlt.

Die offiziellen Primärquellen zeigen einen bislang nicht erkannten exakten
CLIvertrag: `container inspect` reicht APIfehler weiter; der Moby-Client setzt
`Error response from daemon` davor. Siehe
[DockerCLI](https://raw.githubusercontent.com/docker/cli/v28.5.2/cli/command/container/inspect.go),
[Inspect-Client](https://raw.githubusercontent.com/moby/moby/v28.5.2/client/container_inspect.go)
und [APIfehler-Projektion](https://raw.githubusercontent.com/moby/moby/v28.5.2/client/request.go).
Diese Quellen beweisen nicht die konkret im dritten Hosted-Piloten verwendete
Docker-Version/Fehlerzeile. Der aktuelle Fix erkennt zusätzlich ausschließlich
`Error response from daemon: No such container: <exakter eigener voller CID>`.
Kein Substring-/Wildcard-Matcher, kein fremder CID/Name, keine Permission-/Transport-
oder Mehrzeilen-/Zusatzfehlertoleranz. Alte exakte Create-Namen-/Volumeverträge
bleiben erhalten. Abwesenheit verlangt strikt den Integerexit1 und leeren stdout
oder ein tatsächlich leeres JSONarray mit bloßem JSONwhitespace; beliebige
Klammerfolgen, Code0/125/bool und beschädigte/canarybelastete Ausgaben sind kein
Abwesenheitsbeleg. Registrierter eigener voller CID wird vor dem Inspect geprüft.

Die sichere CLI-Operation-ID unterscheidet nun `CONTAINER_REMOVE` vom anschließenden
`CONTAINER_INSPECT`, getrennt je Primär-/Cleanuplane. Keine Argumente/Rohmeldungen.
Alle Ressourcen-/Timeout-/Signal-/PID-/Eigentums- und eigentlichen Cleanupaktionen
sowie C#-/Produkt-/Workflow-/Dockerrecipebytes bleiben unverändert.

Zusätzliche **synthetische echte CLI-Reproduktion**, ausdrücklich kein Runtimepilot:
Der lokal vorhandene DockerCLI29.8.2 erhielt mit leerer eigener Umgebung/Config
nur `--host` auf einen eigenen privaten AF_UNIX-HTTPfakesocket. Zwei Requests
(Ping und fester eigener 404-Testpayload), Exit1, wirklich leeres JSONarray und
exakter kanonischer daemonpräfigierter NotFoundtext wurden tatsächlich geprüft.
Kein realer Dockerdaemon, kein Container/Image/Pull, kein Hostport. Eigene CLIgruppen,
Serverthread, Socket und Arbeitsbereich sind belegbar geschlossen/entfernt.
Diese Reproduktion beweist den CLIvertrag auf diesem Fake, nicht rückwirkend die
Fehlerursache oder den Cleanup der drei verbrauchten Hosted-Leases.
