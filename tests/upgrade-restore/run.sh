#!/usr/bin/env bash
# Upgrade-/Restore-Rig (R2b): weist mit echten Images nach, dass eine Aktualisierung laufende
# Instanzen stehen laesst, dass eine gescheiterte Migration nichts veraendert und dass eine
# Sicherung in eine zweite Datenbank mit echter API weiterlaeuft.
#
#   tests/upgrade-restore/run.sh                  alle Faelle, aktuelles Image aus GHCR
#   tests/upgrade-restore/run.sh --build-current  aktuelles Image aus diesem Repository bauen
#   tests/upgrade-restore/run.sh --cases 1,4      nur ausgewaehlte Faelle (4 bringt 1 mit)
#   tests/upgrade-restore/run.sh --keep           Container und Arbeitsverzeichnis stehen lassen
#
# Faelle:
#   1  Image-Wechsel ohne Migration: Vorgaengerimage -> aktuelles Image mit drei wartenden
#      Instanzen (Aufgabe, Auftrag, Timer)
#   2  Schemasprung 016 -> aktuell ueber den Klartext-Fixture fixtures/schema-016
#   3  Negative Migration: Kollision vor migrate, Rueckweg mit dem Image des Stands 016
#   4  backup.sh aus Fall 1, restore.sh in eine zweite Datenbank, API dagegen; dazu 4b:
#      Sicherung des Stands 016, Restore, Migration mit dem aktuellen Image
#
# Umgebungsvariablen:
#   FLOWZER_IMAGE_TAG                    Tag des aktuellen Images in GHCR (Standard latest)
#   FLOWZER_UPGRADE_CURRENT_IMAGE        aktuelles Image vollstaendig (ueberschreibt den Tag)
#   FLOWZER_UPGRADE_PREVIOUS_IMAGE       Vorgaengerimage (Standard ...:sha-5a2d9b38eec4, #344)
#   FLOWZER_UPGRADE_LEGACY_IMAGE         Image des Stands 016 (Standard: aus 92d8557 gebaut)
#   FLOWZER_TEST_PG_IMAGE                PostgreSQL-Image (Standard postgres:17-alpine)
#   FLOWZER_UPGRADE_RESTORE_LOG_DIR      Ablage der Ausgaben (Standard tests/upgrade-restore/logs)
#
# Voraussetzungen: docker (mit Compose v2), jq, curl, git, tar, perl. Alle Passwoerter entstehen
# je Lauf zufaellig und erscheinen in keiner Ausgabe.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
KEEP=0
BUILD_CURRENT=0
CASES='1,2,3,4'

usage() {
  sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --keep) KEEP=1; shift ;;
    --build-current) BUILD_CURRENT=1; shift ;;
    --cases) CASES="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unbekannter Schalter: $1" >&2; usage >&2; exit 64 ;;
  esac
done

case_selected() {
  [[ ",${CASES}," == *",$1,"* ]]
}
[[ "$CASES" =~ ^[1-4](,[1-4])*$ ]] || { echo "--cases erwartet eine Liste aus 1 bis 4, etwa 1,4" >&2; exit 64; }
# Fall 4 sichert den Bestand aus Fall 1.
if case_selected 4 && ! case_selected 1; then
  CASES="1,${CASES}"
fi

UR_PROJECT="flowzer-upgrade-restore-$$-${RANDOM}"
UR_WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/flowzer-upgrade-restore.XXXXXX")"
# Normalisiert (TMPDIR endet unter macOS auf /); backup.sh schreibt absolute Pfade in die .meta.
UR_WORK_DIR="$(cd "$UR_WORK_DIR" && pwd)"
if [[ -n "${FLOWZER_UPGRADE_RESTORE_LOG_DIR:-}" ]]; then
  UR_LOG_DIR="$FLOWZER_UPGRADE_RESTORE_LOG_DIR"
  mkdir -p "$UR_LOG_DIR"
else
  UR_LOG_DIR="${SCRIPT_DIR}/logs"
  rm -rf "$UR_LOG_DIR"
  mkdir -p "$UR_LOG_DIR"
fi
UR_LOG_DIR="$(cd "$UR_LOG_DIR" && pwd)"

# shellcheck source=tests/upgrade-restore/lib.sh
source "${SCRIPT_DIR}/lib.sh"

ur_require_tools docker jq curl git tar perl od sed seq
docker compose version >/dev/null 2>&1 || { echo "Voraussetzung fehlt: docker compose (v2)" >&2; exit 69; }

PG_IMAGE="${FLOWZER_TEST_PG_IMAGE:-postgres:17-alpine}"
export FLOWZER_TEST_PG_IMAGE="$PG_IMAGE"
PREVIOUS_IMAGE="${FLOWZER_UPGRADE_PREVIOUS_IMAGE:-ghcr.io/flowzer-io/flowzer-api:sha-5a2d9b38eec4}"
if [[ "$BUILD_CURRENT" -eq 1 ]]; then
  CURRENT_IMAGE=flowzer-upgrade-restore/api:current
else
  CURRENT_IMAGE="${FLOWZER_UPGRADE_CURRENT_IMAGE:-ghcr.io/flowzer-io/flowzer-api:${FLOWZER_IMAGE_TAG:-latest}}"
fi
LEGACY_IMAGE="${FLOWZER_UPGRADE_LEGACY_IMAGE:-$UR_LEGACY_IMAGE_DEFAULT}"

finish() {
  local status=$?
  # Ein weiteres Signal darf Logsammeln und Aufraeumen nicht mehr unterbrechen.
  trap '' INT TERM
  ur_kill_child
  if [[ -n "${FLOWZER_UPGRADE_API_IMAGE:-}" ]]; then
    ur_compose ps -a >"${UR_LOG_DIR}/compose-ps.txt" 2>&1 || true
    ur_compose logs --no-color --timestamps >"${UR_LOG_DIR}/compose.log" 2>&1 || true
  fi
  if [[ "$status" -ne 0 && -d "${UR_WORK_DIR}/backups" ]]; then
    # Fuer die Diagnose Dateiliste, Pruefsummen und .meta - nie die Dumps selbst.
    ls -la "${UR_WORK_DIR}/backups" >"${UR_LOG_DIR}/backups-listing.txt" 2>&1 || true
    cp "${UR_WORK_DIR}"/backups/*.meta "${UR_WORK_DIR}"/backups/*.sha256 "$UR_LOG_DIR"/ 2>/dev/null || true
  fi
  ur_redact_logs || true

  if [[ "$KEEP" -eq 1 ]]; then
    echo "Stehen gelassen (--keep): Compose-Projekt ${UR_PROJECT}, Arbeitsverzeichnis ${UR_WORK_DIR}"
    echo "Aufraeumen: docker compose -p ${UR_PROJECT} -f ${UR_COMPOSE_FILE} down -v --remove-orphans"
  else
    if [[ -n "${FLOWZER_UPGRADE_API_IMAGE:-}" ]]; then
      ur_compose down -v --remove-orphans >/dev/null 2>&1 || true
    fi
    rm -rf "$UR_WORK_DIR"
  fi

  local elapsed
  elapsed="$(ur_elapsed)"
  if [[ "$status" -eq 0 ]]; then
    printf '\nUpgrade-/Restore-Rig bestanden: %s Faelle, %s Pruefungen, %s Befund(e) (%ss). Logs: %s\n' \
      "$ur_case_count" "$ur_check_count" "$ur_finding_count" "$elapsed" "$UR_LOG_DIR" | tee -a "${UR_LOG_DIR}/ergebnis.txt"
  else
    printf '\nUpgrade-/Restore-Rig fehlgeschlagen (Exit %s, %ss). Logs: %s\n' "$status" "$elapsed" "$UR_LOG_DIR" >&2
    printf 'Fehlgeschlagen (Exit %s, %ss)\n' "$status" "$elapsed" >>"${UR_LOG_DIR}/ergebnis.txt" 2>/dev/null || true
  fi
  exit "$status"
}
trap finish EXIT

# Bei Abbruch regulaer beenden, damit die EXIT-Falle aufraeumt; 130/143 nach Signal-Konvention.
on_signal() {
  ur_kill_child
  exit "$1"
}
trap 'on_signal 130' INT
trap 'on_signal 143' TERM

# --- Vorbereitung --------------------------------------------------------------------------

ur_phase "Images bereitstellen"
if [[ "$BUILD_CURRENT" -eq 1 ]]; then
  echo "Baue ${CURRENT_IMAGE} aus dem Repository (Dockerfile.api) ..."
  ur_run_child docker build --label "org.opencontainers.image.revision=$(git -C "$UR_REPO_ROOT" rev-parse HEAD 2>/dev/null || echo unbekannt)" \
    -f "${UR_REPO_ROOT}/Dockerfile.api" -t "$CURRENT_IMAGE" "$UR_REPO_ROOT" \
    >"${UR_LOG_DIR}/build-current.log" 2>&1 || ur_fail "Bau des aktuellen Images gescheitert (siehe build-current.log)"
fi
ur_ensure_image "$CURRENT_IMAGE" || ur_fail "aktuelles Image fehlt"
ur_ensure_image "$PG_IMAGE" || ur_fail "PostgreSQL-Image fehlt"
if case_selected 1; then
  ur_ensure_image "$PREVIOUS_IMAGE" || ur_fail "Vorgaengerimage fehlt"
fi
if case_selected 3; then
  ur_ensure_legacy_image "$LEGACY_IMAGE" || ur_fail "Image des Stands 016 fehlt und liess sich nicht bauen"
fi
{
  echo "Datum:           $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "Host:            $(uname -sm), Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null), $(docker compose version --short 2>/dev/null)"
  echo "Repository:      $(git -C "$UR_REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo '?')"
  echo "Faelle:          ${CASES}"
  echo "PostgreSQL:      ${PG_IMAGE}"
  echo "Aktuell:         ${CURRENT_IMAGE} ($(ur_image_describe "$CURRENT_IMAGE"))"
  if case_selected 1; then echo "Vorgaenger:      ${PREVIOUS_IMAGE} ($(ur_image_describe "$PREVIOUS_IMAGE"))"; fi
  if case_selected 3; then echo "Stand 016:       ${LEGACY_IMAGE} ($(ur_image_describe "$LEGACY_IMAGE"))"; fi
} | tee "${UR_LOG_DIR}/umgebung.txt"

ur_phase "PostgreSQL starten und Rollen anlegen"
ur_init_secrets
ur_use flowzer "$CURRENT_IMAGE" false
ur_start_db || ur_fail "PostgreSQL startet nicht"
ur_info "Server: $(ur_sql postgres 'SHOW server_version')"
# Der erste Lauf des Rollenskripts legt auch die Rollen an; danach die Passwoerter.
ur_prepare_database flowzer
ur_set_role_passwords
migration_files=("${UR_REPO_ROOT}"/src/PostgreSqlStorageSystem/Migrations/[0-9]*.sql)
EXPECTED_MAX="${migration_files[${#migration_files[@]}-1]##*/}"
EXPECTED_MAX="$((10#${EXPECTED_MAX%%_*}))"
ur_info "Hoechste Migration im Repository: ${EXPECTED_MAX}"

# --- Fall 1 --------------------------------------------------------------------------------

if case_selected 1; then
  # Testzweck: Der reale Produktionsfall seit #344 - ein Image-Wechsel ohne neue Migration. Drei
  # Instanzen warten mit dem Vorgaengerimage (Aufgabe, Auftrag, Timer); nach `api` stoppen,
  # `migrate` und `api` mit dem aktuellen Image wendet migrate nichts an, die Bereitschaft meldet
  # UpToDate, der gespeicherte Laufzeitzustand ist unveraendert, und alle drei Instanzen lassen
  # sich mit der neuen Version abschliessen.
  ur_begin_case "Fall 1: Image-Wechsel ohne Migration (Vorgaenger -> aktuell, laufende Instanzen)"
  ur_deploy fall1-vorgaenger flowzer "$PREVIOUS_IMAGE" false \
    || ur_fail "Start mit dem Vorgaengerimage gescheitert (Compose ${UR_COMPOSE_STATUS}, migrate ${UR_MIGRATE_EXIT}; siehe compose-fall1-vorgaenger.log)"
  ur_assert_eq 0 "$UR_MIGRATE_EXIT" "Vorgaenger: migrate Exit"
  previous_applied="$(ur_migrate_applied_count "$(ur_migrate_log_file fall1-vorgaenger)")"
  ur_info "Vorgaenger: migrate wendete ${previous_applied:-?} Migration(en) an"
  state_previous="$(ur_migration_state flowzer)"
  ur_assert_eq "Healthy/Ready/UpToDate/0/${state_previous#*/}" "$(ur_readiness)" "Vorgaenger: /health/ready"

  ur_setup_definitions
  ur_start_instances
  ur_expect_waiting "Vorgaenger"
  ur_runtime_fingerprint flowzer >"${UR_LOG_DIR}/fall1-zustand-vorher.txt"
  ur_info "Laufzeitzustand vorher: $(wc -l <"${UR_LOG_DIR}/fall1-zustand-vorher.txt" | tr -d ' ') Zeilen (fall1-zustand-vorher.txt)"

  ur_stop_api
  ur_deploy fall1-aktuell flowzer "$CURRENT_IMAGE" false \
    || ur_fail "Aktualisierung gescheitert (Compose ${UR_COMPOSE_STATUS}, migrate ${UR_MIGRATE_EXIT}; siehe compose-fall1-aktuell.log)"
  migrate_log="$(ur_migrate_log_file fall1-aktuell)"
  ur_assert_eq 0 "$UR_MIGRATE_EXIT" "Aktuell: migrate Exit"
  state_current="$(ur_migration_state flowzer)"
  applied="$(ur_migrate_applied_count "$migrate_log")"
  # Ohne neue Migration im aktuellen Image ist die Differenz 0 - der Produktionsfall seit #344.
  ur_assert_eq "$((${state_current%%/*} - ${state_previous%%/*}))" "${applied:-leer}" "Aktuell: migrate meldet angewendete Migrationen (Differenz ${state_previous} -> ${state_current})"
  if [[ "$applied" == 0 ]]; then
    ur_pass "Aktuell: migrate wendet nichts an (Image-Wechsel ohne Schemaaenderung)"
  else
    ur_info "Hinweis: Das aktuelle Image bringt ${applied} neue Migration(en) mit; der Fall prueft dann den Sprung."
  fi
  ur_assert_eq 0 "$(ur_migrate_form_bindings "$migrate_log")" "Aktuell: Formularbindungs-Upgrade lief, ergaenzte Bindungen"
  ur_assert_eq "Healthy/Ready/UpToDate/0/${state_current#*/}" "$(ur_readiness)" "Aktuell: /health/ready"
  ur_runtime_fingerprint flowzer >"${UR_LOG_DIR}/fall1-zustand-nachher.txt"
  diff "${UR_LOG_DIR}/fall1-zustand-vorher.txt" "${UR_LOG_DIR}/fall1-zustand-nachher.txt" >"${UR_LOG_DIR}/fall1-zustand-diff.txt" \
    || ur_fail "Laufzeitzustand hat sich durch den Image-Wechsel veraendert (fall1-zustand-diff.txt)"
  ur_pass "Laufzeitzustand (Instanzen, Aufgabe, Auftrag, Timer, Deployments samt Formularbindung, Formular; je mit Datensatz-Pruefsumme) unveraendert"
  ur_expect_waiting "Aktuell"
  ur_complete_instances "Aktuell"
fi

# --- Fall 2 --------------------------------------------------------------------------------

if case_selected 2; then
  # Testzweck: Der Schemasprung der letzten Produktionsaktualisierung mit Migrationen (016 ->
  # aktuell). Der Klartext-Fixture traegt drei wartende Instanzen des Stands 016; migrate mit dem
  # aktuellen Image wendet genau 017 bis zur hoechsten Version an, das Formularbindungs-Upgrade
  # laeuft, der Laufzeitzustand bleibt unveraendert und die Instanzen lassen sich abschliessen.
  ur_begin_case "Fall 2: Schemasprung 016 -> aktuell (Fixture)"
  [[ -f "${SCRIPT_DIR}/${UR_FIXTURE_016}" ]] || ur_fail "Fixture ${UR_FIXTURE_016} fehlt (erzeugen mit fixtures/make-schema-016.sh)"
  ur_prepare_database flowzer_stand016
  ur_load_fixture flowzer_stand016 "$UR_FIXTURE_016"
  ur_assert_eq "16/16" "$(ur_migration_state flowzer_stand016)" "Fixture: Migrationsstand"
  ur_read_instance_ids_from_db flowzer_stand016
  ur_info "Instanzen aus dem Fixture: Aufgabe ${UR_REVIEW_ID}, Auftrag ${UR_SERVICE_ID}, Timer ${UR_TIMER_ID}"
  ur_runtime_fingerprint flowzer_stand016 >"${UR_LOG_DIR}/fall2-zustand-vorher.txt"

  ur_deploy fall2-aktuell flowzer_stand016 "$CURRENT_IMAGE" false \
    || ur_fail "Migration 016 -> aktuell gescheitert (Compose ${UR_COMPOSE_STATUS}, migrate ${UR_MIGRATE_EXIT}; siehe fall2-aktuell-migrate.log)"
  migrate_log="$(ur_migrate_log_file fall2-aktuell)"
  ur_assert_eq 0 "$UR_MIGRATE_EXIT" "migrate Exit"
  state_after="$(ur_migration_state flowzer_stand016)"
  # paste statt `seq -s`: BSD-seq haengt das Trennzeichen auch ans Ende.
  expected_list="$(seq 17 "${state_after#*/}" | paste -sd, - | sed 's/,/, /g')"
  ur_assert_eq "$((${state_after#*/} - 16))" "$(ur_migrate_applied_count "$migrate_log")" "migrate meldet angewendete Migrationen"
  ur_assert_eq "$expected_list" "$(ur_migrate_applied_list "$migrate_log")" "angewendete Versionen"
  ur_assert_eq "${state_after#*/}/${state_after#*/}" "$state_after" "Historie danach lueckenlos (Anzahl/hoechste Version)"
  ur_assert_eq 0 "$(ur_migrate_form_bindings "$migrate_log")" "Formularbindungs-Upgrade lief, ergaenzte Bindungen (016 bindet bereits beim Deployment)"
  ur_assert_eq "Healthy/Ready/UpToDate/0/${state_after#*/}" "$(ur_readiness)" "/health/ready"
  ur_runtime_fingerprint flowzer_stand016 >"${UR_LOG_DIR}/fall2-zustand-nachher.txt"
  diff "${UR_LOG_DIR}/fall2-zustand-vorher.txt" "${UR_LOG_DIR}/fall2-zustand-nachher.txt" >"${UR_LOG_DIR}/fall2-zustand-diff.txt" \
    || ur_fail "Laufzeitzustand hat sich durch die Migration veraendert (fall2-zustand-diff.txt)"
  ur_pass "Laufzeitzustand des Stands 016 nach der Migration unveraendert"
  ur_expect_waiting "Nach 016 -> ${state_after#*/}"
  ur_complete_instances "Nach 016 -> ${state_after#*/}"
fi

# --- Fall 3 --------------------------------------------------------------------------------

if case_selected 3; then
  # Testzweck: Eine Migration, die an einer vorab angelegten Tabelle mit abweichender Struktur
  # scheitert, laesst die Datenbank unveraendert (eine Transaktion je Lauf): migrate endet von
  # selbst mit Exit 1 und nennt Ausnahmetyp, SQLSTATE und Migration (#366), schema_migrations
  # und alle Relationen bleiben wie vorher, die API des neuen Images startet nicht
  # (service_completed_successfully), und das Image des bisherigen Stands laeuft danach ohne
  # Restore weiter und schliesst die wartenden Instanzen ab.
  ur_begin_case "Fall 3: Negative Migration und Rueckweg ohne Restore"
  ur_prepare_database flowzer_kollision
  ur_load_fixture flowzer_kollision "$UR_FIXTURE_016"
  ur_read_instance_ids_from_db flowzer_kollision
  ur_deploy fall3-stand016 flowzer_kollision "$LEGACY_IMAGE" false \
    || ur_fail "Start mit dem Image des Stands 016 gescheitert (siehe compose-fall3-stand016.log)"
  ur_assert_eq 0 "$(ur_migrate_applied_count "$(ur_migrate_log_file fall3-stand016)")" "Stand 016: migrate wendet nichts an"
  ur_expect_waiting "Stand 016 laeuft"
  ur_stop_api

  # Kollision: 019 legt inbound_triggers mit IF NOT EXISTS an und baut danach einen Unique-Index
  # ueber trigger_key. Eine gleichnamige Tabelle ohne diese Spalte laesst genau diesen Schritt
  # scheitern - nachdem 017 und 018 in derselben Transaktion bereits gelaufen sind.
  ur_psql_migration flowzer_kollision -q \
    -c "CREATE TABLE ${UR_SCHEMA}.inbound_triggers (id uuid PRIMARY KEY, body text NOT NULL)" \
    >>"${UR_LOG_DIR}/fall3-kollision.log" 2>&1 || ur_fail "Kollisionstabelle liess sich nicht anlegen"
  ur_pass "Kollision angelegt: ${UR_SCHEMA}.inbound_triggers ohne Spalte trigger_key"
  versions_before="$(ur_migration_versions flowzer_kollision)"
  ur_schema_fingerprint flowzer_kollision >"${UR_LOG_DIR}/fall3-schema-vorher.txt"
  ur_runtime_fingerprint flowzer_kollision >"${UR_LOG_DIR}/fall3-zustand-vorher.txt"

  if ur_deploy fall3-aktuell flowzer_kollision "$CURRENT_IMAGE" false; then
    ur_fail "Compose meldete Erfolg trotz Kollision"
  fi
  ur_pass "docker compose up endet mit Fehler (Exit ${UR_COMPOSE_STATUS})"
  migrate_log="$(ur_migrate_log_file fall3-aktuell)"
  # Rueckfall zu #366: migrate haengt nach dem Fehler (der Watchdog beendete es) oder endet
  # mit einem anderen Code als 1, etwa 134 (abort unter init) oder 139 (abort als PID 1).
  [[ "$UR_MIGRATE_HUNG" -eq 0 ]] \
    || ur_fail "migrate endete nach dem Fehlschlag nicht von selbst ($(uname -m)); der Rig beendete den Prozess nach 30 s (Exit ${UR_MIGRATE_EXIT}). Rueckfall zu #366, siehe docs/acceptance/upgrade-restore.md."
  ur_assert_eq 1 "$UR_MIGRATE_EXIT" "migrate endet von selbst mit Exit"
  if grep -q 'Unhandled exception' "$migrate_log"; then
    ur_fail "migrate meldet eine unbehandelte Ausnahme statt einer Fehlerzeile (Rueckfall zu #366)"
  fi
  ur_pass "migrate meldet keine unbehandelte Ausnahme"
  ur_assert_file_contains "$migrate_log" 'PostgreSQL migration 019_inbound_triggers \(version 19\) failed' \
    "migrate nennt die gescheiterte Migration (019_inbound_triggers)"
  ur_assert_file_contains "$migrate_log" 'Npgsql\.PostgresException \(SqlState 42703\)' \
    "migrate nennt Ausnahmetyp und SQLSTATE (Npgsql.PostgresException, 42703)"
  ur_assert_file_contains "$migrate_log" '42703: column "trigger_key" does not exist' "migrate nennt den Fehler (42703, trigger_key)"
  if grep -q 'Applied [0-9]* PostgreSQL migration' "$migrate_log"; then
    ur_fail "migrate meldet trotz Fehler angewendete Migrationen"
  fi
  ur_pass "migrate meldet keine angewendete Migration"
  api_container="$(ur_container_of api)"
  api_state="$(docker inspect --format '{{.State.Status}}|{{.State.StartedAt}}|{{.Config.Image}}' "$api_container" 2>/dev/null || echo 'fehlt||')"
  ur_info "API-Container nach dem Fehlschlag: ${api_state}"
  [[ "${api_state%%|*}" != running ]] || ur_fail "API laeuft trotz gescheiterter Migration"
  if [[ "${api_state##*|}" == "$CURRENT_IMAGE" ]]; then
    started="${api_state#*|}"
    started="${started%%|*}"
    [[ "$started" == 0001-01-01T00:00:00Z ]] || ur_fail "API des aktuellen Images wurde gestartet (${started})"
    ur_pass "API des aktuellen Images angelegt, aber nie gestartet (service_completed_successfully)"
  else
    ur_pass "API nicht auf das aktuelle Image gehoben und nicht gestartet"
  fi
  ur_assert_eq "$versions_before" "$(ur_migration_versions flowzer_kollision)" "schema_migrations unveraendert"
  ur_schema_fingerprint flowzer_kollision >"${UR_LOG_DIR}/fall3-schema-nachher.txt"
  diff "${UR_LOG_DIR}/fall3-schema-vorher.txt" "${UR_LOG_DIR}/fall3-schema-nachher.txt" >"${UR_LOG_DIR}/fall3-schema-diff.txt" \
    || ur_fail "Schema hat sich trotz Rollback veraendert (fall3-schema-diff.txt)"
  ur_pass "Relationen und Spalten unveraendert ($(wc -l <"${UR_LOG_DIR}/fall3-schema-nachher.txt" | tr -d ' ') Relationen; keine Indizes aus 017/018, keine Tabellen aus 020)"
  ur_assert_eq 0 "$(ur_sql flowzer_kollision "SELECT count(*) FROM pg_class WHERE relname IN ('ai_runs_instance_idx', 'idempotency_records_instance_idx', 'runtime_node_events_definition_time_idx', 'inbound_triggers_key_unique_idx', 'decision_definitions', 'decision_definition_versions')")" \
    "Zahl der Indizes und Tabellen aus 017 bis 020"
  ur_runtime_fingerprint flowzer_kollision >"${UR_LOG_DIR}/fall3-zustand-nachher.txt"
  diff "${UR_LOG_DIR}/fall3-zustand-vorher.txt" "${UR_LOG_DIR}/fall3-zustand-nachher.txt" >"${UR_LOG_DIR}/fall3-zustand-diff.txt" \
    || ur_fail "Laufzeitzustand hat sich veraendert (fall3-zustand-diff.txt)"
  ur_pass "Laufzeitzustand unveraendert"

  # Rueckweg: bisheriges Image wieder setzen, kein Restore.
  ur_deploy fall3-rueckweg flowzer_kollision "$LEGACY_IMAGE" false \
    || ur_fail "Rueckweg mit dem Image des Stands 016 gescheitert (Compose ${UR_COMPOSE_STATUS}, migrate ${UR_MIGRATE_EXIT})"
  ur_assert_eq 0 "$UR_MIGRATE_EXIT" "Rueckweg: migrate Exit"
  ur_assert_eq 0 "$(ur_migrate_applied_count "$(ur_migrate_log_file fall3-rueckweg)")" "Rueckweg: migrate wendet nichts an"
  ur_assert_eq Healthy "$(ur_readiness | cut -d/ -f1)" "Rueckweg: /health/ready"
  ur_expect_waiting "Rueckweg"
  ur_complete_instances "Rueckweg"
fi

# --- Fall 4 --------------------------------------------------------------------------------

if case_selected 4; then
  # Die Sicherungsskripte sollen nur sehen, was der Rig ihnen gibt (wie im Skripttest von R2a):
  # ambiente Verbindungs-, Pfad- und PG*-Variablen des Hosts werden vorher entfernt.
  unset STORAGE_CONNECTION_STRING STORAGE_MIGRATION_CONNECTION_STRING STORAGE_SCHEMA \
    FLOWZER_RUNTIME_ROLE FLOWZER_RUNTIME_PASSWORD FLOWZER_STORAGE_DIR FLOWZER_KEYRING_DIR \
    FLOWZER_BACKUP_DIR FLOWZER_APP_VERSION PGHOST PGPORT PGUSER PGDATABASE PGPASSWORD PGOPTIONS PGSERVICE
  # Testzweck: Klon auf eine zweite Datenbank mit echten Images. backup.sh sichert den Bestand
  # aus Fall 1 (drei abgeschlossene und drei neu wartende Instanzen) bei gestoppter API,
  # restore.sh spielt ihn samt Dateien (--files-root) in eine mit 01 vorbereitete zweite
  # Datenbank, die API laeuft dagegen mit UpToDate, Instanzliste, Zustaende und Zeilenzahlen
  # gleichen der Quelle, und die wartenden Instanzen laufen im Klon weiter, ohne die Quelle
  # zu beruehren.
  ur_begin_case "Fall 4: Sicherung aus Fall 1, Restore in eine zweite Datenbank, API dagegen"
  ur_deploy fall4-quelle flowzer "$CURRENT_IMAGE" false \
    || ur_fail "Quelle liess sich nicht starten (siehe compose-fall4-quelle.log)"
  ur_setup_definitions
  ur_start_instances
  ur_expect_waiting "Quelle"
  source_instances="${UR_LOG_DIR}/fall4-instanzen-quelle.txt"
  ur_instance_list >"$source_instances"
  ur_info "Quelle: $(wc -l <"$source_instances" | tr -d ' ') Instanzen ($(grep -c ' Completed ' "$source_instances" || true) abgeschlossen, $(grep -c ' Waiting ' "$source_instances" || true) wartend)"
  ur_stop_api
  ur_table_counts flowzer >"${UR_LOG_DIR}/fall4-zeilen-quelle.txt"
  source_state="$(ur_migration_state flowzer)"

  # Dateiablage und Keyring: Mit PostgreSQL und ohne BFF liest die API keines von beiden; die
  # Verzeichnisse belegen hier den Weg --files/--files-root der Skripte mit echten Pfaden.
  source_files="${UR_WORK_DIR}/quelle"
  mkdir -p "${source_files}/ablage/FileStorage" "${source_files}/schluesselring"
  echo '{"probe":"upgrade-restore-rig"}' >"${source_files}/ablage/FileStorage/probe.json"
  echo '<key id="r2b"/>' >"${source_files}/schluesselring/key-r2b.xml"

  migration_connection() {
    printf 'Host=db;Port=5432;Database=%s;Username=%s;Password=%s' "$1" "$UR_MIGRATION_ROLE" "$FLOWZER_UPGRADE_MIGRATION_PASSWORD"
  }
  backup_status=0
  STORAGE_MIGRATION_CONNECTION_STRING="$(migration_connection flowzer)" \
    FLOWZER_PG_CLIENT=docker FLOWZER_PG_DOCKER_NETWORK="${UR_PROJECT}_default" FLOWZER_PG_IMAGE="$PG_IMAGE" \
    FLOWZER_APP_VERSION="$CURRENT_IMAGE" STORAGE_SCHEMA="$UR_SCHEMA" \
    FLOWZER_STORAGE_DIR="${source_files}/ablage" FLOWZER_KEYRING_DIR="${source_files}/schluesselring" \
    ur_run_child "${UR_REPO_ROOT}/scripts/runtime/backup.sh" --out "${UR_WORK_DIR}/backups" \
    >"${UR_LOG_DIR}/fall4-backup.log" 2>&1 || backup_status=$?
  ur_assert_eq 0 "$backup_status" "backup.sh Exit (Ausgabe fall4-backup.log)"
  dumps=("${UR_WORK_DIR}"/backups/*.dump)
  [[ "${#dumps[@]}" -eq 1 && -f "${dumps[0]}" ]] || ur_fail "genau ein Dump erwartet"
  dump="${dumps[0]}"
  meta="${dump%.dump}.meta"
  files_archive="${dump%.dump}-files.tgz"
  ur_assert_eq "flowzer" "$(grep -m 1 '^database=' "$meta" | cut -d= -f2-)" ".meta database"
  ur_assert_eq "${source_state#*/}" "$(grep -m 1 '^schema_migrations_max=' "$meta" | cut -d= -f2-)" ".meta schema_migrations_max"
  ur_assert_eq "$CURRENT_IMAGE" "$(grep -m 1 '^app_version=' "$meta" | cut -d= -f2-)" ".meta app_version"
  cp "$meta" "${UR_LOG_DIR}/fall4-sicherung.meta"

  ur_prepare_database flowzer_klon
  restore_status=0
  STORAGE_MIGRATION_CONNECTION_STRING="$(migration_connection flowzer_klon)" \
    FLOWZER_RUNTIME_PASSWORD="$FLOWZER_UPGRADE_RUNTIME_PASSWORD" \
    FLOWZER_PG_CLIENT=docker FLOWZER_PG_DOCKER_NETWORK="${UR_PROJECT}_default" FLOWZER_PG_IMAGE="$PG_IMAGE" \
    ur_run_child "${UR_REPO_ROOT}/scripts/runtime/restore.sh" "$dump" --schema "$UR_SCHEMA" \
    --runtime-role "$UR_RUNTIME_ROLE" --files "$files_archive" --files-root "${UR_WORK_DIR}/klon-dateien" \
    >"${UR_LOG_DIR}/fall4-restore.log" 2>&1 || restore_status=$?
  ur_assert_eq 0 "$restore_status" "restore.sh Exit (Ausgabe fall4-restore.log)"
  ur_assert_file_contains "${UR_LOG_DIR}/fall4-restore.log" 'leer bis auf eine leere schema_migrations' "restore.sh erkennt das mit 01 vorbereitete Ziel als leer"
  ur_assert_file_contains "${UR_LOG_DIR}/fall4-restore.log" "Lesen als ${UR_RUNTIME_ROLE} ueber eigene Verbindung bestaetigt" "restore.sh liest als Laufzeitrolle"
  diff -r "${source_files}/ablage" "${UR_WORK_DIR}/klon-dateien/ablage" >/dev/null || ur_fail "Dateiablage im Klon weicht ab"
  diff -r "${source_files}/schluesselring" "${UR_WORK_DIR}/klon-dateien/schluesselring" >/dev/null || ur_fail "Keyring im Klon weicht ab"
  ur_pass "Dateiablage und Keyring unter --files-root identisch"
  ur_assert_eq "$source_state" "$(ur_migration_state flowzer_klon)" "Migrationsstand im Klon"
  ur_table_counts flowzer_klon >"${UR_LOG_DIR}/fall4-zeilen-klon.txt"
  diff "${UR_LOG_DIR}/fall4-zeilen-quelle.txt" "${UR_LOG_DIR}/fall4-zeilen-klon.txt" >"${UR_LOG_DIR}/fall4-zeilen-diff.txt" \
    || ur_fail "Zeilenzahlen im Klon weichen ab (fall4-zeilen-diff.txt)"
  ur_pass "Zeilenzahlen aller $(wc -l <"${UR_LOG_DIR}/fall4-zeilen-klon.txt" | tr -d ' ') Tabellen wie in der Quelle"

  ur_deploy fall4-klon flowzer_klon "$CURRENT_IMAGE" false \
    || ur_fail "API gegen den Klon startet nicht (Compose ${UR_COMPOSE_STATUS}, migrate ${UR_MIGRATE_EXIT})"
  ur_assert_eq 0 "$(ur_migrate_applied_count "$(ur_migrate_log_file fall4-klon)")" "Klon: migrate wendet nichts an"
  ur_assert_eq "Healthy/Ready/UpToDate/0/${source_state#*/}" "$(ur_readiness)" "Klon: /health/ready"
  ur_instance_list >"${UR_LOG_DIR}/fall4-instanzen-klon.txt"
  diff "$source_instances" "${UR_LOG_DIR}/fall4-instanzen-klon.txt" >"${UR_LOG_DIR}/fall4-instanzen-diff.txt" \
    || ur_fail "Instanzliste im Klon weicht ab (fall4-instanzen-diff.txt)"
  ur_pass "Instanzliste und Zustaende im Klon identisch ($(wc -l <"$source_instances" | tr -d ' ') Instanzen)"
  ur_expect_waiting "Klon"
  ur_complete_instances "Klon"
  ur_assert_eq "3" "$(ur_sql flowzer "SELECT count(*) FROM ${UR_SCHEMA}.instances WHERE instance_id IN ('${UR_REVIEW_ID}', '${UR_SERVICE_ID}', '${UR_TIMER_ID}') AND NOT is_finished")" \
    "Quelle unberuehrt: dieselben drei Instanzen warten dort weiter"

  # Testzweck: Der Weg, den restore.sh nach dem Einspielen empfiehlt, mit einer Sicherung eines
  # aelteren Stands: backup.sh sichert eine Ablage auf Stand 016 (Fixture), restore.sh spielt
  # sie in eine zweite Datenbank (Abschlusspruefung gegen die .meta: 16/16), und migrate des
  # aktuellen Images hebt den Klon auf den aktuellen Stand; die Instanzen warten danach weiter.
  ur_begin_case "Fall 4b: Sicherung des Stands 016, Restore, Migration mit dem aktuellen Image"
  [[ -f "${SCRIPT_DIR}/${UR_FIXTURE_016}" ]] || ur_fail "Fixture ${UR_FIXTURE_016} fehlt"
  ur_stop_api
  ur_prepare_database flowzer_alt016
  ur_load_fixture flowzer_alt016 "$UR_FIXTURE_016"
  ur_read_instance_ids_from_db flowzer_alt016
  ur_runtime_fingerprint flowzer_alt016 >"${UR_LOG_DIR}/fall4b-zustand-quelle.txt"
  backup_status=0
  STORAGE_MIGRATION_CONNECTION_STRING="$(migration_connection flowzer_alt016)" \
    FLOWZER_PG_CLIENT=docker FLOWZER_PG_DOCKER_NETWORK="${UR_PROJECT}_default" FLOWZER_PG_IMAGE="$PG_IMAGE" \
    FLOWZER_APP_VERSION="${LEGACY_IMAGE} (Fixture)" STORAGE_SCHEMA="$UR_SCHEMA" \
    ur_run_child "${UR_REPO_ROOT}/scripts/runtime/backup.sh" --no-files --out "${UR_WORK_DIR}/backups-016" \
    >"${UR_LOG_DIR}/fall4b-backup.log" 2>&1 || backup_status=$?
  ur_assert_eq 0 "$backup_status" "backup.sh Exit (Ausgabe fall4b-backup.log)"
  dumps=("${UR_WORK_DIR}"/backups-016/*.dump)
  [[ "${#dumps[@]}" -eq 1 && -f "${dumps[0]}" ]] || ur_fail "genau ein Dump erwartet"
  ur_assert_eq 16 "$(grep -m 1 '^schema_migrations_max=' "${dumps[0]%.dump}.meta" | cut -d= -f2-)" ".meta schema_migrations_max"
  ur_prepare_database flowzer_alt016_klon
  restore_status=0
  STORAGE_MIGRATION_CONNECTION_STRING="$(migration_connection flowzer_alt016_klon)" \
    FLOWZER_RUNTIME_PASSWORD="$FLOWZER_UPGRADE_RUNTIME_PASSWORD" \
    FLOWZER_PG_CLIENT=docker FLOWZER_PG_DOCKER_NETWORK="${UR_PROJECT}_default" FLOWZER_PG_IMAGE="$PG_IMAGE" \
    ur_run_child "${UR_REPO_ROOT}/scripts/runtime/restore.sh" "${dumps[0]}" --schema "$UR_SCHEMA" \
    --runtime-role "$UR_RUNTIME_ROLE" >"${UR_LOG_DIR}/fall4b-restore.log" 2>&1 || restore_status=$?
  ur_assert_eq 0 "$restore_status" "restore.sh Exit (Ausgabe fall4b-restore.log)"
  ur_assert_eq "16/16" "$(ur_migration_state flowzer_alt016_klon)" "Migrationsstand im Klon vor der Migration"
  ur_deploy fall4b-klon flowzer_alt016_klon "$CURRENT_IMAGE" false \
    || ur_fail "Migration des wiederhergestellten Stands 016 gescheitert (Compose ${UR_COMPOSE_STATUS}, migrate ${UR_MIGRATE_EXIT})"
  state_after="$(ur_migration_state flowzer_alt016_klon)"
  ur_assert_eq "$(seq 17 "${state_after#*/}" | paste -sd, - | sed 's/,/, /g')" \
    "$(ur_migrate_applied_list "$(ur_migrate_log_file fall4b-klon)")" "migrate wendet im Klon an"
  ur_assert_eq "Healthy/Ready/UpToDate/0/${state_after#*/}" "$(ur_readiness)" "Klon: /health/ready"
  ur_runtime_fingerprint flowzer_alt016_klon >"${UR_LOG_DIR}/fall4b-zustand-klon.txt"
  diff "${UR_LOG_DIR}/fall4b-zustand-quelle.txt" "${UR_LOG_DIR}/fall4b-zustand-klon.txt" >"${UR_LOG_DIR}/fall4b-zustand-diff.txt" \
    || ur_fail "Laufzeitzustand im migrierten Klon weicht von der Sicherung ab (fall4b-zustand-diff.txt)"
  ur_pass "Laufzeitzustand nach Restore und Migration wie in der gesicherten Ablage"
  ur_expect_waiting "Klon 016 -> ${state_after#*/}"
fi

# --- Abschluss -----------------------------------------------------------------------------

# Testzweck: Keine Ausgabe dieses Laufs enthaelt eines der drei zufaelligen Passwoerter.
ur_begin_case "Keine Passwoerter in den Ausgaben"
ur_compose logs --no-color --timestamps >"${UR_LOG_DIR}/compose.log" 2>&1 || true
if grep -rlF -e "$FLOWZER_UPGRADE_SUPERUSER_PASSWORD" -e "$FLOWZER_UPGRADE_MIGRATION_PASSWORD" \
    -e "$FLOWZER_UPGRADE_RUNTIME_PASSWORD" "$UR_LOG_DIR"; then
  ur_fail "ein Passwort steht in einer Ausgabedatei (Dateien oben)"
fi
ur_pass "kein Passwort in $(find "$UR_LOG_DIR" -type f | wc -l | tr -d ' ') Ausgabedateien"
ur_current_case=''
