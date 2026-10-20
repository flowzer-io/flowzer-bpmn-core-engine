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
Migrationsstand ist **20**, nicht der unabhängige21-Beispielwert der Fakes.
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
