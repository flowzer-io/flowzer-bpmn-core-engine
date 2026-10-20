# Isolierter PostgreSQL-STDIN-Kanaltest

**Source, noch kein ausgeführter Linuxlauf.** Kein privater Host/Secretzugriff,
keine Imageveröffentlichung, kein Flowzer-/Keycloak-/TT-/Produktionsdeploy.
Die Source qualifiziert ausschließlich PostgreSQL auf einem frischen nativen
Linux/amd64-GitHub-Hostedrunner. ARM64, Migration22, API/FEEL, HTTPS und
langlebige Formulare bleiben getrennte spätere Abnahmen.

## Öffnung und Grenzen

Nur der explizite `pg_channel_opening`-Opt-in der bereits registrierten
`ci.yml` auf dem geprüften Topic, niemals Push/PR oder ein Release-/Stagingdeploy.
Die acht normalen CIjobs bleiben in diesem Modus geschlossen, im normalen
Default-/alten stdinPilotmodus dagegen bytegleich. Gemischte Inputs führen
im ausgeführten PG-Preflight zu FAIL, nicht zu einem grünen Alles-SKIP.
Die PG-Concurrency ist getrennt und bricht keinen bestehenden Lauf ab.
Es ist kein neuer Workflow auf `main` oder eine Main-Promotion erforderlich. Vor Veröffentlichung/Dispatch bindet
Root tatsächlich den finalen Commit/Tree, Workflow-SHA256, neue Nonce und Frist.
Das öffentliche JSON hat exakt mode=`pg-channel-v1`, sourceSha, sourceTree,
workflowSha256, nonce und expiresAt (UTC-Unixsekunden); unbekannte oder doppelte
Felder und Pakete über4KiB werden vor Docker abgewiesen.
Der Fixtureguard kontrolliert Hosted/Linux/X64, Topicref, tatsächlichen
Workflowpfad, Attempt1, aktuelle
Source-/Tree-/Workflowbytes und alle tatsächlich benutzten Runtimequellen.
Der einmalige Claim wird vor dem ersten Dockerport im eigenen festen
`RUNNER_TEMP/flowzer-pg-channel-proof/` angelegt. Kein ungeprüfter Dispatchwert
wird als Artifactpfad verwendet. Checkout-/Upload-Actions sind vollständig
gepinnt, Checkoutcredentials werden nicht persistiert. Kein GitHub-Environment,
`secrets: inherit`, SDK, APIimagebuild oder Package-/Deploymentrecht.

Das PostgreSQLimage ist das unveränderliche **amd64**-Runnablemanifest
`docker.io/library/postgres@sha256:66aafa11cf15800a3c94763f7e11d1e7b2e37e5e84bc4ce027cb6e1e4bfaf4df`,
Konfiguration `sha256:589f742565e4f6d22149176b032974f55f8201a8a37b71ed587ec5c6902e689f`.
Das wurde anhand der öffentlichen OCI-Metadaten geprüft, nicht als ARM64image
angenommen. Vor Nutzung werden ID/OS/Architektur und die tatsächliche psql17-
Version geprüft. Nur dieses Image wird gezogen, mit `--quiet`, kein Registrywrite.

## Drei voneinander getrennte Wegwerfcluster

1. **Positive zwei Prompts und EOF:** tatsächliches initdb-Passwort über
   `/dev/stdin`, danach normaler PGserver ohne TCP. Gebundener SQL01/02-Stream
   über psql `-W`; zweiter Passwortprompt nach dem kanonischen `\connect`.
   Terminaler Exit0, beide Output-EOFs, Rollen-/DB-/Historienrechte und echte
   Migration-/Runtime-Logins inklusive DML/DDL-Verweigerung werden geprüft.
2. **Fehlende zweite Passwortzeile:** kontrolliert beschädigter RAM-Stream im
   separaten neuen Cluster muss begrenzt fehlschlagen, kein Bootstrapretry oder
   Wiederverwenden dieses Zustands für einen anderen Test.
3. **Zurückgehaltenes EOF:** eigener Client lässt stdin nach vollständiger
   Lieferung zwei Sekunden offen. Ein vorzeitiger Prozessabschluss ist Fehler.
   Erst danach explizites EOF und terminaler Abschluss. Dieser Eingriff lebt nur
   in der Fixture; der normale Transport erhält keinen No-EOF-/Fallbackmodus.

Alle Werte sind frisch synthetisch und nur im RAM/STDIN. Keine Passwortwerte,
ENV-Secrets, argv-Werte, Secretdateien, Rohkanäle oder Payload-/Secret-Hashes in
Logs/Artifacts. Eigene Klartext-/SQLquote-/Base64-Echos sperren die Verarbeitung.
Bytepuffer werden geleert; immutable Pythonkopien sind nicht sicher zeroisierbar.

Pro Fall genau zwei neue Volumes und höchstens drei neue Container, insgesamt
maximal sechs/neun. Jede Ressource ist durch Nonce/Source/Run/Scope gelabelt.
Container: `network=none`, keine veröffentlichten Ports, `restart=no`,
`log-driver=none`, read-only RootFS, begrenzte tmpfs, Cap-Drop, 384MiB/0,75CPU/
64PIDs. Root ist nur für Chown auf diesen neuen Volumes vorgesehen; initdb/PG/
psql laufen als attestierter PGuser. Kein privater Docker-/SSH-Socketfallback.
Die Host-Egress-Firewall wird nicht als gesperrt behauptet: der reviewte Code
nutzt nur lokalen Docker, öffentlichen Imagepull und GitHub-Controlplane;
Container haben keine Netzwerkverbindung.

## Endlicher Abschluss

180s Arbeitsbudget, nur Cleanup bis30s danach sowie der bestehende begrenzte
lokale Gruppenabschluss. Der Job endet spätestens nach8min. Vor Stop/Remove
werden exakte eigene CID, Image, Namen und vollständige Owner-/Nonce-/Source-/
Runlabels geprüft. Fremde oder unklare Ressourcen werden nicht adoptiert.
Kein Prune und keine zweite Cleanupreparatur nach UNKNOWN. Negative Teilzustände
werden nur im expliziten Wegwerftest beseitigt, niemals als privater Rollback
interpretiert. ABSENT-Readback ist Pflicht, andernfalls FAIL/UNKNOWN samt Handles.

Das einzige Artifact ist `proof.safe.json`: sichere Source-/Imagebindungen,
endliche Phasen/Exitcodes/Promptzahlen/Ergebnisflags und getrennte Cleanup-
Nachweise. Kein Linuxerfolg wird aus den lokalen Pure-/Pipe-/Fake-Formattertests
abgeleitet. Ausbleibender oder unvollständiger Beleg bleibt UNKNOWN.
