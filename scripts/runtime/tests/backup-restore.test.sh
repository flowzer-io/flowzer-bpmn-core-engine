#!/usr/bin/env bash
# Skripttest fuer Sicherung und Wiederherstellung (R2a): startet einen eigenen PostgreSQL-Container,
# legt Quelle und Ziel mit deploy/postgresql/01-datenbank-und-rollen.sql an, migriert die Quelle
# mit der API (--migrate), schreibt Testdaten und prueft backup.sh und restore.sh an echten Faellen:
# Sicherung, Restore in ein vorbereitetes Ziel, Schutz der Quelle, manipulierte Pruefsumme,
# --force mit und ohne Laufzeitrolle, --check-config gegen das wiederhergestellte Ziel.
#
#   scripts/runtime/tests/backup-restore.test.sh          Lauf mit Aufraeumen
#   scripts/runtime/tests/backup-restore.test.sh --keep   Container und Arbeitsverzeichnis stehen lassen
#
# Voraussetzungen: docker und dotnet (SDK laut global.json). Die PostgreSQL-Werkzeuge der Skripte
# laufen bewusst im Container (FLOWZER_PG_CLIENT=docker): lokale Clients sind oft aelter als der
# Server und wuerden pg_dump scheitern lassen.
#
# Umgebungsvariablen:
#   FLOWZER_BACKUP_RESTORE_TEST_LOG_DIR  Ablage der Skriptausgaben (Standard: <arbeitsverzeichnis>/logs)
#   FLOWZER_TEST_API_DLL                 bereits gebaute WebApiEngine.dll; sonst baut der Test (Release)
#   FLOWZER_PG_IMAGE                     Image fuer Server und Clientwerkzeuge (Standard postgres:17-alpine)
#
# Alle Passwoerter hier sind offensichtliche Testwerte fuer einen Wegwerf-Container.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
KEEP=0

for argument in "$@"; do
  case "$argument" in
    --keep) KEEP=1 ;;
    -h|--help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unbekannter Schalter: $argument" >&2; exit 64 ;;
  esac
done

for tool in docker dotnet tar; do
  command -v "$tool" >/dev/null 2>&1 || { echo "Voraussetzung fehlt: $tool" >&2; exit 69; }
done

PG_IMAGE="${FLOWZER_PG_IMAGE:-postgres:17-alpine}"
SUFFIX="$$-${RANDOM}"
CONTAINER="flowzer-backup-restore-test-${SUFFIX}"
NETWORK="flowzer-backup-restore-test-${SUFFIX}"
WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/flowzer-backup-restore-test.XXXXXX")"
# Normalisiert (TMPDIR endet unter macOS auf /); die .meta vergleicht absolute Pfade.
WORK_DIR="$(cd "$WORK_DIR" && pwd)"
LOG_DIR="${FLOWZER_BACKUP_RESTORE_TEST_LOG_DIR:-${WORK_DIR}/logs}"
mkdir -p "$LOG_DIR"

SCHEMA=flowzer
SOURCE_DB=flowzer_br_quelle
TARGET_DB=flowzer_br_ziel
MIGRATION_ROLE=flowzer_br_migration
RUNTIME_ROLE=flowzer_br_runtime
SUPERUSER_PASSWORD=backup-restore-test-superuser
MIGRATION_PASSWORD=backup-restore-test-migration
RUNTIME_PASSWORD=backup-restore-test-runtime
APP_VERSION=r2a-skripttest

started_at=$(date +%s)
case_count=0
current_case=''

phase() {
  printf '\n==> %s (bisher %ss)\n' "$1" "$(($(date +%s) - started_at))"
}

begin_case() {
  case_count=$((case_count + 1))
  current_case="$1"
  printf '\n--- Fall %s: %s\n' "$case_count" "$1"
}

fail() {
  echo "FEHLER${current_case:+ in \"${current_case}\"}: $*" >&2
  exit 1
}

pass() {
  echo "    ok: $*"
}

finish() {
  local status=$?
  trap '' INT TERM
  if [[ "$status" -ne 0 ]]; then
    docker logs "$CONTAINER" >"${LOG_DIR}/postgres.log" 2>&1 || true
    # Fuer die Diagnose: Dateiliste, Pruefsummen und .meta, aber keine Dumps oder Archive.
    if [[ -d "${WORK_DIR}/backups" ]]; then
      ls -la "${WORK_DIR}/backups" >"${LOG_DIR}/backups-listing.txt" 2>&1 || true
      cp "${WORK_DIR}"/backups/*.meta "${WORK_DIR}"/backups/*.sha256 "$LOG_DIR"/ 2>/dev/null || true
    fi
  fi
  if [[ "$KEEP" -eq 1 ]]; then
    echo "Stehen gelassen (--keep): Container ${CONTAINER}, Netz ${NETWORK}, Arbeitsverzeichnis ${WORK_DIR}"
  else
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
    docker network rm "$NETWORK" >/dev/null 2>&1 || true
    if [[ "$status" -ne 0 && "$LOG_DIR" == "${WORK_DIR}"/* ]]; then
      echo "Arbeitsverzeichnis mit Logs bleibt fuer die Diagnose: ${WORK_DIR}" >&2
    else
      rm -rf "$WORK_DIR"
    fi
  fi
  local elapsed=$(($(date +%s) - started_at))
  if [[ "$status" -eq 0 ]]; then
    printf '\nSkripttest Sicherung/Wiederherstellung bestanden: %s Faelle (%ss).\n' "$case_count" "$elapsed"
  else
    printf '\nSkripttest Sicherung/Wiederherstellung fehlgeschlagen (Exit %s, %ss). Logs: %s\n' "$status" "$elapsed" "$LOG_DIR" >&2
  fi
  exit "$status"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# --- Hilfsfunktionen -------------------------------------------------------------------------

# SQL als Superuser im Testcontainer; Ausgabe ohne Rahmen.
sql() {
  local database="$1"
  local statement="$2"
  docker exec "$CONTAINER" psql --no-psqlrc -tA -v ON_ERROR_STOP=1 -U postgres -d "$database" -c "$statement"
}

# SQL ueber TCP mit einer der Flowzer-Rollen (prueft echte Rechte, nicht die des Superusers).
sql_as() {
  local role="$1"
  local password="$2"
  local database="$3"
  local statement="$4"
  docker exec -e PGPASSWORD="$password" "$CONTAINER" \
    psql --no-psqlrc -tA -v ON_ERROR_STOP=1 -h 127.0.0.1 -U "$role" -d "$database" -c "$statement"
}

sha256_of() {
  local line
  if command -v sha256sum >/dev/null 2>&1; then line="$(sha256sum "$1")"; else line="$(shasum -a 256 "$1")"; fi
  printf '%s' "${line%% *}"
}

meta_value() {
  local file="$1"
  local key="$2"
  grep -m 1 "^${key}=" "$file" | cut -d= -f2- || true
}

assert_eq() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [[ "$expected" == "$actual" ]] || fail "${label}: erwartet '${expected}', ist '${actual}'"
  pass "$label = ${actual}"
}

assert_contains() {
  local file="$1"
  local needle="$2"
  local label="$3"
  grep -qF -- "$needle" "$file" || fail "${label}: '${needle}' fehlt in ${file##*/}"
  pass "$label"
}

# Fuehrt ein Skript gegen eine Datenbank aus; die Ausgabe landet in LOG_DIR/<name>.log.
# Rueckgabe ist der Exit-Code des Skripts; ein Fehlschlag bricht den Test hier nicht ab.
run_script() {
  local name="$1"
  local database="$2"
  shift 2
  local status=0
  STORAGE_MIGRATION_CONNECTION_STRING="Host=db;Port=5432;Database=${database};Username=${MIGRATION_ROLE};Password=${MIGRATION_PASSWORD}" \
    "$@" >"${LOG_DIR}/${name}.log" 2>&1 || status=$?
  echo "    ${name}: Exit ${status} (Ausgabe ${name}.log)"
  return "$status"
}

# Rechte der Laufzeitrolle im Ziel als eine Zeile: USAGE/CREATE auf dem Schema, Zahl der Tabellen
# ohne volle Datenrechte, Rechte auf schema_migrations (S/I/U/D) und Zahl der Default-Privileges.
runtime_rights() {
  sql "$TARGET_DB" "SELECT has_schema_privilege('${RUNTIME_ROLE}', '${SCHEMA}', 'USAGE')
    || '/' || has_schema_privilege('${RUNTIME_ROLE}', '${SCHEMA}', 'CREATE')
    || '/' || (SELECT count(*) FROM pg_class c WHERE c.relnamespace = '${SCHEMA}'::regnamespace
               AND c.relkind = 'r' AND c.relname <> 'schema_migrations'
               AND NOT (has_table_privilege('${RUNTIME_ROLE}', c.oid, 'SELECT')
                    AND has_table_privilege('${RUNTIME_ROLE}', c.oid, 'INSERT')
                    AND has_table_privilege('${RUNTIME_ROLE}', c.oid, 'UPDATE')
                    AND has_table_privilege('${RUNTIME_ROLE}', c.oid, 'DELETE')))
    || '/' || has_table_privilege('${RUNTIME_ROLE}', '${SCHEMA}.schema_migrations', 'SELECT')::text
    || has_table_privilege('${RUNTIME_ROLE}', '${SCHEMA}.schema_migrations', 'INSERT')::text
    || has_table_privilege('${RUNTIME_ROLE}', '${SCHEMA}.schema_migrations', 'UPDATE')::text
    || has_table_privilege('${RUNTIME_ROLE}', '${SCHEMA}.schema_migrations', 'DELETE')::text
    || '/' || (SELECT count(*) FROM pg_default_acl WHERE defaclnamespace = '${SCHEMA}'::regnamespace)"
}
EXPECTED_RIGHTS='true/false/0/truefalsefalsefalse/2'

# Bestand einer Datenbank als eine Zeile: Schema-OID, Definitionen, Katalogordner, Migrationen.
database_state() {
  sql "$1" "SELECT (SELECT oid FROM pg_namespace WHERE nspname = '${SCHEMA}')
    || '/' || (SELECT count(*) FROM ${SCHEMA}.definitions)
    || '/' || (SELECT count(*) FROM ${SCHEMA}.workflow_folders)
    || '/' || (SELECT count(*) || ':' || max(version) FROM ${SCHEMA}.schema_migrations)"
}

# --- Vorbereitung ----------------------------------------------------------------------------

# Die Skripte sollen nur sehen, was der Test ihnen gibt.
unset STORAGE_CONNECTION_STRING STORAGE_MIGRATION_CONNECTION_STRING STORAGE_SCHEMA \
  FLOWZER_RUNTIME_ROLE FLOWZER_RUNTIME_PASSWORD FLOWZER_STORAGE_DIR FLOWZER_KEYRING_DIR \
  FLOWZER_BACKUP_DIR FLOWZER_APP_VERSION FLOWZER_IMAGE_TAG PGHOST PGPORT PGUSER PGDATABASE PGPASSWORD
export FLOWZER_PG_CLIENT=docker
export FLOWZER_PG_DOCKER_NETWORK="$NETWORK"
export FLOWZER_PG_IMAGE="$PG_IMAGE"

phase "API bauen"
if [[ -n "${FLOWZER_TEST_API_DLL:-}" ]]; then
  API_DLL="$FLOWZER_TEST_API_DLL"
else
  dotnet build "${REPO_ROOT}/src/WebApiEngine/WebApiEngine.csproj" --configuration Release --nologo \
    >"${LOG_DIR}/dotnet-build.log" 2>&1 || fail "dotnet build scheiterte (siehe dotnet-build.log)"
  API_DLL="$(find "${REPO_ROOT}/src/WebApiEngine/bin/Release" -path '*/net*/WebApiEngine.dll' | head -n 1)"
fi
[[ -f "$API_DLL" ]] || fail "WebApiEngine.dll nicht gefunden (${API_DLL:-leer})"
echo "API: ${API_DLL}"

phase "PostgreSQL starten (${PG_IMAGE})"
docker network create "$NETWORK" >/dev/null
docker run -d --name "$CONTAINER" --network "$NETWORK" --network-alias db \
  -p 127.0.0.1::5432 -e POSTGRES_PASSWORD="$SUPERUSER_PASSWORD" \
  -v "${REPO_ROOT}/deploy/postgresql:/flowzer-sql:ro" "$PG_IMAGE" >/dev/null
ready=0
for _ in $(seq 1 60); do
  # Ueber TCP pruefen: Waehrend der Initialisierung lauscht der temporaere Server nur lokal.
  if docker exec "$CONTAINER" pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1; then ready=1; break; fi
  sleep 1
done
[[ "$ready" -eq 1 ]] || fail "PostgreSQL wurde nicht bereit"
HOST_PORT="$(docker port "$CONTAINER" 5432/tcp | head -n 1)"
HOST_PORT="${HOST_PORT##*:}"
echo "PostgreSQL bereit, Hostport ${HOST_PORT}"

phase "Quelle und Ziel mit dem Rollenskript anlegen"
for database in "$SOURCE_DB" "$TARGET_DB"; do
  docker exec "$CONTAINER" psql --no-psqlrc -q -v ON_ERROR_STOP=1 -U postgres -d postgres \
    -v datenbank="$database" -v migrationsrolle="$MIGRATION_ROLE" -v laufzeitrolle="$RUNTIME_ROLE" \
    -v schema="$SCHEMA" -f /flowzer-sql/01-datenbank-und-rollen.sql >>"${LOG_DIR}/rollenskript.log" 2>&1 \
    || fail "01-datenbank-und-rollen.sql scheiterte fuer ${database} (siehe rollenskript.log)"
done
sql postgres "ALTER ROLE ${MIGRATION_ROLE} PASSWORD '${MIGRATION_PASSWORD}'" >/dev/null
sql postgres "ALTER ROLE ${RUNTIME_ROLE} PASSWORD '${RUNTIME_PASSWORD}'" >/dev/null

phase "Quelle migrieren (--migrate)"
Storage__Provider=PostgreSql Storage__PostgreSql__Schema="$SCHEMA" \
  Storage__PostgreSql__ConnectionString="Host=127.0.0.1;Port=${HOST_PORT};Database=${SOURCE_DB};Username=${MIGRATION_ROLE};Password=${MIGRATION_PASSWORD}" \
  dotnet "$API_DLL" --migrate >"${LOG_DIR}/migrate.log" 2>&1 || fail "--migrate scheiterte (siehe migrate.log)"
migration_files=("${REPO_ROOT}"/src/PostgreSqlStorageSystem/Migrations/[0-9]*.sql)
EXPECTED_COUNT="${#migration_files[@]}"
last_migration="${migration_files[${#migration_files[@]}-1]##*/}"
EXPECTED_MAX="$((10#${last_migration%%_*}))"
echo "Erwarteter Migrationsstand: ${EXPECTED_COUNT} Migrationen, hoechste Version ${EXPECTED_MAX}"

phase "Testdaten schreiben (als Laufzeitrolle) und Dateiablage anlegen"
sql_as "$RUNTIME_ROLE" "$RUNTIME_PASSWORD" "$SOURCE_DB" "
  INSERT INTO ${SCHEMA}.definitions (id, definition_id, is_active, version_major, version_minor, saved_on, body) VALUES
    ('11111111-1111-4111-8111-111111111111', 'sicherung-a', true, 1, 0, now(), '{\"name\":\"Sicherung A\"}'),
    ('22222222-2222-4222-8222-222222222222', 'sicherung-b', true, 1, 0, now(), '{\"name\":\"Sicherung B\"}');
  INSERT INTO ${SCHEMA}.workflow_folders (id, parent_id, name, body) VALUES
    ('33333333-3333-4333-8333-333333333333', NULL, 'Katalog Sicherung', '{\"name\":\"Katalog Sicherung\"}');" >/dev/null
SOURCE_FILES="${WORK_DIR}/quelle"
mkdir -p "${SOURCE_FILES}/ablage/FileStorage" "${SOURCE_FILES}/schluesselring"
echo '{"probe":"dateiablage"}' >"${SOURCE_FILES}/ablage/FileStorage/beispiel.json"
echo '<key id="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"/>' >"${SOURCE_FILES}/schluesselring/key-aaaaaaaa.xml"
# Unveraenderte Vergleichskopie; die Faelle unten veraendern die Quelldateien gezielt.
cp -R "$SOURCE_FILES" "${WORK_DIR}/quelle-original"
SOURCE_STATE="$(database_state "$SOURCE_DB")"
echo "Quelle: ${SOURCE_STATE}"

BACKUP_DIR="${WORK_DIR}/backups"

# --- Faelle ----------------------------------------------------------------------------------

# Testzweck: backup.sh legt Dump, Pruefsummen, Dateiarchiv und .meta ohne Zwischenstaende in einem
# nur fuer den Besitzer lesbaren Verzeichnis ab; die .meta nennt Herkunft, Versionen,
# Migrationsstand und die absoluten Quellpfade von Ablage und Keyring.
begin_case "backup.sh sichert Datenbank und Dateien mit Pruefsumme und .meta"
run_script backup "$SOURCE_DB" env FLOWZER_APP_VERSION="$APP_VERSION" \
  FLOWZER_STORAGE_DIR="${SOURCE_FILES}/ablage" FLOWZER_KEYRING_DIR="${SOURCE_FILES}/schluesselring" \
  "${REPO_ROOT}/scripts/runtime/backup.sh" --out "$BACKUP_DIR" || fail "backup.sh scheiterte"
dumps=("$BACKUP_DIR"/*.dump)
[[ "${#dumps[@]}" -eq 1 && -f "${dumps[0]}" ]] || fail "genau ein Dump erwartet"
DUMP="${dumps[0]}"
STAMP="${DUMP%.dump}"
META="${STAMP}.meta"
FILES_ARCHIVE="${STAMP}-files.tgz"
for expected_file in "$DUMP" "${DUMP}.sha256" "$META" "$FILES_ARCHIVE" "${FILES_ARCHIVE}.sha256"; do
  [[ -f "$expected_file" ]] || fail "${expected_file##*/} fehlt"
done
pass "Dump, .sha256, .meta, Dateiarchiv und dessen .sha256 vorhanden"
[[ -z "$(find "$BACKUP_DIR" -name '*.tmp')" ]] || fail "temporaere Dateien liegen geblieben"
pass "keine .tmp-Reste"
[[ -n "$(find "$BACKUP_DIR" -maxdepth 0 -perm 700)" ]] || fail "Sicherungsverzeichnis ist nicht 700"
[[ -z "$(find "$BACKUP_DIR" -type f ! -perm 600)" ]] || fail "Sicherungsdateien sind nicht 600"
pass "Verzeichnis 700, Dateien 600"
assert_eq "$(sha256_of "$DUMP")  ${DUMP##*/}" "$(cat "${DUMP}.sha256")" "Pruefsummenzeile des Dumps"
assert_eq "$(sha256_of "$FILES_ARCHIVE")" "$(cut -d' ' -f1 "${FILES_ARCHIVE}.sha256")" "Pruefsumme des Dateiarchivs"
assert_eq db "$(meta_value "$META" host)" ".meta host"
assert_eq "$SOURCE_DB" "$(meta_value "$META" database)" ".meta database"
assert_eq "$SCHEMA" "$(meta_value "$META" schema)" ".meta schema"
assert_eq "$APP_VERSION" "$(meta_value "$META" app_version)" ".meta app_version"
assert_eq "$EXPECTED_COUNT" "$(meta_value "$META" schema_migrations_count)" ".meta schema_migrations_count"
assert_eq "$EXPECTED_MAX" "$(meta_value "$META" schema_migrations_max)" ".meta schema_migrations_max"
server_version="$(meta_value "$META" postgres_server_version)"
[[ "$server_version" == 17.* ]] || fail ".meta postgres_server_version '${server_version}' ist nicht 17.x"
pass ".meta postgres_server_version = ${server_version}"
pg_dump_version="$(meta_value "$META" pg_dump_version)"
[[ "$pg_dump_version" == "pg_dump (PostgreSQL) 17."* ]] || fail ".meta pg_dump_version '${pg_dump_version}'"
pass ".meta pg_dump_version = ${pg_dump_version}"
assert_eq "${SOURCE_FILES}/ablage" "$(meta_value "$META" files_storage_dir)" ".meta files_storage_dir"
assert_eq "${SOURCE_FILES}/schluesselring" "$(meta_value "$META" files_keyring_dir)" ".meta files_keyring_dir"
tar -tzf "$FILES_ARCHIVE" >"${LOG_DIR}/files-archive.txt"
assert_contains "${LOG_DIR}/files-archive.txt" "ablage/FileStorage/beispiel.json" "Archiv enthaelt die Dateiablage relativ"
assert_contains "${LOG_DIR}/files-archive.txt" "schluesselring/key-aaaaaaaa.xml" "Archiv enthaelt den Keyring relativ"

# Testzweck: Scheitert pg_dump mitten in der Sicherung (hier: eine fremde Tabelle im Schema, die
# die Migrationsrolle nicht lesen darf), endet backup.sh mit Fehler und hinterlaesst weder einen
# halben Dump noch .tmp-, .sha256- oder .meta-Dateien.
begin_case "Abgebrochene Sicherung hinterlaesst keine Dateien"
sql "$SOURCE_DB" "CREATE TABLE ${SCHEMA}.fremd_ohne_recht (id integer); INSERT INTO ${SCHEMA}.fremd_ohne_recht VALUES (1)" >/dev/null
FAILED_BACKUP_DIR="${WORK_DIR}/backups-abgebrochen"
if run_script backup-abgebrochen "$SOURCE_DB" "${REPO_ROOT}/scripts/runtime/backup.sh" --no-files --out "$FAILED_BACKUP_DIR"; then
  fail "backup.sh meldete Erfolg trotz unlesbarer Tabelle"
fi
sql "$SOURCE_DB" "DROP TABLE ${SCHEMA}.fremd_ohne_recht" >/dev/null
assert_contains "${LOG_DIR}/backup-abgebrochen.log" "permission denied" "pg_dump-Fehler sichtbar"
assert_eq "" "$(find "$FAILED_BACKUP_DIR" -type f)" "keine Dateien im Sicherungsverzeichnis"

# Testzweck: restore.sh spielt die Sicherung ohne --force in eine mit dem Rollenskript vorbereitete
# Datenbank ein (vorab angelegte, leere schema_migrations), setzt ueber 02 die Laufzeitrechte -
# schema_migrations nur lesend - und stellt die Dateien unter --files-root wieder her.
begin_case "Restore ohne --force in das vorbereitete Ziel"
assert_eq "0" "$(sql "$TARGET_DB" "SELECT count(*) FROM ${SCHEMA}.schema_migrations")" "Ziel hat vorab eine leere schema_migrations"
run_script restore-ziel "$TARGET_DB" env FLOWZER_RUNTIME_PASSWORD="$RUNTIME_PASSWORD" \
  "${REPO_ROOT}/scripts/runtime/restore.sh" "$DUMP" --runtime-role "$RUNTIME_ROLE" \
  --files "$FILES_ARCHIVE" --files-root "${WORK_DIR}/ziel-dateien" || fail "restore.sh scheiterte"
assert_contains "${LOG_DIR}/restore-ziel.log" "leer bis auf eine leere schema_migrations" "leere Historie als leer erkannt"
assert_contains "${LOG_DIR}/restore-ziel.log" "Lesen als ${RUNTIME_ROLE} ueber eigene Verbindung bestaetigt" "Restore liest als Laufzeitrolle"
target_state="$(database_state "$TARGET_DB")"
assert_eq "${SOURCE_STATE#*/}" "${target_state#*/}" "Bestand im Ziel (Definitionen/Ordner/Migrationen)"
assert_eq "$EXPECTED_RIGHTS" "$(runtime_rights)" "Laufzeitrechte im Ziel"
assert_eq "2" "$(sql_as "$RUNTIME_ROLE" "$RUNTIME_PASSWORD" "$TARGET_DB" "SELECT count(*) FROM ${SCHEMA}.definitions")" "Laufzeitrolle liest die Definitionen"
diff -r "${SOURCE_FILES}/ablage" "${WORK_DIR}/ziel-dateien/ablage" >/dev/null || fail "Dateiablage weicht ab"
diff -r "${SOURCE_FILES}/schluesselring" "${WORK_DIR}/ziel-dateien/schluesselring" >/dev/null || fail "Keyring weicht ab"
pass "Dateiablage und Keyring unter --files-root identisch"

# Testzweck: Ein Restore in die Datenbank, aus der die Sicherung stammt, wird verweigert - ohne
# Schalter und auch mit --force -, und die Quelle bleibt unveraendert.
begin_case "Restore in die Quelle wird verweigert"
if run_script restore-quelle "$SOURCE_DB" "${REPO_ROOT}/scripts/runtime/restore.sh" "$DUMP"; then
  fail "Restore in die Quelle lief durch"
fi
assert_contains "${LOG_DIR}/restore-quelle.log" "ist die Quelle dieser Sicherung" "Begruendung genannt"
if run_script restore-quelle-force "$SOURCE_DB" "${REPO_ROOT}/scripts/runtime/restore.sh" "$DUMP" --force; then
  fail "Restore mit --force in die Quelle lief durch"
fi
assert_contains "${LOG_DIR}/restore-quelle-force.log" "ist die Quelle dieser Sicherung" "Begruendung auch mit --force"
assert_eq "$SOURCE_STATE" "$(database_state "$SOURCE_DB")" "Quelle unveraendert (Schema-OID/Bestand/Migrationen)"

# Testzweck: Eine Sicherung, deren Pruefsumme nicht passt, wird vor jedem Zugriff auf das Ziel
# abgewiesen - auch mit --force; das Ziel bleibt unveraendert.
begin_case "Manipulierte Pruefsumme wird abgewiesen"
TAMPERED="${WORK_DIR}/manipuliert"
mkdir -p "$TAMPERED"
cp "$DUMP" "$META" "$TAMPERED/"
printf '%064d  %s\n' 0 "${DUMP##*/}" >"${TAMPERED}/${DUMP##*/}.sha256"
state_before="$(database_state "$TARGET_DB")"
if run_script restore-manipuliert "$TARGET_DB" "${REPO_ROOT}/scripts/runtime/restore.sh" \
    "${TAMPERED}/${DUMP##*/}" --force --runtime-role "$RUNTIME_ROLE"; then
  fail "Restore mit falscher Pruefsumme lief durch"
fi
assert_contains "${LOG_DIR}/restore-manipuliert.log" "weicht von" "Abweichung gemeldet"
assert_eq "$state_before" "$(database_state "$TARGET_DB")" "Ziel unveraendert"

# Testzweck: Fehlt die Pruefsummendatei, bricht restore.sh mit --require-checksum ab, ohne das
# Ziel anzufassen.
begin_case "Fehlende Pruefsumme mit --require-checksum"
rm -f "${TAMPERED}/${DUMP##*/}.sha256"
if run_script restore-ohne-pruefsumme "$TARGET_DB" "${REPO_ROOT}/scripts/runtime/restore.sh" \
    "${TAMPERED}/${DUMP##*/}" --force --require-checksum --runtime-role "$RUNTIME_ROLE"; then
  fail "Restore ohne Pruefsumme lief trotz --require-checksum durch"
fi
assert_contains "${LOG_DIR}/restore-ohne-pruefsumme.log" "--require-checksum" "Abbruch begruendet"
assert_eq "$state_before" "$(database_state "$TARGET_DB")" "Ziel unveraendert"

# Testzweck: Fehlt die .meta, laesst sich die Herkunft nicht pruefen; --force wird dann ohne
# --allow-same-database verweigert, bevor das Ziel angefasst wird.
begin_case "Fehlende .meta: --force nur mit --allow-same-database"
rm -f "${TAMPERED}/${META##*/}"
if run_script restore-ohne-meta "$TARGET_DB" "${REPO_ROOT}/scripts/runtime/restore.sh" \
    "${TAMPERED}/${DUMP##*/}" --force --runtime-role "$RUNTIME_ROLE"; then
  fail "Restore mit --force ohne .meta lief durch"
fi
assert_contains "${LOG_DIR}/restore-ohne-meta.log" "--force nur zusammen mit --allow-same-database" "Abbruch begruendet"
assert_eq "$state_before" "$(database_state "$TARGET_DB")" "Ziel unveraendert"

# Testzweck: Wuerde --files vorhandene Dateien ersetzen - auf demselben Host die der
# Quellinstallation -, bricht restore.sh ohne --overwrite-files ab, bevor Datenbank oder Dateien
# angefasst werden, auch mit --force.
begin_case "Vorhandene Dateien werden nicht ohne --overwrite-files ersetzt"
echo 'lokal geaendert' >"${SOURCE_FILES}/schluesselring/key-aaaaaaaa.xml"
if run_script restore-dateikonflikt "$TARGET_DB" "${REPO_ROOT}/scripts/runtime/restore.sh" "$DUMP" \
    --force --runtime-role "$RUNTIME_ROLE" --files "$FILES_ARCHIVE"; then
  fail "Restore ersetzte vorhandene Dateien ohne --overwrite-files"
fi
assert_contains "${LOG_DIR}/restore-dateikonflikt.log" "--overwrite-files" "Abbruch nennt den Schalter"
assert_eq "lokal geaendert" "$(cat "${SOURCE_FILES}/schluesselring/key-aaaaaaaa.xml")" "Quelldatei unveraendert"
assert_eq "$state_before" "$(database_state "$TARGET_DB")" "Ziel unveraendert"

# Testzweck: --force verwirft das belegte Zielschema, spielt die Sicherung ein und stellt ueber 02
# Schema-USAGE, Default-Privileges und Tabellenrechte wieder her (Pruefung per has_*_privilege,
# ohne Passwort der Laufzeitrolle); mit --overwrite-files landen die Dateien an den absoluten
# Pfaden aus der .meta, fehlende werden neu angelegt.
begin_case "--force ins Ziel stellt Bestand, Rechte und Dateien wieder her"
rm -rf "${SOURCE_FILES}/ablage"
run_script restore-ziel-force "$TARGET_DB" "${REPO_ROOT}/scripts/runtime/restore.sh" "$DUMP" \
  --force --runtime-role "$RUNTIME_ROLE" --files "$FILES_ARCHIVE" --overwrite-files || fail "restore.sh --force scheiterte"
assert_contains "${LOG_DIR}/restore-ziel-force.log" "--overwrite-files: 1 vorhandene Datei(en) werden ersetzt" "Ueberschreiben angekuendigt"
state_after="$(database_state "$TARGET_DB")"
[[ "${state_after%%/*}" != "${state_before%%/*}" ]] || fail "Schema wurde nicht neu angelegt"
pass "Zielschema neu angelegt (OID ${state_before%%/*} -> ${state_after%%/*})"
assert_eq "${SOURCE_STATE#*/}" "${state_after#*/}" "Bestand im Ziel"
assert_eq "$EXPECTED_RIGHTS" "$(runtime_rights)" "Laufzeitrechte nach --force"
diff -r "${WORK_DIR}/quelle-original" "$SOURCE_FILES" >/dev/null || fail "Dateien an den .meta-Pfaden weichen ab"
pass "Dateiablage (neu angelegt) und Keyring (ersetzt) an den absoluten .meta-Pfaden identisch"

# Testzweck: Ohne bekannte Laufzeitrolle warnt restore.sh nach --force deutlich und nennt den
# Befehl zum Nachholen; ohne 02 fehlt der Laufzeit USAGE auf dem Schema, nach 02 sind die Rechte
# vollstaendig, und ein zweiter Lauf von 02 aendert nichts (idempotent).
begin_case "--force ohne Laufzeitrolle: Warnung, 02 von Hand nachholen"
run_script restore-ziel-ohne-rolle "$TARGET_DB" "${REPO_ROOT}/scripts/runtime/restore.sh" "$DUMP" --force \
  || fail "restore.sh --force ohne Laufzeitrolle scheiterte"
assert_contains "${LOG_DIR}/restore-ziel-ohne-rolle.log" "Laufzeitrolle unbekannt" "Warnung erscheint"
assert_contains "${LOG_DIR}/restore-ziel-ohne-rolle.log" "-v laufzeitrolle=${RUNTIME_ROLE} -v schema=${SCHEMA} -f deploy/postgresql/02-laufzeitrechte.sql" "Befehl zum Nachholen genannt"
assert_eq "false" "$(sql "$TARGET_DB" "SELECT has_schema_privilege('${RUNTIME_ROLE}', '${SCHEMA}', 'USAGE')::text")" "ohne 02 kein USAGE"
for attempt in 1 2; do
  docker exec -e PGPASSWORD="$MIGRATION_PASSWORD" "$CONTAINER" psql --no-psqlrc -q -v ON_ERROR_STOP=1 \
    -h 127.0.0.1 -U "$MIGRATION_ROLE" -d "$TARGET_DB" \
    -v migrationsrolle="$MIGRATION_ROLE" -v laufzeitrolle="$RUNTIME_ROLE" -v schema="$SCHEMA" \
    -f /flowzer-sql/02-laufzeitrechte.sql >>"${LOG_DIR}/laufzeitrechte-manuell.log" 2>&1 \
    || fail "02-laufzeitrechte.sql scheiterte (Lauf ${attempt})"
  assert_eq "$EXPECTED_RIGHTS" "$(runtime_rights)" "Laufzeitrechte nach 02 (Lauf ${attempt})"
done

# Testzweck: Die API bestaetigt nach dem Restore mit --check-config gegen das Ziel, dass die
# Migrationen aktuell sind und die Ablage mit der Laufzeitkennung erreichbar ist.
begin_case "--check-config gegen das wiederhergestellte Ziel"
check_status=0
Storage__Provider=PostgreSql Storage__PostgreSql__Schema="$SCHEMA" \
  Storage__PostgreSql__ConnectionString="Host=127.0.0.1;Port=${HOST_PORT};Database=${TARGET_DB};Username=${RUNTIME_ROLE};Password=${RUNTIME_PASSWORD}" \
  Storage__PostgreSql__MigrationConnectionString="Host=127.0.0.1;Port=${HOST_PORT};Database=${TARGET_DB};Username=${MIGRATION_ROLE};Password=${MIGRATION_PASSWORD}" \
  Authentication__Scheme=None ServiceTaskWebhooks__Enabled=false \
  dotnet "$API_DLL" --check-config >"${LOG_DIR}/check-config.log" 2>&1 || check_status=$?
echo "    check-config: Exit ${check_status} (Ausgabe check-config.log)"
# Exit 2 heisst "nur Warnungen"; erwartet ist hier genau die zu Authentication:Scheme=None.
[[ "$check_status" -eq 0 || "$check_status" -eq 2 ]] || fail "--check-config meldet Fehler (Exit ${check_status})"
grep -Eq '^Migrationen +OK +aktuell' "${LOG_DIR}/check-config.log" || fail "Migrationen nicht als aktuell gemeldet"
pass "Migrationen OK, aktuell"
grep -Eq '^Ablage +OK +PostgreSQL erreichbar' "${LOG_DIR}/check-config.log" || fail "Ablage nicht OK"
pass "Ablage OK mit der Laufzeitkennung"
if grep -Eq '^[[:alpha:]-]+ +Fehler ' "${LOG_DIR}/check-config.log"; then
  fail "--check-config enthaelt eine Fehlerzeile"
fi
pass "keine Fehlerzeile"

# Testzweck: Keine Skriptausgabe dieses Laufs enthaelt ein Datenbankpasswort.
begin_case "Keine Passwoerter in den Ausgaben"
if grep -lF -e "$RUNTIME_PASSWORD" -e "$MIGRATION_PASSWORD" -e "$SUPERUSER_PASSWORD" "${LOG_DIR}"/*.log; then
  fail "ein Testpasswort steht in einer Skriptausgabe (Dateien oben)"
fi
pass "keine Passwoerter in $(find "$LOG_DIR" -name '*.log' | wc -l | tr -d ' ') Ausgabedateien"
current_case=''
