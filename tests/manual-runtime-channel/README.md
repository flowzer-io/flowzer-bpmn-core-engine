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
