# Flowzer-Demo auf Cool3 – getrennte Coolify-Sourcekonfiguration

**Sourcekandidat, keine Runtimefreigabe.** Der native ENV-Startpfad ist wegen
des belegten Coolify-Fehlerkanals für echte Secrets gesperrt. Der neue
[manuelle stdin-Start-/Restartvertrag](../../docs/SUPERVISED-DEMO-BOOTSTRAP.md)
ist ein separater Source-Vertrag, noch kein operativer Launcher. Dieses Manifest
wurde nicht still darauf umgestellt.

 Christian hat am 15.10.2026 einen bestehenden
Coolify-Host mit Reserve statt Dev01-Erweiterung oder VM-Buchung gewählt. Der
Demo-Koordinator bindet **cool3.maass.it**, nicht Cool4 (bestehendes Flowzer-Production).
Der alte [Dev01-Kandidat](../tickytask-demo/README.md) wird nicht eingesetzt.

## Tatsächlich belegter Zielkontext

Root-Beleg `coolify-maassit-two-existing-hosts-capacity-read.safe.json`, SHA256
`cbb4b2c07314119b3b22f260569536ab9fe97be3dd73904c48aee1501dbdb9bc`:

- Cool3, IPv4 `128.140.114.193`, dokumentiert Hetzner 117078810; UID 0, Hostname,
  Server-IP und bestehender ED25519-Fingerprint wurden durch Root verifiziert.
- ARM64/aarch64, vier CPUs; 08:56:54–58 UTC: RAM gesamt 8.104.914.944 Byte, verfügbar
  6.942.068.736 Byte; Root-/Docker-Dateisystem 60.412.997.632 Byte frei. Fünf laufende
  gesunde Container, kein historisch gesetztes OOM-Kennzeichen in dieser Stichprobe.
- Vorhandener `coolify-proxy`, Traefik v3.6, Netz `coolify` und zusätzlich bestehendes
  Documenso-Netz. Beides bleibt unangetastet. Die beobachtete Proxy-IP `172.18.0.2`
  ist **nicht** automatisch seine IP im erst anzulegenden neuen Resource-Netz.
- ACME-Datei lediglich per Stat geprüft; Inhalte, Account-/Zertifikatsbereitschaft
  bleiben unbekannt. Kein DNS-/TLS-Erfolg aus dem Vorhandensein der Datei ableiten.

Das ist ein Reserven-Snapshot, **keine Peak-/Runtimeabnahme**. Vor Apply erneut
Kapazität/Last, aktive Jobs, Zielidentität, Kollisionen und fremde Dienste prüfen.
Eigene Obergrenzen bleiben PG 512 MiB/0,5 CPU, API 768 MiB/1 CPU, Console 128 MiB/
0,25 CPU; Migrator separat 1024 MiB/1 CPU. Runtimebudget inklusive 512 MiB Reserve
1,875 GiB, Migrationsphase PG+Migrator inklusive Reserve 2 GiB; zusätzliche Hostreserve
mindestens 1 GiB als Planungsgrenze. Grenzen sind keine gemessenen Mindestbedarfe.

## Kleinster verwalteter Coolify-Pfad

Eine **neue Git-basierte Docker-Compose-Application**, separate Demo-Umgebung/-Resource,
Compose-Datei `deploy/tickytask-demo-coolify/compose.json`, kein Raw-Modus, kein Build,
keine automatische Webhook-/Branch-Promotion und kein Image-Publisher. Coolify besitzt
Projekt-/Container-/Volume-/Netznamen; deshalb keine festen `container_name`, globalen
Volume-Namen oder Netz-IDs aus einem anderen Stack.

Vier eigene Dienste: `postgres`, `migrate`, `api`, `console`. Alle kommunizieren im
neuen Resource-Netz über Dienstnamen. Anders als der direkte Dev01-Composeplan nutzt
hier der kleine Stack **ein eigenes gemeinsames Netz**, kein zusätzliches DB-Netz,
keine Verbindung mit fremden Resources über „Connect To Predefined Network“ und keine
Hostports. PG hat keine öffentliche Route. Dass der verwaltende Proxy das neue Netz
für die Console erreicht, verleiht PG/API keine öffentliche Domain.

Das Manifest lässt Netzdeklarationen absichtlich vollständig weg. Der geprüfte
[Coolify-4.3.17-Parser](https://github.com/coollabsio/coolify/blob/5ff21103974a30e107ce046190fb478658a214e7/bootstrap/helpers/parsers.php)
ergänzt das UUID-Netz selbst; ein explizites `default` bliebe zusätzlich bestehen.
Das Vertragsmodell mit echter Compose-Interpolation prüft deshalb genau ein
ergänztes Resource-Netz, ohne den Trust-CIDR zu verbreitern. Es ist kein Nachweis
der tatsächlich später gerenderten Coolify-Konfiguration.

Nur der Console-Dienst erhält in Coolifys Domains-Konfiguration
`https://flowzer.tickytask.de:8080`. Der Suffix bezeichnet den **internen** Zielport;
der Browser verwendet weiter öffentlich HTTPS/443. Keine geratenen Traefiklabels,
`VIRTUAL_HOST`, Domain-/Passwortgeneratoren oder manuelle Proxy-Neustarts. Coolify
generiert Routing/Proxy-Netzanbindung; die tatsächlich erzeugte Konfiguration muss
vor Freigabe redigiert geprüft werden: ausschließlich Console öffentlich, kein
zusätzlicher Wildcard-/sslip.io-/API-/PG-Router. Die derzeit dokumentierte Semantik:
[offizielle Compose-Dokumentation](https://coolify.io/docs/applications/builds/docker-compose).
Die installierte Version 4.3.17 ist per regulärem Metadatenread verifiziert; das
konkret gerenderte Ressourcenverhalten bleibt vor Runtime zu binden.

`FLOWZER_DEMO_TRUSTED_PROXY_CIDR` ist zwingend der tatsächlich verifizierte CIDR des
**neuen** eigenen Resource-Netzes, kein Standardwert und kein pauschales Docker-Standard-
oder fremdes Netz.
Zwei vertrauenswürdige Stufen: Coolify-Proxy → Console-nginx → API. Root bindet den
CIDR nach kontrollierter Ressourcen-/Netzvorbereitung **vor dem API-Start**. Kein
API-Start mit unbekannter Proxy-Grenze; keine unterstellte alte Proxy-IP.

## Images und Datenhoheit

API/Migrator und Console bleiben unverändert digestgebundene Images vom geprüften
App-Source **4c4412d62e2ae8062e3880f7d97c9faa1f93debe**, Publisher 38661504525/a1:
API `sha256:9994c7e309fab08f649b21cba44b13249c1eb28d474bcb4a73aa0a7a56552273`,
Console `sha256:fa5f1b80adcc8088108d6f0d639d66106fa8f2b4b4651d1ae2e069b838f7618a`.
Der neue Konfigurations-Source ist **keine neue App-ImageRevision**.

PostgreSQL 17 verwendet den von Root geprüften offiziellen Multiarch-Index
`sha256:2d2b8998d31037bf721cfdf764d76ba74171b4fab3431b7f72c27c56ddbdf9e3`.
Root hat den linux/arm64/v8-Runnable
`sha256:2d46d14f5a051d8c1e4f9d7c07dbeb877ae08150654d69ca32903ccbae4a5421`
und dessen Config-Descriptor
`sha256:a470461db3d4079fb4cb545328a6ef7c7d1ffc33e6981f5d3e2cfda1634d297e`
gebunden (PG-Version `17.11-1.pgdg13+2`). Beleg:
`postgres17-frozen-index-arm64-three-native-read.safe.json`, SHA256
`b077c8f6f723225cf802afdd07bd5e0e81e7710d144cdc1fd78427ff9a9e253d`.
Index-/Runnable-Rawbytes wurden gehasht; die öffentliche Config löste der native
Client am gepinnten Runnable auf, ihre Rawbytes wurden nicht unabhängig neu gehasht.
Kein Pull/Layer-/Runtimebeleg, kein Emulations-/Tag-Fallback.

Neue leere PG- und BFF-Keyring-Volumes, keine Kopie bestehender Daten/Schlüssel.
Bootstrap ausschließlich über die bestehenden Dateien in `../postgresql/`, eigene
DB/Laufzeitrolle `flowzer_tt_demo`, Migrationsrolle `flowzer_tt_demo_migration`, Schema
`flowzer`. Laufzeit ohne DDL, Migrationshistorie nur lesend. `migrate` bleibt explizites
`operations`-Profil, nicht gewöhnlicher API-Start. Ein normaler Deploy ist **kein**
Migrationsbeleg. Vor Aktivierung den echten Coolify-Start-/Migrationsmechanismus binden:
nur PG vorbereiten → vorhandene Rollen-/Rechteskripte → expliziter Migrator Exit 0 →
API-Readiness → Console/Routing. Keine neuen SQL-/Grant-/Bootstrapprogramme.
**Keine Demo-Backups, Dumps, Keyringkopien, Resets, Downmigration oder `down -v`.**

## Coolify-spezifische Secretgrenze

Der Repository-Vertrag in [OPERATIONS](../../docs/OPERATIONS.md#dienstgrenzen-bei-coolify-compose)
belegt gemeinsame generierte `env_file`, zusätzliche Referenzalias-Injektion und
Leerwertersetzung. Deshalb erhalten **alle vier Dienste** nichtleere Masken für die
fünf Secretalias-Klassen. Nur explizite Verwendungsreferenzen dürfen sie ersetzen:

| Dienst | Ausschließlich erlaubte echte Werte |
|---|---|
| PG | PG-Adminpasswort |
| Migrator | Migrations-DSN |
| API | Laufzeit-DSN, BFF-Secret, read-only Directory-Secret |
| Console | keine |

Keine echte Secretdatei oder `.env` in Repo/Prüfergebnissen erzeugen. Secrets ausschließlich
im freigegebenen Secret-Store beziehungsweise erlaubten Coolify-Secretweg, Übergabe im
Laufzeitspeicher. Coolifys intern erzeugte Dateien weder kopieren noch ausgeben. Auch
keine rohe ENV/Inspect-/Compose-/Log-Ausgabe. Der Operator darf nur die im Manifest
referenzierten Keys provisionieren, keine weiteren Secret-/kanonischen Konfigurationskeys
in eine gemeinsame ENV schmuggeln. Tatsächliche Dienstgrenzen später per redigierten
Präsenz-/Gleichheitsprüfungen belegen; die lokale Parsermodellierung ersetzt das nicht.

Realm bleibt **TickyTask**, zwingend echte Demo-Wurzelgruppen-ID, eigenes BFF-/Directory-
Konto und TT-Token-Exchange; keine MaassIT-/Staging-/Production-Clients übernehmen.
FormEmbedding bindet exakt `https://flowzer.tickytask.de` und `https://demo.tickytask.de`.
Mail, HTTP-Connector, KI, externe Webhooks, Inbound-Trigger und Ticket-Serviceaktionen
bleiben bis zu ihren separat gebundenen Phasen geschlossen. Ticketaktionen, Urlaubs-
abläufe und reale 45-Minuten-/Keycloak-/HTTPS-Abnahme bleiben Teil des Gesamtziels.

## Verifikation und noch offene Gates

```sh
python3 -m unittest discover -s deploy/tickytask-demo-coolify -p 'test_*.py'
```

Hermetische Sourceverträge plus echte lokale Compose-Interpolation mit eigenem temporärem
HOME/DOCKER_CONFIG, synthetischer 0600-env_file und nicht existierendem Daemonsocket.
Nur die benötigten ENV- und UUID-Netz-Verträge werden modelliert, kein vollständiger
Coolify-Nachbau. Keine Host-/DB-/Netz-/Secrethelper- oder Containeraktion. Fehlendes
Manifest war zuerst RED; eine leere fremde Maske belegt als Negativfixture den Leak.
Diese Python-Tests sind lokale Belege, keine automatische CI-Ausführung behauptet.

Vor Runtime fehlen insbesondere die konkret gerenderte Ressourcenkonfiguration,
Projekt-/Umgebungs- und Destinationbindung, Namens-/Volume-Kollisionsprüfung, tatsächlich generierte Netze/
Routing/Profiles/Startkommandos, frischer Hostpreflight, DNS/TLS und Realm-
IDs/Secretreferenzen. Keine Akzeptanz aus einem grünen Source-Review oder CI allein.
