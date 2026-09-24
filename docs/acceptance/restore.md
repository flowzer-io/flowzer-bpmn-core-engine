# Abnahme: Sicherung und Wiederherstellung (R2a)

Protokoll des Pakets R2a „Sicherungs- und Wiederherstellungsskripte härten“. Geprüft werden
`scripts/runtime/backup.sh`, `scripts/runtime/restore.sh` und das Rechteskript
`deploy/postgresql/02-laufzeitrechte.sql` gegen einen echten PostgreSQL-17-Server. Der Nachweis
ist der Skripttest `scripts/runtime/tests/backup-restore.test.sh`, der lokal und in der CI
(Job `backup_restore_scripts`, kein Pflicht-Check) läuft. Bedienung und Reihenfolge stehen im
[Runbook](../RUNBOOK-PILOT.md) unter „Backup und Restore“, die Zusicherungen im Überblick in
[Betrieb](../OPERATIONS.md#sicherung-und-wiederherstellung-von-postgresql).

## Zweck

Eine Sicherung ist erst etwas wert, wenn sie sich zurückspielen lässt und die API danach mit
ihren getrennten Rollen arbeitet. Der Test belegt deshalb nicht nur `pg_dump`/`pg_restore`,
sondern den ganzen Weg einer Installation: Rollenskript, Migration, Sicherung samt
Dateiablage und Keyring, Restore in eine vorbereitete Datenbank, Rechte der Laufzeitrolle,
Schutz der Quelle und abschließend `--check-config` der API gegen das Ziel.

## Ausgangslage, praktisch geprüft

Vor der Umsetzung wurden drei Annahmen aus der Analyse in einem Wegwerf-Container
(`postgres:17-alpine`, Server 17.11) mit dem bisherigen Rollenskript und den bisherigen
Befehlen nachgestellt; Quelle migriert mit `--migrate` (20 Migrationen), zwei Definitionen und
ein Katalogordner als Laufzeitrolle geschrieben.

| Annahme | Beobachtung | Folge in R2a |
|---|---|---|
| (a) Restore in eine mit `01-datenbank-und-rollen.sql` vorbereitete Datenbank scheitert | bestätigt: Das bisherige `restore.sh` zählt die vorab angelegte, leere `schema_migrations` als „1 Tabelle“ und verweigert. Ein direktes `pg_restore --exit-on-error` scheitert noch früher an `CREATE SCHEMA flowzer` („schema already exists“), auch nach Entfernen der Tabelle | `restore.sh` wertet ein Schema mit nur einer leeren `schema_migrations` als leer, entfernt die Tabelle und spielt mit `pg_restore --schema=<schema>` ein – das überspringt `CREATE SCHEMA` und behält das vorbereitete Schema samt Rechten |
| (b) `--force` verliert USAGE und Default-Privileges | bestätigt: Nach `DROP SCHEMA … CASCADE` und Restore hat die Laufzeitrolle kein `USAGE` mehr, `pg_default_acl` ist leer, Lesen als Laufzeitrolle endet mit „permission denied for schema flowzer“ | `02-laufzeitrechte.sql` läuft nach jedem Restore (`--runtime-role`), sonst deutliche Warnung mit fertigem Befehl |
| (c) Ohne `--force` bekommt `schema_migrations` mehr als SELECT | bestätigt: Über die Default-Privileges der Migrationsrolle erhält die Laufzeitrolle auf allen wiederhergestellten Tabellen `SELECT/INSERT/UPDATE/DELETE` – auch auf `schema_migrations` | 02 setzt `schema_migrations` auf nur `SELECT` zurück; die Abschlussprüfung von `restore.sh` verlangt genau das |

Zusätzlich geprüft: `01-datenbank-und-rollen.sql` bindet 02 jetzt per `\ir` ein. Die Rechte
(Datenbank-, Schema-, Tabellen-ACL und Default-Privileges) sind nach dem bisherigen und dem
neuen 01 identisch – frisch, nach erneutem Lauf und nach Migration plus erneutem Lauf (Vergleich
von `pg_database.datacl`, `pg_namespace.nspacl`, `pg_class.relacl`, `pg_default_acl`). Der
Container-Init des Abnahme-Stacks aus R1b (`tests/installation-auth/postgres/10-flowzer-init.sh`
über `docker-entrypoint-initdb.d`) wurde mit denselben Mounts in einem einzelnen
PostgreSQL-Container nachgestellt: Mit dem jetzt eingehängten Verzeichnis `deploy/postgresql`
entstehen dieselben Rechte; der vollständige R1b-Lauf wurde für R2a nicht wiederholt.

## Ablauf des Skripttests

Der Test startet `postgres:17-alpine` mit zufälligem Hostport in einem eigenen Docker-Netz,
legt Quelle und Ziel mit dem ausgelieferten Rollenskript an (gemeinsame Migrations- und
Laufzeitrolle), migriert die Quelle mit der gebauten API (`WebApiEngine.dll --migrate`),
schreibt als Laufzeitrolle zwei Zeilen in `definitions` und einen Ordner in
`workflow_folders` und legt eine Dateiablage und einen Keyring unter absoluten Pfaden an. Die
PostgreSQL-Werkzeuge der Skripte laufen mit `FLOWZER_PG_CLIENT=docker` im selben Image, weil
lokal installierte Clients (etwa die der Linux-Distribution auf dem CI-Runner) älter als der
Server sein können und `pg_dump` dann verweigert. Aufräumen
per `trap`; `--keep` lässt Container und Arbeitsverzeichnis stehen.

| Fall | Prüfung |
|---|---|
| 1 Sicherung | Dump, `.dump.sha256`, `.meta`, `-files.tgz` samt `.sha256`; keine `.tmp`-Reste; Verzeichnis 700, Dateien 600; Prüfsummen stimmen; `.meta` mit Host, Datenbank, Schema, App-Version, Migrationsstand (Anzahl/höchste Version), Server- und `pg_dump`-Version, absoluten Quellpfaden; Archiv enthält Ablage und Keyring relativ |
| 2 Abgebrochene Sicherung | eine fremde, für die Migrationsrolle unlesbare Tabelle lässt `pg_dump` scheitern: Exit ≠ 0, Fehler sichtbar, **keine** Datei im Sicherungsverzeichnis |
| 3 Restore ins vorbereitete Ziel | ohne `--force`, mit `--runtime-role` und `FLOWZER_RUNTIME_PASSWORD`: leere Historie als leer erkannt, Bestand wie Quelle, Rechte vollständig, `schema_migrations` nur SELECT, Lesen als Laufzeitrolle; Dateien unter `--files-root` identisch |
| 4 Restore in die Quelle | ohne Schalter und mit `--force` verweigert („ist die Quelle dieser Sicherung“); Quelle unverändert (Schema-OID, Bestand, Migrationen) |
| 5 Manipulierte Prüfsumme | Abbruch vor jedem Zugriff, auch mit `--force`; Ziel unverändert |
| 6 Fehlende Prüfsumme | mit `--require-checksum` Abbruch; Ziel unverändert |
| 7 Fehlende `.meta` | `--force` ohne `--allow-same-database` verweigert; Ziel unverändert |
| 8 Dateikonflikt | `--files` an die `.meta`-Pfade (= Quelldateien) ohne `--overwrite-files` verweigert, auch mit `--force`; Quelldatei und Ziel unverändert |
| 9 `--force` ins Ziel | Schema neu angelegt (neue OID), Bestand wie Quelle, Rechte samt Default-Privileges wieder da (per `has_*_privilege`, ohne Passwort); mit `--overwrite-files` Dateien an den absoluten `.meta`-Pfaden – fehlende angelegt, geänderte ersetzt |
| 10 `--force` ohne Laufzeitrolle | Warnung mit Befehl, der die frühere USAGE-Rolle bereits nennt; ohne 02 kein USAGE; 02 von Hand ausgeführt stellt die Rechte her, ein zweiter Lauf ändert nichts |
| 11 `--check-config` | gegen das Ziel mit Laufzeit- und Migrationskennung: „Migrationen OK aktuell“, „Ablage OK“, keine Fehlerzeile (Exit 2 nur wegen `Authentication:Scheme=None` im Test) |
| 12 Keine Passwörter | keine der Testpasswörter in einer Skriptausgabe |

## Ergebnis der Läufe

| Angabe | Wert |
|---|---|
| Datum | 2026-09-24 |
| Code-Stand | `main` (174259a) zuzüglich dieses Pakets |
| Host | macOS 26.6.2, arm64, Docker Desktop (Engine 29.8.0), .NET SDK 10.0.100 |
| Server und Werkzeuge | `postgres:17-alpine`, PostgreSQL 17.11, `pg_dump (PostgreSQL) 17.11` |

| Lauf | Zeitpunkt (MESZ) | Dauer | Ergebnis |
|---|---|---|---|
| 1 | 2026-09-24 09:17 | 20 s | 12 Fälle bestanden, 52 Einzelprüfungen |
| 2 | 2026-09-24 09:21 | 19 s | 12 Fälle bestanden, 52 Einzelprüfungen |
| 3 | 2026-09-24 09:22 | 17 s | 12 Fälle bestanden, 52 Einzelprüfungen |

Alle drei Läufe auf demselben Code-Stand, jeweils vom leeren Container bis zum Aufräumen
(kein Container, kein Netz, kein Arbeitsverzeichnis blieb zurück); die API war bereits
gebaut (inkrementeller `dotnet build` rund 4 s). Frühere Zwischenläufe deckten zwei Fehler im
Test selbst auf (nicht normalisiertes `TMPDIR` unter macOS, Boolean-Ausgabe `f` statt
`false`) und einen in `restore.sh` (Rechteprüfung rief `has_sequence_privilege` auch für
Tabellen auf). Die dateibezogenen Teile (GNU tar 1.35, `sha256sum`, GNU find) wurden
zusätzlich in `ubuntu:24.04` nachgestellt, weil der CI-Runner GNU- statt BSD-Werkzeuge hat.

Begleitend: `dotnet build core-engine.sln` ohne Fehler; die bestehenden Tests
`HealthReadiness_ShouldReportPendingAndCurrentMigrationState` und
`BackupAndRestore_ShouldRoundTripSchemaAndMigrationState`
(`PostgreSqlStorageIntegrationTest.OperationsReadiness.cs`) grün; `shellcheck` 0.11.0 ohne
Befund auf allen vier Skripten; `actionlint` ohne Befund auf `ci.yml`.

## Grenzen

- **Kein Point-in-Time-Recovery.** Die Skripte erzeugen Momentaufnahmen; alles zwischen zwei
  Sicherungen ist bei einem Restore verloren. WAL-Archivierung und PITR bleiben beim
  Datenbankbetrieb der Installation.
- **Keine Aufbewahrung, kein Zeitplan, kein zweiter Ort.** Die Skripte löschen nichts,
  planen nichts und kopieren nichts weg (#325).
- **Ein Paketstand.** Sicherung und Restore laufen im Test mit demselben Paket. Der volle
  Nachweis über Paketstände hinweg (älteres Release sichern, zurückspielen, neues Paket
  migrieren, laufende Instanzen fortsetzen) folgt als **R2b** mit eigenem Upgrade-/Restore-Rig.
- **Herkunftsprüfung textuell.** „Ziel = Quelle“ vergleicht Host und Datenbanknamen aus der
  `.meta` mit der Zielverbindung. Wer dieselbe Datenbank über einen anderen Hostnamen, eine
  IP-Adresse oder einen Pooler anspricht, wird nicht erkannt.
- **Prüfsumme ist kein Manipulationsschutz.** Sie erkennt Beschädigung beim Kopieren; wer
  Dump und `.sha256` gemeinsam ändern kann, fällt nicht auf. Das Sicherungsverzeichnis bleibt
  deshalb nur für den Besitzer lesbar und gehört an einen geschützten Ort.
- **Laufender Stack nicht abgesichert.** Die Skripte prüfen nicht, ob die API noch schreibt;
  der Stack muss vor Sicherung und Restore gestoppt sein.
- **Keyring nur als Dateien belegt.** Der Test zeigt, dass die Keyring-Dateien unverändert
  zurückkommen, nicht, dass bestehende BFF-Sitzungen danach gültig bleiben.
- **Rollen müssen im Zielcluster existieren.** `restore.sh` legt keine Rollen an; das bleibt
  Aufgabe von `01-datenbank-und-rollen.sql`.
