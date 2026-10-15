# Separate TickyTask-Demoinstallation auf Dev01 – Sourcekandidat

**Keine Ausrollfreigabe.** Dieser Ordner konkretisiert ausschließlich die neue,
separate Demo `https://flowzer.tickytask.de`. Er verändert weder TT noch bestehendes
Flowzer-Staging/Production. `compose.json` ist JSON und damit gültiges Compose-YAML;
es benötigt keinen zusätzlichen Parser im Repository.

## Fester Aufbau

```text
Internet :443 -> vorhandener nginx-proxy (TLS + vorhandener ACME-Companion)
                 -> neues Netz flowzer-tt-demo-edge
                    -> console:8080 -> api:8080
                                       -> internes flowzer-tt-demo-db -> postgres:5432
                    migrate (nur operations-Profil) -------------------^
```

- Genau vier eigene Dienste: PostgreSQL 17, einmaliger Migrator, API und Console.
  Kein neuer Gateway, kein Hostport, kein Docker-Socket und keine Zertifikatsmounts
  in den Appcontainern. Projektname: `flowzer-tt-demo`.
- Zwei neue Netze, keine Übernahme des TT-Netzes: `flowzer-tt-demo-db` ist intern;
  `flowzer-tt-demo-edge` erhält einen vorher gegen Docker- und Hostrouten geprüften
  CIDR über `FLOWZER_DEMO_EDGE_CIDR`. Dieser CIDR ist zugleich die explizite
  Forwarded-Header-Vertrauensgrenze; zwei Proxy-Stufen, kein pauschales Docker-Netz.
- Der bestehende `nginx-proxy` ist laut Root-Inventur **nur** am Default-Bridge,
  nicht am TT-Netz. Seine spätere additive Verbindung zum neuen Edge-Netz ist ein
  **eigens freizugebender Hostwrite**. Kein Neustart/Neuanlegen und keine Änderung
  seiner bestehenden Netze, Mounts, Ports oder anderer vhosts. Ohne diese Verbindung
  ist der Kandidat nicht erreichbar. Der alte Default-Bridge wird nicht als Compose-
  DNS-Netz zweckentfremdet.
- Console nutzt `VIRTUAL_HOST`/`LETSENCRYPT_HOST=flowzer.tickytask.de` und Port 8080.
  Vorher vorhandenen ACME-Companion tatsächlich an diesen Proxy binden/verifizieren;
  keinen Accountkontakt oder Zertifikatszustand erfinden. Kein eigener ACME-Dienst.
- Eigene persistente Volumes: `flowzer-tt-demo-postgres-data` und ausschließlich für
  die API `flowzer-tt-demo-bff-keyring`. Keine Live-Demo-Backups, Dumps, Resets oder
  Keyringkopien durch diesen Kandidaten. Ein bestehendes Volume nie ungeprüft als
  leere Erstinstallation behandeln; niemals `down -v` als Wiederanlauf verwenden.

## Gebundene Images und unveränderte Verträge

API/Migrator und Console sind auf die unabhängig geprüften OCI-Indexdigests des
Topic-Publishers **38661504525/a1**, Source **4c4412d62e2ae8062e3880f7d97c9faa1f93debe**, gebunden:

| Dienst | Digest |
|---|---|
| API und Migrator | `sha256:9994c7e309fab08f649b21cba44b13249c1eb28d474bcb4a73aa0a7a56552273` |
| Console | `sha256:fa5f1b80adcc8088108d6f0d639d66106fa8f2b4b4651d1ae2e069b838f7618a` |

Die API-/Console-Digests sind Multiarch-Indexdigests, keine Behauptung über den
Digest eines einzelnen ausführbaren Plattformmanifests. PostgreSQL 17 benötigt
noch einen verifizierten Plattform-/Indexbezug über `FLOWZER_DEMO_POSTGRES_SHA256`;
kein beweglicher Tag als Rückfall. Kein Imagebau oder neuer Publisher nötig.

Bestehende `compose.coolify.yaml`, `compose.runtime.yml`, Dockerfiles, Workflows,
Runtime-Skripte und SQL-Dateien bleiben unverändert. PostgreSQL-Bootstrap verwendet
nach separater Freigabe die vorhandenen Dateien `../postgresql/01-datenbank-und-rollen.sql`
und `02-laufzeitrechte.sql`, mit eigenen Namen `flowzer_tt_demo` (DB/Laufzeitrolle),
`flowzer_tt_demo_migration` (DDL-Rolle), Schema `flowzer`. Passwörter sind dort bewusst
nicht enthalten. Die Laufzeit bekommt kein DDL und nur SELECT auf `schema_migrations`.

## Kapazität: derzeit NO-GO, nicht durch kleinere Zahlen umgehen

Root hat auf Dev01 zwei CPUs, 4.005.441.536 Byte RAM und zum Messzeitpunkt nur
1.020.583.936 Byte verfügbaren RAM gemeldet; Docker-Dateisystem frei 9.539.432.448 Byte.
Das ist eine Momentaufnahme, kein Last-/Peaknachweis und kein verfügbares Budget.

| Dienst | Kandidatenobergrenze RAM | CPU-Obergrenze |
|---|---:|---:|
| PostgreSQL | 512 MiB | 0,5 |
| API | 768 MiB | 1,0 |
| Console | 128 MiB | 0,25 |
| Migrator, separat | 1024 MiB | 1,0 |

Diese Grenzen sind **keine gemessenen Mindestbedarfe, Reservierungen oder Abnahme**.
Als konservatives Öffnungskriterium: Runtime zusammen 1408 MiB plus mindestens
512 MiB Zusatzreserve; Migration bei gestoppter eigener API/Console 1536 MiB plus
512 MiB Reserve. Die belegte freie RAM-Menge erreicht beides nicht. Für die 1,75 CPU
Runtime-Obergrenze auf einem geteilten Zwei-CPU-Host fehlt außerdem der TT-Peakbeleg.
Root muss erst ein belastbares Kapazitätsbudget oder eine ausdrücklich freigegebene
Kapazitätserweiterung binden. Keine fremden Dienste stoppen und keine Limits blind
heruntersetzen. Auch Pull-/Entpack-/PG-Wachstum brauchen ein gebundenes Diskbudget.

## Authentifizierung und Secrets

- Gemeinsamer Realm **TickyTask**, Directory zwingend auf reale Demo-Wurzelgruppen-ID
  begrenzt. Keine produktiven Realm-Clients oder Secrets aus `MaassIT` übernehmen.
- Vor Runtime zu verifizieren: Authority/Issuer, API-Audience, BFF-Client mit exakten
  Redirects gemäß bestehendem BFF-Vertrag, getrenntes read-only Directory-Konto,
  TT-Token-Exchange-Bindung sowie stabile Demo-Wurzel-/Personal-/`m2`-IDs.
  Leere oder erratene IDs sind keine Freigabe.
- Secretwerte/DSNs kommen ausschließlich über den freigegebenen Secret-Helper aus
  dem Secret-Store zur Laufzeit in den Prozess. Der Kandidat enthält nur zwingende
  Platzhalter, keine Werte, `env_file`, Secretdateien oder gespeicherte `.env`.
  Kein Shelltrace, ENV-/Inspect-Dump oder interpoliertes `compose config` mit echten
  Werten ausgeben/speichern. Runtime-ENV bleibt Docker-Administratoren zugänglich;
  das ist kein Geheimnisschutz gegen Hostadministratoren.
- PG-Adminpasswort nur in PG, Migrations-DSN nur im Migrator, Laufzeit-DSN nur in API;
  BFF-/Directorysecret nur in API. Console erhält ausschließlich öffentliche Werte.
- FormEmbedding bindet exakt `https://flowzer.tickytask.de` und
  `https://demo.tickytask.de`. Sandbox/CSP/Einmal-Link-Verträge werden nicht gelockert.
- Mail, HTTP-Connector, KI, externe Webhooks und Inbound-Trigger bleiben geschlossen.
  Ticket-Serviceaktionen bleiben bis zur separat reviewten TT-Worker-/Policy-Bindung
  ebenfalls geschlossen; **alle vier Ticketaktionen bleiben Teil des Gesamtziels**.

## Spätere serielle Ausführung – kein Copy/Paste-Deploymentauftrag

Erst nach Roots konkreter SHA-/Konfigurations-/Runtimeöffnung:

1. Kapazität, PostgreSQL-Digest, CIDR, Namen-/Volume-Kollisionsfreiheit, Compose-
   Version und Gateway-/ACME-Bindung prüfen. DNS-Ziel und TLS gesondert freigeben.
2. Nur eigenen PG starten. Bestehende SQL-Bootstrap-/Rechte-Dateien mit den obigen
   eigenen Namen ausführen und Rollenpasswörter über den Secretweg vergeben.
3. Nur den expliziten `operations`-Migrator mit seiner DDL-Identität ausführen;
   Exit 0 und vollständigen Migrationsstand belegen. Kein Schema-DDL beim API-Start.
   Das Profil ist ein manueller Operationsschritt: ein gewöhnliches `up` führt ihn
   **nicht** aus und ersetzt seinen Erfolgsbeleg nicht.
4. Laufzeitrechte kontrollieren, API mit Laufzeitidentität starten, Readiness belegen,
   danach Console. Bei Fehlern nur eigene neue Dienste geordnet stoppen; keine
   Rollback-/Löschaktion an Volumes oder fremden Diensten improvisieren.
5. Root verbindet den vorhandenen Proxy additiv mit Edge und setzt den freigegebenen
   DNS-Eintrag. HTTPS/Issuer/Audience/Directory-Scope und negative Rechteprüfungen
   tatsächlich prüfen, bevor TT diesen Ursprung verwendet.
6. Danach erst TT-Demo-Promotion, Urlaubsdefinitionen und reale Browserabnahme seriell
   koordinieren. Virtuelle Zeit-/Source-Tests sind **keine** reale 45-Minuten-Abnahme.

## Tatsächlich noch fehlende Read-only-Bindungen (Owner: Demo-Koordinator)

Die Docker-Inventur muss nicht wiederholt werden. Es fehlen gezielt:

- Host-/Docker-Routen samt Netzen zur Wahl **eines** konfliktfreien Edge-CIDRs;
  Compose-V2-Version/Schemafähigkeit und Kollisionsfreiheit der neuen Namen/Volumes.
- Verifizierter PostgreSQL-17-Digest samt Hostplattform und Platzbedarf.
- Belastbares freies RAM-/CPU-/Diskbudget beziehungsweise freigegebene Erweiterung.
- Companion→Proxy-Verknüpfung, gemeinsame Zertifikats-/vhost-/html-Volumes und
  konfigurierte ACME-Kontakt-/Account-Bereitschaft als redigierte Metadaten; **keine**
  ENV-/Zertifikats-/Private-Key-Inhalte. Öffentliche DNS-Ausgangslage hat Root separat
  geprüft (fehlender Flowzer-Eintrag); kein zweiter eigener DNS-/SSH-Poller.
- Demo-Realm-Client-/Audience-/Gruppen-/Benutzer-IDs und Secret-Referenzen, nicht Werte;
  anschließend separater freigegebener Konfigurationsplan.

Root-Belege vom 15.10.2026: `dev01-corrected-five-read-full-inventory-terminal.safe.json`
(SHA256 `363181425bfb15ab154890206e8b92c41d017c613da0ad2bdf7b5a058961a6b7`),
`flowzer-demo-public-dns-target-read-20261015.json`
(`abfcaf23a2b4bb438e57e0f963736a435e4203a06f8498b62bca319a116764a6`) und
`dev01-resources-ports-acme-terminal.safe.json`
(`59045bf1182a569c20fbed4ca2aa265f9916421f6613948926352870b24d2a7b`).
Diese Host-Fakten wurden durch Root erhoben; lokale Tests lesen nur Source.

## Isolierte Verifikation

```sh
python3 -m unittest discover -s deploy/tickytask-demo -p 'test_*.py'
```

Zehn Tests schützen Topologie, Digestbindung, Rollen-/Credentialtrennung, Origins,
explizite Migration, persistente Volumes, Effektsperren und Ressourcenobergrenzen.
TDD: zuerst fehlendes Manifest → zehn Fehler, danach Manifest → zehn grüne Tests.
Kein Docker-Daemon, Netzwerk, echter Secretwert, Datenbankzugriff oder Hoststart in
diesen Tests. Zusätzlich lokal mit Docker Compose v5.6.0 erfolgreich syntaktisch
geprüft: `docker compose -f deploy/tickytask-demo/compose.json config --no-interpolate --quiet`.
Dabei keine ENV-Datei und keine Secretwerte geladen, kein Pull/Build/Start und kein
Daemonzugriff ausgelöst. Die Compose-Version auf Dev01, Runtimefähigkeit, echte
Ressourcen und HTTPS bleiben separate, noch nicht belegte Gates. Source-Review ist
keine Liveabnahme.
