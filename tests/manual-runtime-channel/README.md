# Kombinierte manuelle Runtime-Kanalqualifikation

**Source vorhanden, tatsächliche native Abnahme noch offen.** Import und normaler
CLI-Aufruf sind inert. Reine Tests starten weder Docker noch SSH, PostgreSQL oder
API. Jede tatsächliche Hosted-Ausführung benötigt Roots separate einmalige Lease;
der Input allein ist keine Betriebsfreigabe. Keine Production oder privaten Ziele.

## Bekannter CI-Aufrufpfad

Der bestehende registrierte `ci.yml`-Opt-in `pg_channel_opening` behält dieselben
sechs öffentlichen Felder: `mode`, `sourceSha`, `sourceTree`, `workflowSha256`,
`nonce`, `expiresAt`. Keine Secrets. `pg-channel-v1` delegiert nach zusätzlicher
Sourceprüfung unverändert an die bestehende PG-Fixture. `runtime-channel-v1`
startet nur diese kombinierte Fixture. Andere Modi, doppelte/zusätzliche Felder,
Alt-Pilot-Mischung, falscher SHA/Tree/Workflow, abgelaufene Öffnung oder Attempt >1
werden vor Docker abgewiesen. Die acht normalen CI-Jobs und deren Schritte
bleiben unverändert. Nur der bereits bekannte isolierte Kanaljob benutzt den
neuen festen Einstiegspunkt; kein neues Workflow-Registrierungs-/Main-Gate.

Die unveränderlichen a7fc-AMD64-API- und PG17-Digests samt Config-IDs stehen als
feste öffentliche Konstanten in der Fixture. Kein Build/Publisher, kein Tag als
Runtimebindung, kein optionaler alter C#-Feeder. Die APIquelle a7fc und die jeweils
aktuelle Operator-/Fixturequelle werden ausdrücklich getrennt protokolliert.
Der APIprozess läuft explizit als 1654:1654, PostgreSQL als 999:999; das
Keyringvolume wird für 1654:1654 vorbereitet. Image-, Container-, Mount-, ENV-,
Ressourcen- und Ownerreadback erfolgen über die vorhandenen Runtimeports.

## Vorgesehene tatsächliche Fälle

1. Frisches eigenes Namespace, drei gelabelte Volumes und genau ein eigenes
   Bridge-Netz `/28`, keine Hostports. Synthetische Zugangsdaten nur im RAM und
   über den vorhandenen begrenzten STDIN-Kanal.
2. Aktueller PGadapter: Ownership, initdb, SCRAM-Socket, unveränderte SQL01/02.
   Tatsächlicher Runtime-Login und vollständige Rollen-/ACLprüfung.
3. Terminaler Migrator derselben API: Historie exakt 1 bis22, danach erneut ACL.
   API-STDIN-/EOF-Start, exakter Own-Inspect und echte Readiness mit PostgreSQL,
   Head22/Pending0, FEEL und anonymer401. CLI0 oder CID allein genügen nie.
4. Erfolgreiche beendete eigene Helpers werden durch die Fixture separat und
   eindeutig entfernt. Restartobserve muss bei noch laufender alter API vor
   neuer Secretlieferung fehlschlagen.
5. Erst danach explizit eigene registrierte API und PG stoppen. Daten/Socket/
   Keyring bleiben erhalten. Neustart mit frischer App-Nonce und frischer
   RAMlieferung derselben Credentials, ohne initdb/Volumecreate/Bootstrap oder
   Rotation; Migration und APIproof nochmals über dieselben Produktports.
6. Einmaliger begrenzter Abschluss nur eigener vollständiger Owner-/Source-/
   Run-/Nonce-/Image-/Namensbindungen: Container, Volumes und Netz müssen
   tatsächlich fehlen. Fremde Ressource oder unbekannter eigener Writer bleibt
   UNKNOWN; keine Adoption, kein Prune, kein automatischer Retry.

Der HTTPS-Authoritywert ist absichtlich `https://127.0.0.1/realms/TickyTask`
**innerhalb der API-Netzwerknamespace**. Das ist ein unerreichbarer synthetischer
Loopback; der aktivierte Directoryabgleich darf klassifiziert fehlschlagen.
Keine externe DNS-/Keycloak-Anfrage, keine deaktivierte TLS-/JWTprüfung. Daraus
folgt **kein** Keycloak-, Login-, Directory-, HTTPS-, TT- oder 45-Minuten-Beleg.
Die festen Flowzer-/TT-Ursprünge sind lediglich unveränderte Konfigurationswerte;
die Fixture ruft diese öffentlichen Systeme nicht auf.

## Grenzen und Belege

Nur frischer GitHub-Hosted-Linux-X64-Runner, maximal12 Minuten Job,
420 Sekunden aktiver Fixturekanal und45 Sekunden eigenes Cleanup. Pro Transport
16KiB Eingabe, je64KiB Ausgabe; APIreadiness insgesamt30 Sekunden. Höchstens
acht eigene Container, drei eigene Volumes und ein eigenes Netz. Keine fremden
oder produktiven Prozesse stoppen. Geheimwerte, Rohfehler, Containerlogs,
Konfigurationsdumps und Secret-Hashes werden nicht gespeichert.

Das sichere Artifact bleibt am vorhandenen festen Pfad
`flowzer-pg-channel-proof/proof.safe.json`. Es unterscheidet den kombinierten
Modus ausdrücklich über `kind`, enthält getrennte Source-/Imagepins und nur
feste Statusfelder/numerische eigene Handles. PASS benötigt alle Fälle plus
Cleanup ABSENT und keine unbekannten lokalen Handles. ARM64, private Cool3-
Installation und die eigentliche Demo-/Browserabnahme bleiben eigene Schritte.

## Vollständige Labelbindung

Docker erbt bei Containercreate sämtliche Imagelabels. Vor Secretlieferung wird
daher die vollständige begrenzte Labelmap aus der bereits exakt SHA-/Config-/
Architektur-/Revision-gebundenen API gelesen. Reservierte eigene Labelschlüssel
im Image sind verboten. App-/Migratorinspect und Fixturecleanup verlangen die
exakte vollständige Union dieser Imagelabels mit den eigenen Run-/Ownerlabels;
fehlende, zusätzliche oder veränderte Labels bleiben gesperrt. PG-/Netz-/Volume-
bindungen bleiben unverändert. Der historische erste kombinierte Hostedversuch
39512694409 scheiterte beim Migratorreadback; dessen Cleanup bleibt UNKNOWN,
nicht nachträglich zu ABSENT umgedeutet. Dieser Sourcefix ist noch kein neuer
nativer Beleg.

## Versionsgebundener Client27, kein Systemdowngrade

Der STDIN-only-Kanal ist zunächst ausdrücklich nur für **DockerCLI27.5.1**
qualifizierbar. DockerCLI29 wartet in diesem Fall nach Eingabe-EOF auf das
Containerende und liefert keine frühe CID. Ein echter eigener AF_UNIX-Fake
reproduziert das als **negativen29-Kompatibilitätsbeleg**, nicht als27-Nachweis.
Die spätere echte27-Positivprobe ist im gebundenen Hostedlauf Pflicht. Der
historische Cool3-Client27-Snapshot ersetzt keine frische private Attestierung.
`RuntimePorts.observe` sperrt alle nicht exakt27.5.1 meldenden Clients vor
Ressourcen-/Secretaktionen. Kein spekulatives `--detach` oder `start -i`.

Nur `runtime-channel-v1` bereitet über `--prepare-client-only` einen temporären
Client vor. Der alte PGmodus bleibt ohne Download. Der öffentliche Paketpin
`docker-cli27-package.json` wurde unabhängig über offiziellen Docker-Trustanchor,
GPG-validierte InRelease und deren authentifizierten Packages-Digest geprüft.
Das exakt15235356Byte große Ubuntu24.04/amd64-CLI27.5.1-Paket ist mit SHA256
`cf45b119c819bf5a19dbd9a943d33d4a0b0731b461de1b6cc51d0eb57a822ab7`
gebunden, nicht bloß durch den späteren Download selbst gehasht.

Der spätere einzige HTTPS-Download ist auf60 Sekunden/16MiB/HTTP200 begrenzt,
ohne Redirect-/Paket-/Programm-Fallback. Nach vollständiger Paketauthentifizierung
wird ausschließlich der reguläre ELF64LE/amd64-Client `usr/bin/docker` extrahiert
(max64MiB, höchstens512 Archiveinträge/96MiB expandiert). Keine Systeminstallation,
kein sudo, kein globaler PATH-/Kontext-/Konfigurationswechsel, kein Appimagebau.
Im exklusiven eigenen `RUNNER_TEMP/flowzer-cli27-verified` gelten Verzeichnismodus
0700, Client0500, öffentliche Bindingdatei0400, eigener UID und genau ein Link.
Der unabhängig aus dem signierten Paket abgeleitete Binärdigest steht ebenfalls
fest im Sourcepin:41798992Bytes, SHA256
`c48d48fe308ada474242812a4aaf9652af1e43e48987ef59b3f13a15a2db5be1`.
Die unabhängige DATA-only-Prüfung desselben signiert authentifizierten Pakets
hat ausschließlich diese reguläre ELF im RAM gelesen, nicht ausgeführt.
Ein gemeinsam geänderter Client und Binding-Selbsthash wird
damit abgewiesen. Vor jeder Verwendung werden Inode/Device/Größe/Modus/SHA
gegen diesen Pin geprüft und derselbe
geöffnete FD über den lebenden eigenen Parent bis zum terminalen Pipeabschluss
gehalten. Alle übrigen Dockerargumente, Secretkanäle und Transportgrenzen bleiben
unverändert; auf die ungebundene System-CLI wird niemals zurückgefallen. Der FD
bindet die Pfadidentität, versiegelt aber nicht den Inode gegen spätere In-place-
Änderungen eines gleich-UID-Prozesses. Der eigene Runner einschließlich Source
ist vertrauenswürdig; dies ist keine Isolation gegen beliebigen Code desselben
Benutzers oder Root. Diese Vertrauensgrenze wird nicht als Sandbox attestiert.

Der Safeproof projiziert künftig die tatsächliche numerische CLIversion und
öffentliche Paket-/Binärdigests, den ersten unveränderten festen Transportcode
mit seinem festen Kommandocode sowie einen tatsächlich validierten terminalen
Head22-Migrationsmeilenstein sofort. Keine Rohfehler, Header, Konfigurationen,
Body-/Secretbytes oder Secret-Hashes. Die alten fehlenden Diagnosen/`actualHead`
null werden nicht rückwirkend umgedeutet. Der zweite historische Hostedlauf
39513937719 bleibt API_START FAILURE, damals Cleanup tatsächlichABSENT; die
konkrete damalige CLIversion/Transportursache ist im alten Proof nicht enthalten.


## Prepare-Abbruch vor Runtime

Bereits der Prepare-CLI-Aufruf gibt bei Fehlern einen festen Marker
`CLI27_PREPARE_SAFE` mit genau fünf sicheren Feldern aus: `stage`, `exit`,
`httpStatus`, `transportCode`, `ownPid`. Stufen sind SOURCE, DOWNLOAD, HTTP,
SIZE, HASH, ARCHIVE, STORE und intern COMPLETE. Exitcodes bleiben nullable
Ganzzahlen von−255bis255, HTTPstatus nullable100bis599; Transportcodes stammen
unverändert aus dem festen Transportenum, OwnPID ist nullable positive32Bit.
Kein Exceptiontext, Downloadinhalt, URL, Header, Umgebungs- oder Konfigurationsdump.
Bei unbekanntem eigenen Writer wird kein zusätzlicher Dateilöschschritt gestartet.
Die Diagnose ist **kein Runtime-/Cleanup-Beleg**. Ein Prepare-Abbruch erzeugt
weiterhin kein erfundenes Runtimeartifact: fehlendes Artifact bleibt UNKNOWN.
Der historische Lauf39518761226 scheiterte vor den Pure-/27-Positiv-/Runtime-
Schritten; seine konkrete Prepare-Ursache wurde nicht belegt und bleibt UNKNOWN.
Ein Diagnosefix ist keine Berechtigung zu Download, Ausführung oder neuem Dispatch.


## Offizieller vierter ar-Member

Die einmalige authentifizierte DATA-only-Prüfung des tatsächlich gepinnten DEB
hat neben `debian-binary`, `control.tar.xz` und `data.tar.xz` den abschließenden
Signaturmember `_gpgbuilder` (1248Bytes) belegt. Der Parser ignoriert ausschließlich
diesen bekannten vierten Member, höchstens2048Bytes und nur nach den drei
Datenmembern am Archivende. Er wird weder extrahiert noch ausgeführt oder als
Vertrauensanker verwendet. Unbekannte, doppelte, zu große und falsch platzierte
Member bleiben gesperrt. Die äußere authentifizierte Paket-SHA und die feste
ELF-SHA-/Größenbindung ändern sich nicht. Der Datenbefund erklärt einen kausalen
aktuellen Sourceblocker, beweist aber ohne nativen Diagnosemarker nicht rückwirkend
die Ursache der historischen Preparefehler. Weitere echte Downloads oder
Hostedversuche bleiben gesondert freizugeben.
