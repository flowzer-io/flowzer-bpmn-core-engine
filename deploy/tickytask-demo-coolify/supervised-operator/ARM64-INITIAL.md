# Ein eigener synthetischer ARM64-Initialversuch

Dies ist **kein Demo-Installer** und keine Ausführungsfreigabe. Der öffentliche
CLI- und `execute()`-Entry ist **immer SOURCE_ONLY**: auch ein syntaktisch gültiges,
selbst ausgestelltes Permit/Manifest mit passenden mitgegebenen CLIhashes kann
keinen Nativeport öffnen. `--execute` wird vor Lease, Rawloader und Channel abgewiesen.

Ein **separates privates Rootcaller-Bindingartifact außerhalb des Repositories**
bleibt jetzt mit festem Go-Pfad und Go-Hash `NOT_ISSUED` vollständig inert. CLI/ENV
können diese Authority nicht wählen. Erst Root darf später eine konkrete neue
Instanz mit tatsächlich extern gebundenem Execute-Go-Pfad/Bytehash sowie Public-
Source-/Manifestpins erstellen und öffnen. Root muss die konkreten Caller-Rawbytes
extern pinnen und dieselben RAMbytes kalt kompilieren, nicht einen `.pyc`-Cache.
Template-/Funktionsbytes und die späteren konkreten Metadaten-/Instanzbytes sind
**getrennte** Freeze-/Abnahmezustände; dieser Sourcetest ist kein Runtime-Go.

Das Go bindet Head/Tree, Ziel/Hostkey, Owner, Namespace und Nonce. Rootdateien sind
Bindungen, keine eigenständigen Signaturen. Lease/Channel bleiben Bibliotheks-
bausteine, keine zweite Authority. Gegen beliebigen bösartigen Same-UID-Pythoncode
wird keine Sandbox behauptet. Der private Caller wird nicht öffentlich committet.

## Nur vier neue Sourcepfade

- `manual_arm64_initial.py`: stets geschlossener öffentlicher Entry, Rootlease/Rawloader,
  Host-/Imageadmission, vorhandener SSH-/STDIN-Kanal und Original-Initialsequenz.
- `manual_arm64_initial_cleanup.py`: vollständiges eigenes Inventar und begrenztes
  Cleanup, keine zweite Pipe oder allgemeine Löschfunktion.
- `test_manual_arm64_initial.py`: isolierte Fakten-/Root-/Once-/Wire-/Cache-/
  Sequenz-/Cleanup-Negative, nur eigene temporäre Dateien/Fakes.
- diese Datei.

Die sechs bisherigen Operator-/PG-/Runtime-/Readiness-/Transportmodule sowie beide
SQLpakete bleiben bytegleich. Der Rawloader pinnt die gesamte Closure vor dem
**ersten** Module-Exec und kompiliert genau diese RAMsnapshots. Vorregistrierte
kanonische Module verhindern auch deren historische importlib-Fallbacks.

## Admission und feste Images

Vor jedem Pull und vor Initial: aktuelle gebundene UID0/aarch64/4CPU-Identität,
Dockerclient und -server27.5.1/API1.47/arm64/Linux, mindestens4GiB `MemAvailable`,
8GiB freier Dockerplatz, endliche Last kleiner4. Der neutrale Config-/HOME-/PATHpfad
muss tatsächlich mit ENOENT fehlen. Der Original-SshTarget und dessen feste
neutrale Dockerhülle/Unixsocket bleiben unverändert, kein Hostconfigread/-write.
Nur vorhandene eigene known_hosts mit genau dem gebundenen ED25519-Fingerprint,
kein Scan, Update oder zweiter Zugang.

Der gesamte eigene Container-/Volumepräfix muss **ABSENT** und das gebundene Netz
leer sein. Fremde Netzteilnehmer werden nur gezählt und sperren vor Detailreads.
Die ursprüngliche Netzwerksemantik prüft ID, Bridge, internal=false, Subnetz,
vollständige Rootlabels und exakte Peers – **kein Gatewayfeld**. Fehlendes IPAM-
Gateway wird nicht erfunden oder wegkonfiguriert. Eine separat belegte physische
Bridgeadresse bleibt ein separater Nachweis, keine Eigenschaft dieses Guards.

Maximal je ein Pull, ausschließlich:

- API: `ghcr.io/flowzer-io/flowzer-api@sha256:8528f456c890be903293fd90b931f7750247108b9b64ca56a37d44f1378222e5`
  → lokale Config-ID `sha256:0033d7828879afb1f8841a87eb7201583c1da865bbd13042de99e50c847718d0`,
  Imagesource `a7fcde7cba2cf526e8908037b238ee6d68bdbf85`, UID1654.
- PG: `postgres@sha256:2d46d14f5a051d8c1e4f9d7c07dbeb877ae08150654d69ca32903ccbae4a5421`
  → lokale Config-ID `sha256:a470461db3d4079fb4cb545328a6ef7c7d1ffc33e6981f5d3e2cfda1634d297e`.

Vor Credentials und neuen Volumes müssen tatsächliche lokale Imageconfigs
OS/ARM64/IDs/APIrevision/UID sowie `PG_MAJOR=17` und `PG_VERSION=17.11` (gegebenenfalls
Paketrelease-Suffix) bestätigen. Ein vorheriger öffentlicher Registry-307/UNKNOWN
ist **kein** PGconfigbeleg. Keine Redirectlockerung oder Registry-/Loginreparatur.
Zusätzliche Image-VOLUME-Deklarationen sperren; nur der ausdrücklich gebundene
PGdatapfad ist erlaubt, keine unbemerkten anonymen Volumes.

## Genau Initial, dann eigenes Cleanup

Nur `ManualOperator.run('initial', RuntimePorts, ...)`, kein Restart oder zweiter
Initialversuch. Die fünf synthetischen zufälligen Werte werden erst nach positiver
Original-Observe im RAM erzeugt und über die Original-STDIN-Pakete geliefert.
Kein Vault, echtes IdP, ENV, argv, Log oder Paketfile. Python-Strings sind nicht
beweisbar zeroisierbar; Originalports leeren veränderliche Paketpuffer. BFF/Directory
zeigen wie im isolierten Foundationtest ausschließlich auf HTTPSloopback, ohne
`RequireHttpsMetadata`/Authentifizierung abzuschwächen.

Genau sechs mögliche Containernamen `<namespace>_<role>_<nonce>` mit Rollen
`ownership-pg`, `ownership-key`, `initdb`, `postgres`, `migrate`, `api`; drei neue
Volumes `<namespace>_postgres-data`, `_socket`, `_keyring`. Maximal zwei Container
laufen gleichzeitig. Originallimits: PG/Helfer384MiB/0.75CPU/64PID,
Migration1024MiB/1CPU/128PID, API768MiB/1CPU/128PID; kein Hostport,
read-only/cap-drop/no-new-privileges/log-none/restart-no unverändert.

Nach Initialerfolg oder bekannt beendetem Fehler (auch bei RAM-Close-Fehler): vollständige **Container- und
Volumeinventare vor erstem Delete** prüfen (exakte Namen/CIDs/Images/vollständige
Own-/OCIlabels), erst dann eigene Container und Volumes entfernen. Vorhandenes Netz
und gemeinsamer Imagecache bleiben unverändert. ABSENT erst nach erneuten echten
Präfix- und Netzwerkreads. Unbekannter eigener Writer oder Prozesshandle sperrt
Cleanup; kein konkurrierender Remove, Blind-Retry oder nachträglicher Fremdkill.
Unbekannte/übrig gebliebene Ressourcen werden ehrlich als FAIL/UNKNOWN gesichert.

O_EXCL bindet den gesamten Rootversuch und jeden Pull-/Initial-/Cleanupstage.
Aktive Frist höchstens600s, explizites Cleanupende höchstens120s danach; Cleanup
zusätzlich monoton auf120s begrenzt, jeder Cleanupcall maximal6s und nie über
seine Restfrist. Jede weitere Aktion nach unbestätigtem Writer/Handle sperrt.

## Abnahmegrenze

PASS bedeutet nur: synthetische Initialsequenz mit realen Originalproofs
(ACL, Migration22/Pending0, API1654, FEEL, anonyme401) **und** eigenes CleanupABSENT
bei geschlossenen eigenen Handles. Kein dauerhaft installiertes Demo, keine
HTTPS-/DNS-/Keycloak-/TT-/Browser-/45Minuten-Formularabnahme. Keine Coolify-APIwrites,
Production, MSSQL, echten Benutzerdaten oder Tenantaktionen aus diesem Pfad.

Lokale Tests: `python3 -I -S -B deploy/tickytask-demo-coolify/supervised-operator/test_manual_arm64_initial.py`.
Der optionale tatsächliche Formattertest verwendet den explizit gehashten lokalen
CLI29 nur am eigenen AF_UNIX-Fake; sonst SKIP. Dies ist **kein** Docker27- oder
privater ARM64-Runtimebeleg. Tests/Review/Commit eröffnen keine Runtimeaktion.

Der private NOT_ISSUED-Bindingtest läuft nur, wenn das separat gehashte private
Artifact vorhanden ist; im reinen öffentlichen Checkout wird dieser Test ausdrücklich
als SKIP ausgewiesen. Vollständiger Fünf-Pfad-Testnachweis und vier öffentlicher
Commitpfade sind getrennt zu dokumentieren.
