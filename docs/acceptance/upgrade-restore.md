# Abnahme: Aktualisierung mit laufenden Instanzen und Wiederherstellung (R2b)

Protokoll des Pakets R2b „Upgrade-/Restore-Rig“. Geprüft wird mit **echten Images** im
Compose-Stack, was im Betrieb bei einer Aktualisierung und bei einem Restore passiert: wartende
Instanzen überstehen den Image-Wechsel und den Schemasprung, eine scheiternde Migration ändert
nichts, und eine Sicherung läuft in einer zweiten Datenbank mit der echten API weiter. Der
Nachweis ist `tests/upgrade-restore/run.sh`, der lokal und in der CI (Job
`upgrade_restore_rig`, kein Pflicht-Check) läuft. Bedienung steht im
[Runbook](../RUNBOOK-PILOT.md) unter „Aktualisieren“ und „Backup und Restore“, die
Zusicherungen im Überblick in [Betrieb](../OPERATIONS.md).

Ergänzt werden damit zwei vorhandene Nachweise: der In-Process-Test
`Upgrade_ShouldKeepRunningInstancesUsableAcrossAllMigrations` (R2d, Migrator und Engine im
Testprozess, Ausgangslagen 012 und 019) und der Skripttest der Sicherungsskripte
([restore.md](restore.md), R2a, ein Paketstand, ohne API-Instanzen).

## Ausgangslage

- **Ältere Images gibt es in GHCR nur mit Schemastand 020.** Die Images beginnen mit #348;
  auch `sha-5a2d9b38eec4` (Release #344) trägt bereits 020, ebenso das aktuelle Image. Der
  reale Produktionsfall seit dem 21.09. ist deshalb ein **Image-Wechsel ohne Migration**
  (Fall 1). Die letzte Aktualisierung mit Schemasprung war #320 (016) → #344 (020); für sie
  gibt es kein Image, deshalb ein **Klartext-Fixture** (Fall 2).
- **Keine der Migrationen 017 bis 020 legt eine Tabelle ohne `IF NOT EXISTS` an**, und der
  Migrator liest nur eingebettete Ressourcen (keine externen Dateien). Eine gleichnamige
  Tabelle allein lässt also keine Migration scheitern. Fall 3 legt deshalb eine gleichnamige
  Tabelle **mit abweichender Struktur** an: 019 überspringt `CREATE TABLE inbound_triggers`
  und scheitert am anschließenden Unique-Index über die fehlende Spalte `trigger_key`
  (`42703`) – nachdem 017 und 018 in derselben Transaktion bereits gelaufen sind.
- **Ohne Identity Provider** (`Authentication__Scheme=None`) verlangt die API für schreibende
  Aufrufe trotzdem einen aufgelösten Benutzer („A resolved user context is required“). Den
  technischen Kopf `X-Flowzer-UserId` wertet sie nur im Development-Modus aus; die API läuft im
  Rig deshalb mit `ASPNETCORE_ENVIRONMENT=Development`, `migrate` wie in Produktion mit
  `Production`. Ablage, Migrationsstand und Engine sind davon unberührt.

## Aufbau

`tests/upgrade-restore/compose.yml` enthält `db` (PostgreSQL), `migrate` (`--migrate` mit der
Migrationsrolle) und `api` (Laufzeitrolle, `depends_on: migrate: service_completed_successfully`
wie in `compose.coolify.yaml`); ohne Konsole, Caddy und Keycloak. `migrate` und `api` laufen
seit #366 wie in `compose.coolify.yaml` mit `init: true` (Init-Prozess als PID 1). Jede Ausgangslage bekommt
eine eigene Datenbank, angelegt mit `deploy/postgresql/01-datenbank-und-rollen.sql`
(Migrations- und Laufzeitrolle getrennt). Das Skript wählt vor jedem Schritt über
`FLOWZER_UPGRADE_DATABASE` und `FLOWZER_UPGRADE_API_IMAGE` Datenbank und Image; ein Wechsel lässt
Compose `migrate` und `api` neu erzeugen, genau wie ein Image-Wechsel in der Installation.

| Rolle | Image |
|---|---|
| Vorgänger (Fall 1) | `ghcr.io/flowzer-io/flowzer-api:sha-5a2d9b38eec4` (Release #344, Schema 020; nur `linux/amd64`, auf arm64 emuliert) |
| Aktuell | `ghcr.io/flowzer-io/flowzer-api:${FLOWZER_IMAGE_TAG:-latest}`; mit `--build-current` aus diesem Stand gebaut (so in der CI) |
| Stand 016 (Fall 3) | aus Commit `92d8557` (Release #320) gebaut: `git archive` in ein Temp-Verzeichnis, `docker build` mit dessen `Dockerfile.api` |
| PostgreSQL | `FLOWZER_TEST_PG_IMAGE`, Standard `postgres:17-alpine` |

Die drei Workflows liegen unter `tests/upgrade-restore/bpmn/` (aus dem R2d-Test übernommen):
Benutzeraufgabe mit gebundenem Formular `UpgradeApproval`, Service-Task für einen externen
Worker (`upgrade-zahlung`), Zwischen-Timer. Der Timer hat bewusst eine Frist von zwei Sekunden,
und der Timer-Scheduler ist in jedem Schritt zunächst aus: So bleibt der Timer beobachtbar
wartend, obwohl er fällig ist. Zum Abschluss startet der Rig die API mit eingeschaltetem
Scheduler (Produktionseinstellung); die Engine holt den fälligen Timer dann schon beim Start
nach, bevor die Bereitschaftsprobe grün wird.

Abschließen heißt in jedem Fall: Aufgabe über `POST /usertask` abschließen, Auftrag über
`POST /job/fetch` holen und über `POST /job/{id}/complete` zurückmelden, Timer über den
Neustart feuern lassen; danach alle drei Instanzen `Completed` und keine Timer-Anmeldung mehr.

Unverändert heißt: Der „Laufzeitabdruck“ – Instanzen, Aufgaben, Aufträge (mit Sperre und
Versuchen), Timer (mit Fälligkeit), Deployments (mit den gebundenen Formularen im Datensatz)
und Formulare, jeweils mit MD5 des gespeicherten Datensatzes – ist vorher und nachher
zeilengleich. Für Fall 3 zusätzlich der Schemaabdruck (alle Relationen samt Spalten).

Passwörter entstehen je Lauf zufällig (`/dev/urandom`), reisen nur über Umgebungsvariablen und
über stdin, nie über Kommandozeilen. Die Ausgaben liegen unter `tests/upgrade-restore/logs`
(oder `FLOWZER_UPGRADE_RESTORE_LOG_DIR`), ohne Dumps; der letzte Fall prüft, dass keines der
Passwörter darin steht, danach werden sie zur Sicherheit zusätzlich ersetzt. Aufräumen per
`trap` (auch bei SIGTERM, geprüft: Exit 143, kein Container, kein Netz, kein Arbeitsverzeichnis
blieb zurück); `--keep` lässt alles stehen.

## Fälle und Erwartungen

| Fall | Ablauf | Erwartung |
|---|---|---|
| 1 Image-Wechsel ohne Migration | `migrate`/`api` mit dem Vorgänger, drei Instanzen über die API anlegen, `api` stoppen, `migrate`/`api` mit dem aktuellen Image | Vorgänger: 20 Migrationen, `/health/ready` Healthy/UpToDate. Aktuell: `migrate` Exit 0, meldet **0** angewendete Migrationen (allgemein: genau die Differenz der Historie), Formularbindungs-Upgrade läuft (0 ergänzt), `/health/ready` Healthy/Ready/UpToDate/0 ausstehend/20, Laufzeitabdruck unverändert, alle drei warten und lassen sich abschließen |
| 2 Schemasprung 016 → aktuell | Fixture in eine mit 01 vorbereitete Datenbank einspielen (als Migrationsrolle, danach 02), `migrate`/`api` mit dem aktuellen Image | Fixture 16/16; `migrate` meldet 4 angewendete Migrationen **17, 18, 19, 20**, Historie danach 20/20, Formularbindungs-Upgrade läuft (0 ergänzt: 016 bindet schon beim Deployment), `/health/ready` UpToDate, Laufzeitabdruck unverändert, alle drei warten und lassen sich abschließen |
| 3 Negative Migration und Rückweg | Fixture einspielen, Stand 016 starten (Instanzen warten), `api` stoppen, Kollisionstabelle anlegen, `migrate`/`api` mit dem aktuellen Image, danach wieder das Image des Stands 016 | `docker compose up` endet mit Fehler; `migrate` endet **von selbst mit Exit 1** (seit #366; vorher nur „nicht 0“), ohne unbehandelte Ausnahme, und nennt in einer Fehlerzeile `PostgreSQL migration 019_inbound_triggers (version 19) failed`, `Npgsql.PostgresException (SqlState 42703)` und `42703: column "trigger_key" does not exist`, meldet keine angewendete Migration; der `api`-Container des aktuellen Images ist angelegt, aber **nie gestartet**; `schema_migrations` (1–16), Schemaabdruck und Laufzeitabdruck unverändert, keines der Objekte aus 017–020 vorhanden. Rückweg: `migrate` des Stands 016 Exit 0, 0 angewendet, `/health/ready` Healthy, alle drei warten und lassen sich abschließen – **ohne Restore** |
| 4 Restore in eine zweite Datenbank | Quelle aus Fall 1 mit dem aktuellen Image: drei weitere Instanzen (warten), `api` stoppen, `backup.sh` (mit Dateiablage und Keyring), zweite Datenbank mit 01, `restore.sh --runtime-role --files --files-root`, `api` gegen den Klon | `backup.sh` Exit 0, `.meta` mit `database=flowzer`, `schema_migrations_max=20`, `app_version`; `restore.sh` Exit 0, erkennt das vorbereitete Ziel als leer, liest als Laufzeitrolle; Dateien unter `--files-root` identisch; Migrationsstand und Zeilenzahlen aller 34 Tabellen wie in der Quelle; `migrate` 0 angewendet, `/health/ready` UpToDate; **Instanzliste und Zustände identisch** (6 Instanzen, 3 abgeschlossen, 3 wartend); die wartenden laufen im Klon zu Ende, in der Quelle warten dieselben drei unberührt weiter |
| 4b Sicherung eines älteren Stands | Fixture 016 in eine eigene Datenbank, `backup.sh --no-files`, Restore in eine weitere Datenbank, `migrate`/`api` mit dem aktuellen Image | `.meta` `schema_migrations_max=16`; Klon nach Restore 16/16; `migrate` wendet 17–20 an; `/health/ready` UpToDate; Laufzeitabdruck wie in der gesicherten Ablage; alle drei warten |
| Abschluss | alle Ausgabedateien durchsuchen | keines der drei Passwörter in einer Ausgabe |

`--cases 1,2,3,4` wählt Fälle aus (4 bringt 1 mit, 4b gehört zu 4). Fall 5 aus dem Auftrag ist
die Variable `FLOWZER_TEST_PG_IMAGE` (siehe Ergebnis).

## Der Fixture des Stands 016

`tests/upgrade-restore/fixtures/schema-016/flowzer-schema-016.sql` ist ein Klartext-Dump
(`pg_dump --schema=flowzer --no-owner --no-privileges`, PostgreSQL 17.11) mit Kopfkommentar
zur Herkunft: 84 564 Byte, 1 330 Zeilen; 16 Einträge in `schema_migrations`, ein Formular,
drei Deployments samt BPMN, drei Instanzen, eine Aufgabe (mit Fälligkeitszeile), ein Auftrag,
ein Timer, sechs Knotenereignisse; keine Rollen, Rechte oder Passwörter. Erzeugt hat ihn
`tests/upgrade-restore/fixtures/make-schema-016.sh` (14 s mit vorhandenem Build-Cache):

1. API aus `92d8557` bauen (`git archive 92d8557 | tar -x` in ein Temp-Verzeichnis,
   `docker build -f Dockerfile.api`; im flachen Klon lädt das Skript genau diesen Commit nach).
   Der Bau gelang mit den Werkzeugen jenes Stands (SDK-Image 10.0.103); ein Ausweichen auf
   einen anderen Stand war nicht nötig. Scheitert er künftig, bricht das Skript mit den letzten
   Zeilen des Bauprotokolls ab.
2. PostgreSQL mit 01 vorbereiten, `--migrate` und API des Stands starten (16 Migrationen).
3. Über die HTTP-API Formular, drei Workflows und je eine Instanz anlegen; Wartezustände prüfen.
4. API stoppen, Klartext-Dump schreiben.

Reproduzierbar ist der Weg, nicht jedes Byte: IDs, Zeitstempel und der `\restrict`-Schlüssel
von `pg_dump` entstehen bei jedem Lauf neu. Der Rig liest die Instanz-IDs deshalb aus der
eingespielten Datenbank, nicht aus dem Fixture.

## Ergebnis der Läufe

| Angabe | Wert |
|---|---|
| Datum | 2026-09-24 |
| Code-Stand | `main` (0d5f083, einschließlich #361) zuzüglich dieses Pakets |
| Host | macOS 26.6, arm64, Docker Desktop (Engine 29.8.0, Compose 5.5.1) |
| Aktuelles Image | `ghcr.io/flowzer-io/flowzer-api:latest` = Revision 0d5f083 (arm64); Kontrolllauf mit `--build-current` |
| PostgreSQL | `postgres:17-alpine` (17.11); zusätzlich `postgres:18-alpine` (18.6) |

| Lauf | Aufruf | Dauer | Ergebnis |
|---|---|---|---|
| 1 | `run.sh` (Stand vor Fall 4b) | 168 s | 5 Fälle, 114 Prüfungen bestanden, 1 Befund (PID 1) |
| 2 | `FLOWZER_TEST_PG_IMAGE=postgres:18-alpine run.sh --cases 1,4` | 81 s | 3 Fälle, 61 Prüfungen bestanden |
| 3 | `FLOWZER_TEST_PG_IMAGE=postgres:18-alpine run.sh` (Stand vor Fall 4b) | 140 s | 5 Fälle, 114 Prüfungen bestanden, 1 Befund |
| 4 | `run.sh` | 157 s | 6 Fälle, 127 Prüfungen bestanden, 1 Befund |
| 5 | `run.sh --build-current` | 177 s | 6 Fälle, 127 Prüfungen bestanden, 1 Befund (Bau mit Build-Cache rund 25 s) |
| 6 | `run.sh` (Endstand, 14:08 MESZ) | 151 s | 6 Fälle, 127 Prüfungen bestanden, 1 Befund |
| 7 | `FLOWZER_TEST_PG_IMAGE=postgres:18-alpine run.sh` (Endstand, 14:11 MESZ) | 150 s | 6 Fälle, 127 Prüfungen bestanden, 1 Befund |

Zeitanteile von Lauf 6: Vorbereitung 4 s, Fall 1 26 s (das Vorgängerimage läuft auf arm64
emuliert), Fall 2 16 s, Fall 3 62 s (davon 30 s Wartezeit des Watchdogs, siehe Befund 1),
Fall 4 28 s, Fall 4b 14 s. Alle Läufe vom leeren Zustand bis zum Aufräumen; danach stand kein
Container des Rigs mehr. Die Läufe 1 bis 5 liefen auf Zwischenständen des Rigs (vor Fall 4b
beziehungsweise bevor der Laufzeitabdruck Deployments und Formulare umfasste); maßgeblich sind
die Läufe 6 und 7 auf dem Endstand.

**Nachlauf #366 (2026-09-26, arm64, `run.sh --build-current` auf `main` e93e66c zuzüglich
#366):** 6 Fälle, 131 Prüfungen bestanden, **0 Befunde**, 140 s. Fall 3 dauerte 27 s statt
62 s: `migrate` endete von selbst mit Exit 1 und der Fehlerzeile zu `019_inbound_triggers`
(`Npgsql.PostgresException`, SqlState 42703), ohne Eingreifen des Watchdogs.

**PostgreSQL 18 (Fall 5, optional):** Alle Fälle laufen auch mit `postgres:18-alpine` (18.6),
einschließlich Einspielen des mit `pg_dump` 17 erzeugten Fixtures sowie `backup.sh`/`restore.sh`
mit den Werkzeugen aus dem 18er-Image. Produktion läuft auf 17; die CI prüft nur 17.

Frühere Zwischenläufe deckten Fehler im Rig selbst auf (BSD-`seq` hängt das Trennzeichen an,
fehlende Typumwandlung von `relkind` in SQL), keinen im Produkt.

## Befunde

1. **Scheiternder `--migrate` endet auf arm64 nicht (PID 1) – behoben mit #366.** Nach der
   unbehandelten Ausnahme bleibt der Prozess im Container mit voller CPU-Last stehen; `docker
   compose up --wait` wartet dann unbegrenzt, `api` startet nie. Ursache: Die .NET-Laufzeit
   bricht per `abort()` ab, und `SIGABRT` wird für PID 1 ohne eigenen Handler ignoriert. Auf
   amd64 endet derselbe Prozess mit Exit 139 (beobachtet mit dem amd64-Vorgängerimage unter
   Emulation; der native amd64-Wert kommt aus dem ersten CI-Lauf). Mit `docker run --init`
   endet er auf arm64 nach 0,4 s mit Exit 134. Nachstellen (Stand vor #366):

   ```bash
   docker run --rm -e Storage__Provider=PostgreSql \
     -e 'Storage__PostgreSql__ConnectionString=Host=127.0.0.1;Port=1;Database=x;Username=y;Password=z;Timeout=3' \
     ghcr.io/flowzer-io/flowzer-api:latest --migrate   # arm64: haengt; mit --init: Exit 134
   ```

   Genauer untersucht in #366 (Ubuntu 24.04, glibc 2.39, .NET 10.0.12): Nach dem verworfenen
   `SIGABRT` greift in `abort()` die architekturabhängige Abbruchinstruktion. Auf amd64 ist das
   `hlt`, das als erzwungenes `SIGSEGV` auch PID 1 beendet (Exit 139). Auf arm64 ist es
   `brk #0x3e8`; das daraus folgende `SIGTRAP` fängt die .NET-Laufzeit selbst ab und kehrt zur
   selben Instruktion zurück – eine Endlosschleife (mit `gdb` belegt: Programmzähler auf
   `abort+432`, dazwischen `pthread_sigmask` aus `libcoreclr`). Logger, Host und V8 sind nicht
   beteiligt; ClearScript ist im `--migrate`-Prozess nicht einmal geladen.

   Behoben auf zwei Wegen: `RunMigrationsAsync` fängt jeden Fehler, protokolliert ihn als
   Fehlerzeile (Migration und Version, Ausnahmetyp, SQLSTATE) und liefert Exit 1; der Migrator
   benennt die gescheiterte Migration über `SchemaMigrationFailedException`, ohne die
   Transaktionssemantik zu ändern. Zusätzlich laufen `migrate` und `api` mit `init: true`.
   Nachgeprüft ohne Init-Prozess, also als PID 1: Exit 1 nach 1–3 s auf arm64 und auf amd64
   (emuliert), ebenso mit `--init`. Fall 3 verlangt seither genau Exit 1 ohne Eingreifen des
   Rigs; hängt `migrate` wieder oder endet es anders, bricht der Rig mit Fehler ab (kein
   „BEFUND“ mehr).
2. **Compose erzeugt `api` neu, bevor `migrate` läuft.** Wechselt das Image, legt
   `docker compose up` beide Container in der Erzeugungsphase neu an und entfernt dabei den
   laufenden alten `api`-Container; gestartet wird `api` erst nach erfolgreichem `migrate`. In
   der Vorarbeit mit laufender API beobachtet, in Fall 3 belegt (`api` des aktuellen Images im
   Zustand `created`, `StartedAt` leer). Folge: Während der Migration läuft keine API, und nach
   einer gescheiterten Migration läuft keine, bis das bisherige Image wieder gesetzt ist. Ob
   Coolify Compose genauso aufruft, ist am Zielsystem zu prüfen.
3. **Rückweg ohne Restore.** Weil der Migrator alle ausstehenden Migrationen in **einer**
   Transaktion anwendet, lässt eine scheiternde Migration Schema, Historie und Laufzeitzustand
   exakt stehen (Fall 3: auch die Indizes aus 017/018, die vor dem Fehler schon angelegt waren,
   sind zurückgerollt). Der Rückweg ist dann das bisherige Image, kein Restore.
4. **Rauschen im Migrationslog.** Jeder `--migrate`-Lauf aller geprüften Images beginnt mit
   „Cannot load library libgssapi_krb5.so.2“ (Npgsql prüft GSSAPI, das im Image fehlt). In
   allen Läufen war das folgenlos, kann Betreiber aber beunruhigen.

## Grenzen

- **Ein synthetischer Datenbestand.** Drei Workflows mit je einer Instanz, kein
  Produktionsabzug. Verzeichnisse, Idempotenzeinträge, KI-Läufe und Entwürfe sind im Fixture
  leer; ihre Migrationen deckt der Schema-Drift-Test ab, ihr Verhalten nach einem Upgrade nicht.
- **Formularbindungs-Upgrade nur als „läuft, ergänzt 0“.** Beide Ausgangslagen binden
  Formulare schon beim Deployment. Den Pfad mit Deployments ohne Bindung (vor M0) belegt
  weiterhin nur der In-Process-Test ab Stand 012.
- **Rückweg nur nach gescheiterter Migration.** Ein Rückweg nach **erfolgreicher** Migration
  (altes Image auf neuerem Schema) ist nicht geprüft; dafür bleibt der Restore der Sicherung
  der dokumentierte Weg.
- **Kein Identity Provider, keine Konsole.** Anmeldung und BFF prüft die Abnahme R1b/R1c; die
  API läuft hier im Development-Modus mit technischem Benutzerkopf.
- **Dateien synthetisch.** Mit PostgreSQL und ohne BFF liest die API weder Dateiablage noch
  Keyring; Fall 4 belegt nur, dass beide über `--files-root` unverändert ankommen.
- **Timer über den Scheduler-Schalter.** Der Rig friert den Wartezustand ein, indem er den
  Scheduler erst zum Abschluss einschaltet. Ein Timer, der während der Aktualisierung von einem
  laufenden Scheduler gefeuert würde, ist nicht Gegenstand.
- **Stand 016 nicht aus GHCR.** Das Image des Stands 016 entsteht aus dem Quellstand; das
  damals ausgelieferte Image kann sich in Basis-Images und Paketständen unterscheiden.
- **CI-Lauf ausstehend.** Die Läufe oben sind lokal auf arm64; der Job `upgrade_restore_rig`
  baut das aktuelle Image und den Stand 016 auf amd64 und liefert die Ausgaben immer als
  Artefakt `upgrade-restore-logs`.
