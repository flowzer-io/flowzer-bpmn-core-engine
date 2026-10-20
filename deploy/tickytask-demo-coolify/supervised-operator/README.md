# Manueller Operator: STDIN-Kanal und Lebensdauer

**Sourcearbeit, keine Ausführungserlaubnis.** Eigener Cool3-Demokontext, keine
Production. Implementiert sind derzeit der begrenzte lokale Pipe-Transportkern
und die einmalige Initial-/Restart-Sequenz mit injizierten Ports. Hinzu kommen
die reinen SQL-/Readinessprotokolle weiter unten. Der konkrete initiale PG-/SSH-/Dockeradapter ist jetzt als Source vorhanden,
der **historische a7fc-PG-Kanal** ist auf isoliertem Linux/amd64 qualifiziert.
Die heutige Adapterquelle ergänzt weitere IPC-/Inspectprüfungen und ist deshalb
nochmals kombiniert nativ zu qualifizieren. Die konkreten Runtime-/Restartports
sind ebenfalls als Source vorhanden, bleiben
aber vor kombinierter Native-/Privatabnahme geschlossen. Host-/Imageattestierung
und tatsächliche Demo-/Keycloak-/HTTPS-/TT-Abnahme bleiben **offen**.
Die Argumentbuilder sind nur Kanalfragmente ohne vollständige Auth-/Mount-/
Netz-/Ressourcenbindung und dürfen nicht als Betriebsstart benutzt werden.
Die vorherige Komposition `dae1b4d` bleibt ein eigener geprüfter
Checkpoint. Ohne separaten Root-Slot werden weder SSH noch Docker/PG/API gestartet.

## Verbindlicher Transport vor Implementierung

- Kein TTY, kein Shellkommando mit Secretwerten. Lokaler Aufrufer hält Werte nur
  im RAM; das entfernte Kommando besteht ausschließlich aus festen Programmteilen
  und validierten nichtgeheimen Bindungen. SSH besitzt BatchMode, strikte bekannte
  Hostidentität, keine Weiterleitungen oder frei geerbte Hooks.
- Pro Aufruf maximal16KiB Eingabe, danach **EOF**; maximal64KiB je Ausgabekanal,
  feste Gesamtfrist einschließlich Schreiben, Lesen, terminalem Prozess und EOF
  beider Ausgabekanäle. Das geordnete eigene Fehlercleanup hat zusätzlich höchstens
  zwei plus zwei Sekunden; gelingt der Gruppenabschluss nicht, bleibt der Handle
  UNKNOWN. Unvollständiger/unklarer Abschluss ist nie Erfolg.
  Keine rohen Ausgaben/Exceptions in Status, Dateien oder Logs. Klartext-/Base64-/
  Doppelbase64-Echos eigener Eingabesecrets blockieren die Verarbeitung.
- Für länger laufende API: reguläres Docker `run --interactive --attach=stdin
  --sig-proxy=false`, kein `--detach`, kein TTY. Nur stdin wird angehängt. Nach
  Eingabe-EOF liefert der CLI die nichtgeheime CID, ohne das Containerende
  abzuwarten. Docker-Logtreiber `none`, `restart=no`, `--pull=never`.
  **Die CID/CLIexit0 ist kein erfolgreicher APIstart.** Erst separater exakter
  Own-Inspect plus Readiness PostgreSQL/Head22/Pending0/FEEL und anonymer401
  können den fachlichen Start bestätigen.
- Migrator/Initialisierung hängen dagegen zusätzlich stdout und stderr an;
  nur ein tatsächlicher terminaler Containerexit0 zählt. Ein Transportverlust
  darf weder als erfolgreicher Start noch als erfolgreicher Rollback gelten.
- Keine automatische Wiederholung, kein Reparatur-/Adoptions-/Stop-/Löschlauf
  nach UNKNOWN. Insbesondere bleiben bereits erzeugte Daten erhalten. Eigene
  lokale Transportprozesse werden begrenzt beendet; unbekannte entfernte
  Container bleiben mit festen redigierten Handles im Checkpoint.

## Initialisierung ist kein Restart

**Initial:** Eigene PG-/Keyring-Namen müssen fehlen. Ein frisch erzeugtes eigenes
PG-Volume wird genau einmal von einem kurzlebigen `initdb`-Container initialisiert;
Adminpasswort ausschließlich `--pwfile=/dev/stdin` (Pipe, keine Secretdatei).
Erst nach dessen Exit0 startet ein separater PG-Server auf diesem Volume ohne
Secretzufuhr/erneutes initdb, ohne TCP-Listener und mit SCRAM am eigenen Unixsocket.
Anschließend echte eigene Rollen-/DB-Erstellung nach dem gebundenen SQL01/02-
Vertrag, expliziter Migrator22 und zuletzt API. Leere Volume-/Image-UID-/Socket-
Berechtigungen sind vor privater Verwendung tatsächlich zu prüfen.

**Restart:** Nur exakt vorher gebundene eigene PG-/Keyring-/Socketressourcen und
PG-CID; niemals initdb, Volume-create, DB-/Rollenbootstrap oder Passwortrotation.
Eine laufende alte API wird nicht automatisch angefasst. Ein neuer beaufsichtigter
APIstart erhält eine frische einmalige Lieferung; kein ENV-/argv-/File-/Encoding-
Fallback oder automatischer Secretagent. Unbekannte alte API oder fremde Ressource
blockiert. Migration und Readiness müssen auch hier vor APIfreigabe belegt sein.

Für SQL-Passwortauthentifizierung ohne ENV ist psql ohne TTY mit `--password`
vorgesehen: erste stdin-Zeile ist das Adminpasswort, folgende Bytes die gebundenen
SQLbefehle. Der PostgreSQL17-Source liest bei fehlendem `/dev/tty` aus stdin.
Mehrfachconnect, Echo-/Fehlerpfade und neue Volumeownership sind dennoch **nicht
runtimeabgenommen**. Keine Umgehung durch `PGPASSWORD`, `.pgpass` oder Secretdatei.
SQL01/02 dürfen nur in der Initialphase eingebunden werden, keine Übernahme des
wegwerfbaren SmokeFeeder-Bootstraps für persistente Daten.

## Primärquellen und offene Betriebsabnahme

- [Docker27.5.1 run](https://github.com/docker/cli/blob/v27.5.1/cli/command/container/run.go):
  getrennte Attachstreams und Rückkehr ohne Outputattach nach Ende des stdin-Streams.
- [Docker27.5.1 Hijack](https://github.com/docker/cli/blob/v27.5.1/cli/command/container/hijack.go):
  ohne Outputstreams reicht Inputende; normales `start -ai` ist deshalb kein Ersatz.
- [PostgreSQL17 prompt](https://github.com/postgres/postgres/blob/REL_17_STABLE/src/common/sprompt.c):
  kein TTY wird stillschweigend erzeugt, sondern stdin/stderr verwendet.

Das ist ein Source-/Protokollbeleg, keine installierte Binaryparität. Tatsächliche
CLIversion/-bytes, Images/ARM64, Ziel/Owner/Netz/Volumes, PostgreSQL-/psql-Kanal,
Fehlerlogs, HTTPS/Keycloak und langlebige TT-Formulare bleiben vor Runtime zu prüfen.

## Tatsächlicher Sourceumfang dieses Schrittes

`manual_transport.py` implementiert die begrenzten echten lokalen Pipe-/Prozess-
Ränder und die reinen Docker-Kanalargumente. Die Tests benutzen nur eigene lokale
Python-Fixtures, keinen Dockerclient oder SSH. `manual_operator.py` führt die
Initial-/Restartreihenfolge aus, benötigt dafür aber bewusst injizierte Ports;
alle bisherigen Portbelege sind Fakes. Ein `SequenceProof` belegt diese Sequenz,
keine tatsächliche Runtime. CLIaufrufe bleiben fest SOURCE_ONLY ohne private Effekte.

**Aktueller konkreter Stand:** `manual_pg_adapter.py` und `manual_runtime_adapter.py`
verdrahten diese vorhandenen Ports. Der initiale PG-Kanal ist separat qualifiziert;
die kombinierte API-/Migrations-/Restartqualifikation steht noch aus.
Eine eigene fremde/unklare Ressource wird weder übernommen noch gelöscht. Vor
privater Nutzung müssen diese Adapter auf frisch gebundenen tatsächlichen
Metadaten beruhen; es gibt keinen generischen Execute-Endpunkt oder frei
konfigurierbaren Shelltransport. Der gegenwärtige Kern ist kein fertiger Installer.

## Gebundene Protokolle für die konkreten Adapter

`manual_postgres.py` bildet ausschließlich die unveränderten, SHA256-geprüften
öffentlichen SQL01-/SQL02-Skripte auf einen begrenzten initialen stdin-Batch ab.
Der relative Include wird im RAM ersetzt; es entsteht keine entfernte SQL- oder
Passwortdatei. Rollen-/DBnamen bleiben fest, Passwörter werden nur als quotierte
SQLdaten nach explizitem `standard_conforming_strings=on` eingesetzt. Alle
Pakete müssen vor dem ersten Ressourcenwrite vorbereitet und auf16KiB begrenzt
sein. psql `--password` erzwingt auch beim kanonischen `\connect` einen
zweiten Prompt; dafür steht exakt dort eine zweite Adminpasswortzeile im
RAM-Stream, keine SQLvariable/Datei. Ein Restart verwendet diesen Initialpfad niemals. psql-Prompt-/Reconnect-/
EOFverhalten und der Docker-/Logkanal sind trotz reiner Vertragstests noch
nicht auf Linux mit synthetischen Passwörtern abgenommen. Insbesondere darf
der spätere Adapter nicht einen beliebigen stderr-Text als Passwortprompt
akzeptieren oder Rohfehler weitergeben.

`manual_readiness.py` konsumiert den tatsächlichen Health-API-Vertrag und
projiziert nur Healthy/Ready, PostgreSQL, UpToDate/Head22/Pending0, FEEL sowie
anonymen401 auf Felder für `ApiProof`. Bodygröße, doppelte JSONschlüssel und
bool/int-Verwechslung sind gesperrt. Unbekannte Healthdetails verlassen den
Parser nicht. Die Containerhoheit muss vorher separat geprüft werden; eine
positive Healthantwort ist kein Owner-, HTTPtransport- oder Runtimebeweis.

Diese Module sind reine Protokollbausteine, weiterhin kein fertiger Installer.
Persönliche Read-Probes und Root-Permits gehören **nicht** in einen öffentlichen
Commit. Der generische Operator kennt weder persönliche Maschinenpfade noch
private Permit-/Hostbindungen.

## Initialer PostgreSQLadapter (historischer a7fc-Beleg, aktueller Delta offen)

`manual_pg_adapter.py` setzt den initialen Transport um: Pflichtbindungen ohne
persönliche Defaults, frischer Präfix-/Imagecheck, gelabelte neue Volumes mit
Readback vor Chown, eigene begrenzte Chown-/initdb-Container, SCRAM am Unixsocket
ohne TCP, PG-CID-/Mount-/Ownerreadback, begrenztes read-only pg_isready-Polling
und der oben gebundene psql-STDIN-Stream. Jeder PGimage-Run bindet dessen
deklarierten Datenpfad ausdrücklich, damit keine anonymen Datenträger entstehen.
Die optionale Keyringvorbereitung entfällt im Nur-PG-Test.

Jeder äußere Port braucht weiterhin einen Rootguard mit tatsächlichen
Hostkey-/Image-/UID-/Owner-/Fristbindungen; eine Dataclass erteilt keine Rechte.
Der Adapter besitzt weder Secretresolver noch Restart, automatische Reparatur,
Stop/Löschroutine oder öffentlichen Execute-Endpunkt. Unklarer Ausgang sperrt
den Versuch; nur eigene RAM-Puffer werden geleert.

Die [isolierte Nur-PG-Qualifikation](../../../tests/manual-postgres-channel/README.md)
prüft später mit synthetischen Passwörtern genau diese Source auf Linux. Import
und normaler CLIaufruf sind inert. Reine Tests/Fake-Docker-Formatter sind noch
kein bestandener PG-/SSH-/Installerbeweis. Private Probes/Permits bleiben außerhalb
eines öffentlichen Commits.


## Konkrete Runtimeports und Wiederanlauf

`manual_runtime_adapter.py` ergänzt **keinen zweiten Installer**, sondern die
vorhandenen `ManualOperator`-Ports: tatsächliches Observe, Runtime-Rollen-/ACL-
Login, terminaler Migrator mit anschließendem Historienread22, stdin-APIstart
und begrenzte HTTPreadiness mit FEEL/Head22/Pending0/anonymer401. CLIexit/CID
allein sind nie Erfolg. Exakte Owner-/Image-/ENV-/Mountziel-/RW-/UID-/Netz- und
Ressourcenreadbacks sperren Drift; Secretkonfiguration bleibt ausschließlich
im bestehenden stdin-JSONvertrag. Import und normaler CLIaufruf bleiben inert.

Der äußere beaufsichtigte Aufrufer verwendet
`deliver=lambda: ports.accept_bundle(resolve_once())`; erst nach positiver
Vorprüfung wird einmal geliefert. Ein eigener Resolver, ENV-/Dateifallback,
Secretagent oder automatischer Restart wird nicht eingeführt. Appimagequelle
und Operator-/Ressourcenquelle sind getrennte attestierte Bindungen. Vollständige
Netzlabels werden tatsächlich gebunden, nicht aus einem Coolify-Namensschema geraten.

Ein Restart benötigt einen **neuen API-/Migratorversuch mit neuer Nonce**, aber
exakt registrierte unveränderte Daten-/Socket-/Keyringvolumes und PG-CID. Die
alte API muss fehlen oder nachweislich beendet sein; eine laufende alte API
wird nicht gestoppt. `PG_VERSION` wird selbst bei gestopptem PG ausschließlich
als begrenzter RAM-Tar über `docker cp ... -` gelesen, nicht als Secretdatei oder
neuer Helpercontainer. Kein initdb, Volume-create, Rollenbootstrap oder Rotation.

**Enger Restart-Vorbehalt:** In diesem Schritt entfernt der Adapter keinerlei
Container. Der Rootoperator muss erfolgreiche initiale Hilfscontainer zuvor
separat eindeutig bestätigt entfernt haben; für den Wiederanlauf müssen exakt
PG und gegebenenfalls die alte API im Namensraum verbleiben. Unbekannte oder
alte Hilfscontainer sperren die Vorprüfung, statt automatisch adoptiert oder
bereinigt zu werden. Unklare Ausgänge bleiben UNKNOWN mit eigenem lokalem
Handle; keine automatische Reparatur, Löschung oder Datenrollbackbehauptung.

Der [einmalige native PG-Kanallauf39505148488/a1](https://github.com/flowzer-io/flowzer-bpmn-core-engine/actions/runs/39505148488)
auf Source`a7fcde7` hat genau seine drei getrennten synthetischen Fälle bestanden:
zwei Passwortprompts/Reconnect/EOF, erwarteter Fehler ohne zweite Passwortzeile
und kontrolliert zurückgehaltenes EOF; Rollenrechte geprüft, Ressourcen ABSENT,
eigene Handles0. Das ist **kein** kombinierter API22-/ARM64-/Cool3-/Keycloak-/
HTTPS-/45-Minuten-Formularbeleg. Der neue Runtimeadapter hat bislang nur reine
Fake-/Pipe- und eigene AF_UNIX-DockerCLI-Formattertests. Der ältere numerische
C#-PID-P3 bleibt separat im Testfeeder offen; er gehört weder zum WebApiEngine-
Publish-Projektreferenzgraph noch zu diesem neuen Adapter.


Die zusätzliche Restartprüfung bindet PG auch an read-only RootFS, private PID-/
IPC-Namespaces, leere Portbindungen, Cap-/Security-/Ressourcenwerte, originales
Postgreskommando und unveränderte Image-ENV. ACLreads schließen zusätzliche
Default-/effektive Tabellen-/Sequenzrechte einschließlich Grantoptionen aus;
PUBLIC und geerbte Rollen werden berücksichtigt. Diese erweiterten SQLreads
haben noch keine echte kombinierte PostgreSQLabnahme, nur Source-/Fakeverträge.
Das gesamte Proxy-CIDR wird nicht verbreitert: Netzwerkreadbacks verlangen
exakt die vorher von Root registrierten Proxy-/Console-CIDs (höchstenszwei)
und gegebenenfalls die aktuelle eigene API. Fremde Netzteilnehmer blockieren.
Alle Readinessreads einschließlich Dockerinspect und Netzinventar verwenden
das verbleibende30s-Portbudget.

Der Runtime-Adapter prüft vor dem ersten Ressourcenwrite zusätzlich beide festen SQL-STDIN-Pakete auf das 16-KiB-UTF-8-Limit sowie die festen SQL-/App-Argumente gegen die unveränderten Transport-Canaries. Das gilt auch beim Wiederanlauf; sämtliche eigenen Paketpuffer werden bei Fehler und Abschluss geleert.
