#!/usr/bin/env bash
# Erzeugt den Klartext-Fixture fixtures/schema-016/flowzer-schema-016.sql fuer Fall 2 und 3 des
# Upgrade-/Restore-Rigs (R2b): Schema flowzer auf Migrationsstand 016 (Release #320, Commit
# 92d8557) mit drei wartenden Instanzen - Benutzeraufgabe mit gebundenem Formular, Auftrag fuer
# einen externen Worker, Zwischen-Timer.
#
#   tests/upgrade-restore/fixtures/make-schema-016.sh            Image wiederverwenden, falls da
#   tests/upgrade-restore/fixtures/make-schema-016.sh --rebuild  Image des Stands neu bauen
#
# Ablauf: API des Stands 016 bauen (git archive 92d8557 in ein Temp-Verzeichnis, docker build mit
# dessen Dockerfile.api; kein git worktree), PostgreSQL mit deploy/postgresql/01 vorbereiten,
# `--migrate` und API des Stands starten, ueber die HTTP-API Formular, drei Workflows und je eine
# Instanz anlegen, API stoppen und `pg_dump --schema=flowzer --no-owner --no-privileges` als
# Klartext schreiben. Der Timer-Scheduler bleibt aus; der Timer wartet deshalb im Fixture,
# obwohl seine Frist (PT2S) laengst abgelaufen ist.
#
# Reproduzierbar ist der Weg, nicht jedes Byte: IDs und Zeitstempel entstehen bei jedem Lauf neu.
# Scheitert der Bau des Stands (etwa am SDK-Band), bricht das Skript mit den letzten Zeilen des
# Bauprotokolls ab; der Grund steht dann in logs-fixture/build-92d85573f8e2.log.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RIG_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
REBUILD=0

for argument in "$@"; do
  case "$argument" in
    --rebuild) REBUILD=1 ;;
    -h|--help) sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unbekannter Schalter: $argument" >&2; exit 64 ;;
  esac
done

UR_PROJECT="flowzer-upgrade-fixture-$$-${RANDOM}"
UR_WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/flowzer-upgrade-fixture.XXXXXX")"
UR_LOG_DIR="${RIG_DIR}/logs-fixture"
rm -rf "$UR_LOG_DIR"
mkdir -p "$UR_LOG_DIR"

# shellcheck source=tests/upgrade-restore/lib.sh
source "${RIG_DIR}/lib.sh"

ur_require_tools docker jq curl git tar perl od sed
PG_IMAGE="${FLOWZER_TEST_PG_IMAGE:-postgres:17-alpine}"
export FLOWZER_TEST_PG_IMAGE="$PG_IMAGE"
LEGACY_IMAGE="${FLOWZER_UPGRADE_LEGACY_IMAGE:-$UR_LEGACY_IMAGE_DEFAULT}"
TARGET="${RIG_DIR}/${UR_FIXTURE_016}"

finish() {
  local status=$?
  trap '' INT TERM
  ur_kill_child
  if [[ -n "${FLOWZER_UPGRADE_API_IMAGE:-}" ]]; then
    ur_compose logs --no-color --timestamps >"${UR_LOG_DIR}/compose.log" 2>&1 || true
    ur_compose down -v --remove-orphans >/dev/null 2>&1 || true
  fi
  ur_redact_logs || true
  rm -rf "$UR_WORK_DIR"
  if [[ "$status" -eq 0 ]]; then
    printf '\nFixture geschrieben: %s (%ss)\n' "$TARGET" "$(ur_elapsed)"
  else
    printf '\nFixture nicht erzeugt (Exit %s). Logs: %s\n' "$status" "$UR_LOG_DIR" >&2
  fi
  exit "$status"
}
trap finish EXIT
trap 'ur_kill_child; exit 130' INT
trap 'ur_kill_child; exit 143' TERM

ur_phase "API des Stands 016 (${UR_LEGACY_COMMIT:0:12}) bereitstellen"
ur_ensure_legacy_image "$LEGACY_IMAGE" "$REBUILD" \
  || ur_fail "Die API des Stands ${UR_LEGACY_COMMIT:0:12} liess sich nicht bauen; Grund siehe oben und logs-fixture/"
ur_ensure_image "$PG_IMAGE" || ur_fail "PostgreSQL-Image fehlt"
echo "Image: ${LEGACY_IMAGE} ($(ur_image_describe "$LEGACY_IMAGE"))"

ur_phase "PostgreSQL und Stand 016 starten"
ur_init_secrets
ur_use flowzer "$LEGACY_IMAGE" false
ur_start_db || ur_fail "PostgreSQL startet nicht"
ur_prepare_database flowzer
ur_set_role_passwords
ur_deploy stand016 flowzer "$LEGACY_IMAGE" false \
  || ur_fail "Stand 016 startet nicht (Compose ${UR_COMPOSE_STATUS}, migrate ${UR_MIGRATE_EXIT}; siehe logs-fixture/)"
ur_assert_eq "16/16" "$(ur_migration_state flowzer)" "Migrationsstand"

ur_phase "Formular, Workflows und drei wartende Instanzen anlegen"
ur_setup_definitions
ur_start_instances
ur_expect_waiting "Stand 016"
ur_stop_api

ur_phase "Klartext-Dump schreiben"
pg_version="$(ur_sql postgres 'SHOW server_version')"
dump_version="$(docker exec "$UR_DB_CONTAINER" pg_dump --version)"
mkdir -p "$(dirname "$TARGET")"
{
  echo "-- Fixture des Upgrade-/Restore-Rigs (R2b): Schema flowzer auf Migrationsstand 016."
  echo "--"
  echo "-- Erzeugt von tests/upgrade-restore/fixtures/make-schema-016.sh am $(date -u +%Y-%m-%d) mit der API"
  echo "-- aus Commit ${UR_LEGACY_COMMIT} (Release #320), PostgreSQL ${pg_version},"
  echo "-- ${dump_version}; pg_dump --schema=flowzer --no-owner --no-privileges."
  echo "-- Inhalt: Formular UpgradeApproval, Workflows Upgrade_Review, Upgrade_Service, Upgrade_Timer"
  echo "-- (tests/upgrade-restore/bpmn) und je eine wartende Instanz:"
  echo "--   Aufgabe  ${UR_REVIEW_ID}"
  echo "--   Auftrag  ${UR_SERVICE_ID}"
  echo "--   Timer    ${UR_TIMER_ID} (Frist abgelaufen, Scheduler war aus)"
  echo "-- Keine Rollen, Rechte oder Passwoerter: Eigentuemerin wird, wer einspielt (im Rig die"
  echo "-- Migrationsrolle); die Laufzeitrechte setzt danach deploy/postgresql/02-laufzeitrechte.sql."
  echo "--"
  docker exec "$UR_DB_CONTAINER" pg_dump -U postgres -d flowzer --schema=flowzer --no-owner --no-privileges
} >"${UR_WORK_DIR}/fixture.sql"
grep -q '^COPY flowzer.schema_migrations ' "${UR_WORK_DIR}/fixture.sql" || ur_fail "Dump enthaelt keine schema_migrations"
mv "${UR_WORK_DIR}/fixture.sql" "$TARGET"
ur_info "$(wc -c <"$TARGET" | tr -d ' ') Byte, $(wc -l <"$TARGET" | tr -d ' ') Zeilen"
ur_info "Instanzen: Aufgabe ${UR_REVIEW_ID}, Auftrag ${UR_SERVICE_ID}, Timer ${UR_TIMER_ID}"
