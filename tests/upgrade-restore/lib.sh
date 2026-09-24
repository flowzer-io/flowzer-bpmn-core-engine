#!/usr/bin/env bash
# Gemeinsame Hilfsfunktionen des Upgrade-/Restore-Rigs (R2b) fuer run.sh und
# fixtures/make-schema-016.sh. Wird mit `source` eingebunden, nie direkt aufgerufen.
#
# Vor dem Einbinden setzt der Aufrufer:
#   UR_PROJECT   eindeutiger Compose-Projektname
#   UR_LOG_DIR   Ablage der Ausgaben (Logs ohne Dumps)
#   UR_WORK_DIR  Arbeitsverzeichnis fuer Zwischenstaende (wird vom Aufrufer entfernt)
#
# Grundregeln: Passwoerter entstehen je Lauf zufaellig, reisen nur ueber Umgebungsvariablen
# (nie ueber Kommandozeilen) und werden nie ausgegeben. Alle Container gehoeren zum
# Compose-Projekt UR_PROJECT und verschwinden mit `down -v`.
#
# Kompatibel mit bash 3.2 (macOS): keine assoziativen Arrays, kein mapfile.

# Konstanten und Ergebnisvariablen hier lesen die einbindenden Skripte.
# shellcheck disable=SC2034

UR_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UR_REPO_ROOT="$(cd "${UR_DIR}/../.." && pwd)"
UR_COMPOSE_FILE="${UR_DIR}/compose.yml"
UR_BPMN_DIR="${UR_DIR}/bpmn"

# Release #320 (Schemastand 016). Aeltere Images gibt es in GHCR nicht; der Rig baut die API
# dieses Stands bei Bedarf selbst aus dem Repository (git archive, Dockerfile.api des Stands).
UR_LEGACY_COMMIT=92d85573f8e215eceee3c1d280575cd41bfedbff
UR_LEGACY_IMAGE_DEFAULT=flowzer-upgrade-restore/api:92d8557
UR_FIXTURE_016="fixtures/schema-016/flowzer-schema-016.sql"

UR_MIGRATION_ROLE=flowzer_migration
UR_RUNTIME_ROLE=flowzer_runtime
UR_SCHEMA=flowzer

# Technischer Benutzer fuer schreibende Aufrufe; die API wertet den Kopf nur im
# Development-Modus aus (siehe compose.yml).
UR_USER_ID=5a1e0b2c-4d3f-4e21-9a87-0c6b2d4e8f10
UR_JOB_TYPE=upgrade-zahlung
UR_WORKER_ID=r2b-worker

# Laufzustand
UR_API_BASE=''
UR_DB_CONTAINER=''
UR_CHILD_PID=''
UR_REVIEW_ID=''
UR_SERVICE_ID=''
UR_TIMER_ID=''
UR_MIGRATE_EXIT=''
UR_MIGRATE_HUNG=0
UR_COMPOSE_STATUS=0

# --- Ausgabe und Pruefungen --------------------------------------------------------------

ur_started_at=$(date +%s)
ur_current_case=''
ur_case_count=0
ur_check_count=0
ur_finding_count=0

ur_log_result() {
  [[ -n "${UR_LOG_DIR:-}" && -d "${UR_LOG_DIR}" ]] || return 0
  printf '%s\n' "$*" >>"${UR_LOG_DIR}/ergebnis.txt"
}

ur_phase() {
  printf '\n==> %s (bisher %ss)\n' "$1" "$(($(date +%s) - ur_started_at))"
  ur_log_result "==> $1"
}

ur_begin_case() {
  ur_case_count=$((ur_case_count + 1))
  ur_current_case="$1"
  printf '\n--- %s (bisher %ss)\n' "$1" "$(($(date +%s) - ur_started_at))"
  ur_log_result "--- $1"
}

ur_fail() {
  echo "FEHLER${ur_current_case:+ in \"${ur_current_case}\"}: $*" >&2
  ur_log_result "FEHLER: $*"
  exit 1
}

ur_pass() {
  ur_check_count=$((ur_check_count + 1))
  echo "    ok: $*"
  ur_log_result "    ok: $*"
}

ur_info() {
  echo "    $*"
  ur_log_result "    $*"
}

# Befund: beobachtetes Verhalten, das der Rig dokumentiert, ohne den Lauf abzubrechen.
ur_finding() {
  ur_finding_count=$((ur_finding_count + 1))
  echo "    BEFUND: $*"
  ur_log_result "    BEFUND: $*"
}

ur_assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$expected" == "$actual" ]] || ur_fail "${label}: erwartet '${expected}', ist '${actual}'"
  ur_pass "${label} = ${actual}"
}

ur_assert_file_contains() {
  local file="$1" pattern="$2" label="$3"
  grep -Eq -- "$pattern" "$file" || ur_fail "${label}: Muster '${pattern}' fehlt in ${file##*/}"
  ur_pass "$label"
}

# Dateisicherer Name aus einer Beschriftung ("Nach 016 -> 20" -> "nach-016-20").
ur_slug() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | tr -cs 'a-z0-9' '-' | sed 's/^-//; s/-$//'
}

ur_elapsed() {
  echo "$(($(date +%s) - ur_started_at))"
}

# --- Voraussetzungen und Geheimnisse ------------------------------------------------------

ur_require_tools() {
  local tool
  for tool in "$@"; do
    command -v "$tool" >/dev/null 2>&1 || { echo "Voraussetzung fehlt: ${tool}" >&2; exit 69; }
  done
}

# 32 Hex-Zeichen aus /dev/urandom. `od` liest genau 16 Byte; ein `head` in einer Pipe wuerde
# unter `set -o pipefail` mit SIGPIPE abbrechen.
ur_random_secret() {
  od -An -N16 -tx1 /dev/urandom | tr -d ' \n'
}

ur_init_secrets() {
  FLOWZER_UPGRADE_SUPERUSER_PASSWORD="$(ur_random_secret)"
  FLOWZER_UPGRADE_MIGRATION_PASSWORD="$(ur_random_secret)"
  FLOWZER_UPGRADE_RUNTIME_PASSWORD="$(ur_random_secret)"
  export FLOWZER_UPGRADE_SUPERUSER_PASSWORD FLOWZER_UPGRADE_MIGRATION_PASSWORD FLOWZER_UPGRADE_RUNTIME_PASSWORD
}

# Ersetzt die Passwoerter dieses Laufs in allen Ausgabedateien. Die eigentliche Pruefung,
# dass keines darin steht, macht run.sh vorher (letzter Fall); dies ist die zweite Linie.
ur_redact_logs() {
  local file value
  [[ -d "${UR_LOG_DIR:-}" ]] || return 0
  for value in "${FLOWZER_UPGRADE_SUPERUSER_PASSWORD:-}" "${FLOWZER_UPGRADE_MIGRATION_PASSWORD:-}" "${FLOWZER_UPGRADE_RUNTIME_PASSWORD:-}"; do
    [[ -n "$value" ]] || continue
    while IFS= read -r file; do
      VALUE="$value" perl -pi -e 's/\Q$ENV{VALUE}\E/***/g' "$file"
    done < <(grep -rlF -- "$value" "$UR_LOG_DIR" 2>/dev/null || true)
  done
}

# --- Compose -------------------------------------------------------------------------------

ur_compose() {
  docker compose -p "$UR_PROJECT" -f "$UR_COMPOSE_FILE" "$@"
}

# Waehlt Datenbank, API-Image und Timer-Scheduler fuer die naechsten Compose-Aufrufe.
ur_use() {
  export FLOWZER_UPGRADE_DATABASE="$1"
  export FLOWZER_UPGRADE_API_IMAGE="$2"
  export FLOWZER_UPGRADE_TIMERS="${3:-false}"
}

# Lange Schritte laufen im Hintergrund, damit ein Signal (Strg+C, Abbruch des CI-Runners) den
# Aufrufer sofort erreicht und dessen EXIT-Falle aufraeumen kann.
ur_run_child() {
  local exit_code=0
  "$@" &
  UR_CHILD_PID=$!
  wait "$UR_CHILD_PID" || exit_code=$?
  UR_CHILD_PID=''
  return "$exit_code"
}

ur_kill_child() {
  if [[ -n "${UR_CHILD_PID:-}" ]]; then
    kill -TERM "$UR_CHILD_PID" 2>/dev/null || true
    wait "$UR_CHILD_PID" 2>/dev/null || true
    UR_CHILD_PID=''
  fi
}

# Stellt sicher, dass ein Image lokal vorliegt. Das Vorgaengerimage gibt es in GHCR nur fuer
# linux/amd64; auf arm64-Hosts laeuft es dann emuliert.
ur_ensure_image() {
  local image="$1"
  if docker image inspect "$image" >/dev/null 2>&1; then
    return 0
  fi
  echo "Ziehe ${image} ..."
  if docker pull -q "$image" >>"${UR_LOG_DIR}/docker-pull.log" 2>&1; then
    return 0
  fi
  echo "Kein Image fuer die Hostplattform; versuche linux/amd64 (Emulation) ..."
  docker pull -q --platform linux/amd64 "$image" >>"${UR_LOG_DIR}/docker-pull.log" 2>&1 \
    || { echo "Image ${image} nicht verfuegbar (siehe docker-pull.log)." >&2; return 1; }
}

ur_image_describe() {
  docker image inspect --format '{{.Os}}/{{.Architecture}}, erstellt {{.Created}}, Revision {{index .Config.Labels "org.opencontainers.image.revision"}}' "$1" 2>/dev/null || echo 'unbekannt'
}

# Baut die API eines aelteren Stands aus dem Repository, ohne git worktree: `git archive` in
# ein Temp-Verzeichnis und `docker build` mit dem Dockerfile.api jenes Stands. Fehlt der Commit
# im flachen Klon (CI), wird genau er nachgeladen.
ur_build_image_from_commit() {
  local commit="$1" image="$2"
  local source_dir="${UR_WORK_DIR}/src-${commit:0:12}"
  local build_log="${UR_LOG_DIR}/build-${commit:0:12}.log"
  if ! git -C "$UR_REPO_ROOT" cat-file -e "${commit}^{commit}" 2>/dev/null; then
    echo "Commit ${commit:0:12} fehlt lokal; lade ihn nach (git fetch --depth=1) ..."
    git -C "$UR_REPO_ROOT" fetch --quiet --no-tags --depth=1 origin "$commit" >>"$build_log" 2>&1 \
      || { echo "Commit ${commit} ist nicht verfuegbar (siehe ${build_log##*/})." >&2; return 1; }
  fi
  rm -rf "$source_dir"
  mkdir -p "$source_dir"
  git -C "$UR_REPO_ROOT" archive "$commit" | tar -x -C "$source_dir"
  echo "Baue ${image} aus ${commit:0:12} (Dockerfile.api jenes Stands) ..."
  if ! docker build --label "org.opencontainers.image.revision=${commit}" \
      -f "${source_dir}/Dockerfile.api" -t "$image" "$source_dir" >>"$build_log" 2>&1; then
    echo "Bau von ${commit:0:12} gescheitert. Letzte Zeilen aus ${build_log##*/}:" >&2
    tail -n 25 "$build_log" >&2
    return 1
  fi
  rm -rf "$source_dir"
}

ur_ensure_legacy_image() {
  local image="$1" rebuild="${2:-0}"
  if [[ "$rebuild" -eq 0 ]] && docker image inspect "$image" >/dev/null 2>&1; then
    return 0
  fi
  ur_build_image_from_commit "$UR_LEGACY_COMMIT" "$image"
}

# Startet die Datenbank und merkt sich den Container fuer schnelle `docker exec`-Aufrufe.
ur_start_db() {
  ur_run_child ur_compose up -d --wait db >>"${UR_LOG_DIR}/compose-db.log" 2>&1 \
    || { echo "PostgreSQL wurde nicht bereit (siehe compose-db.log)." >&2; return 1; }
  UR_DB_CONTAINER="$(ur_compose ps -q db)"
  [[ -n "$UR_DB_CONTAINER" ]] || { echo "Datenbankcontainer nicht gefunden." >&2; return 1; }
}

ur_container_of() {
  # sed statt head: liest die Eingabe ganz, sonst droht SIGPIPE unter pipefail.
  ur_compose ps -aq "$1" 2>/dev/null | sed -n 1p
}

ur_migrate_log_file() {
  printf '%s/%s-migrate.log' "$UR_LOG_DIR" "$1"
}

# Ein Deploy-Schritt wie in der Installation: Datenbank und Image waehlen, migrate frisch
# anlegen und `docker compose up --wait api` ausfuehren. Compose startet api nur, wenn migrate
# mit Exit 0 endet (service_completed_successfully).
#
# Ein Watchdog beendet einen migrate-Prozess, der nach einer unbehandelten Ausnahme nicht
# endet (setzt UR_MIGRATE_HUNG=1). Ergebnis: UR_COMPOSE_STATUS, UR_MIGRATE_EXIT; die Ausgabe
# von migrate liegt danach in <label>-migrate.log. Rueckgabe 0 nur, wenn Compose Erfolg meldet.
ur_deploy() {
  local label="$1" database="$2" image="$3" timers="${4:-false}"
  local compose_log="${UR_LOG_DIR}/compose-${label}.log"
  local migrate_log
  migrate_log="$(ur_migrate_log_file "$label")"
  ur_use "$database" "$image" "$timers"
  UR_MIGRATE_EXIT=''
  UR_MIGRATE_HUNG=0
  UR_COMPOSE_STATUS=0

  # Frischer migrate-Container: seine Ausgabe gehoert genau zu diesem Schritt.
  ur_compose rm -fsv migrate >>"$compose_log" 2>&1 || true

  ur_compose up -d --wait api >>"$compose_log" 2>&1 &
  UR_CHILD_PID=$!
  local waited=0 hung_since=0 migrate_container state
  while kill -0 "$UR_CHILD_PID" 2>/dev/null; do
    sleep 1
    waited=$((waited + 1))
    migrate_container="$(ur_container_of migrate)"
    if [[ -n "$migrate_container" ]]; then
      state="$(docker inspect --format '{{.State.Status}}' "$migrate_container" 2>/dev/null || true)"
      docker logs "$migrate_container" >"${UR_WORK_DIR}/migrate-watch.log" 2>&1 || true
      if [[ "$state" == running ]] && grep -q 'Unhandled exception' "${UR_WORK_DIR}/migrate-watch.log"; then
        [[ "$hung_since" -gt 0 ]] || hung_since=$waited
        if [[ $((waited - hung_since)) -ge 30 ]]; then
          UR_MIGRATE_HUNG=1
          docker kill "$migrate_container" >/dev/null 2>&1 || true
        fi
      fi
    fi
    if [[ "$waited" -ge 600 ]]; then
      echo "Compose-Schritt ${label} dauert ueber 600 s; breche ab." >&2
      kill -TERM "$UR_CHILD_PID" 2>/dev/null || true
    fi
  done
  wait "$UR_CHILD_PID" || UR_COMPOSE_STATUS=$?
  UR_CHILD_PID=''

  migrate_container="$(ur_container_of migrate)"
  if [[ -n "$migrate_container" ]]; then
    docker logs "$migrate_container" >"$migrate_log" 2>&1 || true
    UR_MIGRATE_EXIT="$(docker inspect --format '{{.State.ExitCode}}' "$migrate_container" 2>/dev/null || echo '?')"
  else
    : >"$migrate_log"
    UR_MIGRATE_EXIT='?'
  fi
  UR_API_BASE=''
  if [[ "$UR_COMPOSE_STATUS" -eq 0 ]]; then
    local port
    port="$(ur_compose port api 8080 | sed -n 1p)"
    UR_API_BASE="http://${port}"
  fi
  return "$UR_COMPOSE_STATUS"
}

# Nur die API neu starten (anderer Timer-Schalter), migrate bleibt unberuehrt.
ur_restart_api() {
  local label="$1" timers="$2"
  export FLOWZER_UPGRADE_TIMERS="$timers"
  ur_run_child ur_compose up -d --wait --no-deps api >>"${UR_LOG_DIR}/compose-${label}.log" 2>&1 \
    || ur_fail "API-Neustart ${label} scheiterte (siehe compose-${label}.log)"
  UR_API_BASE="http://$(ur_compose port api 8080 | sed -n 1p)"
}

ur_stop_api() {
  ur_run_child ur_compose stop api >>"${UR_LOG_DIR}/compose-stop.log" 2>&1 || ur_fail "API liess sich nicht stoppen"
  UR_API_BASE=''
}

# Aus der migrate-Ausgabe: Zahl und Liste der angewendeten Migrationen, Zahl der ergaenzten
# Formularbindungen.
ur_migrate_applied_count() {
  sed -n 's/.*Applied \([0-9][0-9]*\) PostgreSQL migration(s).*/\1/p' "$1" | sed -n 1p
}

ur_migrate_applied_list() {
  sed -n 's/.*Applied [0-9][0-9]* PostgreSQL migration(s) to schema [a-z0-9_]*: *\(.*\)$/\1/p' "$1" | sed -n 1p | tr -d '\r'
}

ur_migrate_form_bindings() {
  sed -n 's/.*historische Formularbindungen: \([0-9][0-9]*\).*/\1/p' "$1" | sed -n 1p
}

# --- Datenbank -----------------------------------------------------------------------------

# SQL als Superuser ueber den lokalen Socket im Container; Ausgabe ohne Rahmen.
ur_sql() {
  local database="$1" statement="$2"
  docker exec "$UR_DB_CONTAINER" psql --no-psqlrc -tA -v ON_ERROR_STOP=1 -U postgres -d "$database" -c "$statement"
}

# psql als Migrationsrolle ueber TCP (echte Rechte). Das Passwort reist ueber die Umgebung
# des docker-Aufrufs (`-e PGPASSWORD` nennt nur den Namen), nie ueber die Kommandozeile.
ur_psql_migration() {
  local database="$1"
  shift
  PGPASSWORD="$FLOWZER_UPGRADE_MIGRATION_PASSWORD" docker exec -i -e PGPASSWORD "$UR_DB_CONTAINER" \
    psql --no-psqlrc -v ON_ERROR_STOP=1 -h 127.0.0.1 -U "$UR_MIGRATION_ROLE" -d "$database" "$@"
}

# Legt eine Datenbank mit dem ausgelieferten Rollenskript an (getrennte Migrations- und
# Laufzeitrolle). Beim ersten Aufruf entstehen die Rollen; ihre Passwoerter setzt
# ur_set_role_passwords ueber stdin.
ur_prepare_database() {
  local database="$1"
  docker exec "$UR_DB_CONTAINER" psql --no-psqlrc -q -v ON_ERROR_STOP=1 -U postgres -d postgres \
    -v datenbank="$database" -v migrationsrolle="$UR_MIGRATION_ROLE" -v laufzeitrolle="$UR_RUNTIME_ROLE" \
    -v schema="$UR_SCHEMA" -f /flowzer-sql/01-datenbank-und-rollen.sql >>"${UR_LOG_DIR}/rollenskript.log" 2>&1 \
    || ur_fail "01-datenbank-und-rollen.sql scheiterte fuer ${database} (siehe rollenskript.log)"
}

ur_set_role_passwords() {
  printf "ALTER ROLE %s PASSWORD '%s';\nALTER ROLE %s PASSWORD '%s';\n" \
    "$UR_MIGRATION_ROLE" "$FLOWZER_UPGRADE_MIGRATION_PASSWORD" "$UR_RUNTIME_ROLE" "$FLOWZER_UPGRADE_RUNTIME_PASSWORD" \
    | docker exec -i "$UR_DB_CONTAINER" psql --no-psqlrc -q -v ON_ERROR_STOP=1 -U postgres -d postgres >/dev/null \
    || ur_fail "Rollenpasswoerter liessen sich nicht setzen"
}

# Spielt den Klartext-Fixture (Schema flowzer auf Stand 016) als Migrationsrolle in eine mit
# 01 vorbereitete Datenbank ein: vorbereitetes, leeres Schema verwerfen, Fixture einspielen
# (legt das Schema neu an, Eigentuemerin ist die Migrationsrolle), danach 02 fuer die
# Laufzeitrechte - dieselbe Reihenfolge wie restore.sh nach --force. Eine Transaktion.
ur_load_fixture() {
  local database="$1" fixture="$2"
  ur_psql_migration "$database" -q --single-transaction \
    -c "DROP SCHEMA ${UR_SCHEMA} CASCADE" \
    -f "/flowzer-fixtures/${fixture#fixtures/}" \
    -v migrationsrolle="$UR_MIGRATION_ROLE" -v laufzeitrolle="$UR_RUNTIME_ROLE" -v schema="$UR_SCHEMA" \
    -f /flowzer-sql/02-laufzeitrechte.sql >>"${UR_LOG_DIR}/fixture-${database}.log" 2>&1 \
    || ur_fail "Fixture ${fixture} liess sich nicht in ${database} einspielen (siehe fixture-${database}.log)"
}

# Migrationsstand als "<anzahl>/<hoechste Version>".
ur_migration_state() {
  ur_sql "$1" "SELECT count(*) || '/' || coalesce(max(version), 0) FROM ${UR_SCHEMA}.schema_migrations"
}

ur_migration_versions() {
  ur_sql "$1" "SELECT coalesce(string_agg(version::text, ',' ORDER BY version), '') FROM ${UR_SCHEMA}.schema_migrations"
}

# Laufzeitzustand der Engine als sortierte Zeilen: Instanzen, Aufgaben (samt Fristen), Auftraege
# (samt Sperre und Versuchen), Timer und Knotenereignisse (Historie) sowie Deployments (samt
# gebundener Formulare im Datensatz) und Formulare, jeweils mit Pruefsumme des gespeicherten
# Datensatzes. Unveraendert heisst: dieselben Zeilen.
ur_runtime_fingerprint() {
  ur_sql "$1" "
    SELECT 'instance ' || instance_id || ' ' || meta_definition_id || ' finished=' || is_finished || ' body=' || md5(body) FROM ${UR_SCHEMA}.instances
    UNION ALL SELECT 'usertask ' || id || ' instance=' || coalesce(process_instance_id::text, '-') || ' body=' || md5(body) FROM ${UR_SCHEMA}.user_task_subscriptions
    UNION ALL SELECT 'job ' || id || ' instance=' || process_instance_id || ' type=' || type || ' retries=' || retries || ' locked=' || coalesce(locked_by, '-') || ' body=' || md5(body) FROM ${UR_SCHEMA}.service_task_jobs
    UNION ALL SELECT 'timer ' || id || ' instance=' || coalesce(process_instance_id::text, '-') || ' due=' || to_char(due_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US') || ' body=' || md5(body) FROM ${UR_SCHEMA}.timer_subscriptions
    UNION ALL SELECT 'definition ' || id || ' ' || definition_id || ' active=' || is_active || ' body=' || md5(body) FROM ${UR_SCHEMA}.definitions
    UNION ALL SELECT 'form ' || id || ' form=' || form_id || ' body=' || md5(body) FROM ${UR_SCHEMA}.forms
    UNION ALL SELECT 'deadline ' || user_task_id || ' revision=' || revision || ' next=' || coalesce(to_char(next_check_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US'), '-') || ' body=' || md5(body) FROM ${UR_SCHEMA}.user_task_deadlines
    UNION ALL SELECT 'event ' || id || ' instance=' || process_instance_id || ' node=' || flow_node_id || ' at=' || to_char(occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US') || ' body=' || md5(body) FROM ${UR_SCHEMA}.runtime_node_events
    ORDER BY 1"
}

# Relationen im Schema (Tabellen, Indizes, Sequenzen) samt Spalten der Tabellen: der
# Schemastand, unabhaengig vom Inhalt.
ur_schema_fingerprint() {
  ur_sql "$1" "
    SELECT c.relkind::text || ' ' || c.relname || coalesce(' (' || (
             SELECT string_agg(a.attname || ':' || format_type(a.atttypid, a.atttypmod), ', ' ORDER BY a.attnum)
             FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
             AND c.relkind = 'r') || ')', '')
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = '${UR_SCHEMA}' AND c.relkind IN ('r', 'i', 'S', 'v', 'm')
    ORDER BY 1"
}

# Zeilenzahl je Tabelle des Schemas, eine Zeile je Tabelle.
ur_table_counts() {
  ur_sql "$1" "
    SELECT c.relname || '=' || (xpath('/row/c/text()',
             query_to_xml(format('SELECT count(*) AS c FROM %I.%I', n.nspname, c.relname), false, true, '')))[1]::text
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = '${UR_SCHEMA}' AND c.relkind = 'r'
    ORDER BY c.relname"
}

# Liest die drei Instanz-IDs einer Ablage (je Workflow die juengste nicht beendete).
ur_read_instance_ids_from_db() {
  local database="$1"
  UR_REVIEW_ID="$(ur_sql "$database" "SELECT instance_id FROM ${UR_SCHEMA}.instances WHERE meta_definition_id = 'Upgrade_Review' AND NOT is_finished ORDER BY instance_id LIMIT 1")"
  UR_SERVICE_ID="$(ur_sql "$database" "SELECT instance_id FROM ${UR_SCHEMA}.instances WHERE meta_definition_id = 'Upgrade_Service' AND NOT is_finished ORDER BY instance_id LIMIT 1")"
  UR_TIMER_ID="$(ur_sql "$database" "SELECT instance_id FROM ${UR_SCHEMA}.instances WHERE meta_definition_id = 'Upgrade_Timer' AND NOT is_finished ORDER BY instance_id LIMIT 1")"
  [[ -n "$UR_REVIEW_ID" && -n "$UR_SERVICE_ID" && -n "$UR_TIMER_ID" ]] \
    || ur_fail "In ${database} fehlen wartende Instanzen (Aufgabe '${UR_REVIEW_ID}', Auftrag '${UR_SERVICE_ID}', Timer '${UR_TIMER_ID}')"
}

# --- HTTP ----------------------------------------------------------------------------------

# Ruft die API auf und gibt den Antwortkoerper aus. Rueckgabe 0 nur bei HTTP 2xx. Jeder Aufruf
# landet gekuerzt in http.log (Antworten enthalten keine Zugangsdaten).
ur_http() {
  local method="$1" path="$2" data="${3:-}" content_type="${4:-application/json}"
  local body_file="${UR_WORK_DIR}/http-body.$$"
  local status
  local args=(-sS -o "$body_file" -w '%{http_code}' -X "$method" -H "X-Flowzer-UserId: ${UR_USER_ID}" -H 'Accept: application/json')
  if [[ -n "$data" ]]; then
    args+=(-H "Content-Type: ${content_type}" --data-binary "$data")
  fi
  status="$(curl "${args[@]}" "${UR_API_BASE}${path}" 2>>"${UR_LOG_DIR}/http.log")" || status=000
  printf '%s %s %s -> %s %s\n' "$(date -u +%H:%M:%S)" "$method" "$path" "$status" \
    "$(head -c 400 "$body_file" 2>/dev/null | tr '\n' ' ' | tr -s ' ')" >>"${UR_LOG_DIR}/http.log"
  cat "$body_file" 2>/dev/null || true
  rm -f "$body_file"
  [[ "$status" == 2* ]]
}

# Zustand als Name; die API liefert ihn als Zahl (ProcessInstanceStateDto).
UR_JQ_STATE='def statename: if type == "number" then (["Initialized","Running","Waiting","Completing","Completed","Failing","Failed","Terminating","Terminated","Compensating","Compensated"][.] // tostring) else . end;'

ur_instance_state() {
  local body
  body="$(ur_http GET "/instance/$1")" || { echo "HTTP-Fehler"; return 0; }
  jq -r "${UR_JQ_STATE} .result.state | statename" <<<"$body"
}

# Bereitschaft: Healthy; wenn die API den Migrationsstand meldet, UpToDate ohne ausstehende.
# Ausgabe: "<status>/<storage>/<migrationState>/<pending>/<expectedVersion>".
ur_readiness() {
  local body
  body="$(ur_http GET /health/ready)" || true
  jq -r '.result | [.status, .storage, (.details.migrationState // "-"), (.details.pendingMigrationCount // "-"), (.details.expectedMigrationVersion // "-")] | map(tostring) | join("/")' <<<"$body" 2>/dev/null || echo 'keine Antwort'
}

# Legt Formular, Katalogeintraege und Deployments der drei Workflows an. Idempotent: was die
# Ablage schon kennt, wird nicht erneut angelegt.
ur_setup_definitions() {
  local body form_id
  body="$(ur_http GET /form/meta)" || ur_fail "Formularliste nicht lesbar: ${body}"
  form_id="$(jq -r '.result[]? | select(.name == "UpgradeApproval") | .formId' <<<"$body" | sed -n 1p)"
  if [[ -z "$form_id" ]]; then
    form_id="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n' | sed -E 's/^(.{8})(.{4}).(.{3}).(.{3})(.{12})$/\1-\2-4\3-8\4-\5/')"
    body="$(ur_http POST "/form/meta/${form_id}" "$(jq -nc --arg id "$form_id" '{formId: $id, name: "UpgradeApproval"}')")" \
      || ur_fail "Formular-Metadaten nicht gespeichert: ${body}"
    local schema='{"components":[{"label":"Bemerkung","key":"bemerkung","type":"textfield","input":true}]}'
    body="$(ur_http POST /form "$(jq -nc --arg id "$form_id" --arg data "$schema" '{formId: $id, version: {major: 1, minor: 0}, formData: $data}')")" \
      || ur_fail "Formular nicht gespeichert: ${body}"
  fi

  local entry definition_id file
  body="$(ur_http GET /definition/meta)" || ur_fail "Katalog nicht lesbar: ${body}"
  local known="$body"
  for entry in Upgrade_Review:upgrade-review Upgrade_Service:upgrade-service Upgrade_Timer:upgrade-timer; do
    definition_id="${entry%%:*}"
    file="${UR_BPMN_DIR}/${entry#*:}.bpmn"
    if ! jq -e --arg id "$definition_id" '.result[]? | select(.definitionId == $id)' <<<"$known" >/dev/null; then
      body="$(ur_http POST /definition/meta "$(jq -nc --arg id "$definition_id" '{definitionId: $id, name: $id, description: "Upgrade-/Restore-Rig (R2b)"}')")" \
        || ur_fail "Katalogeintrag ${definition_id} nicht angelegt: ${body}"
      body="$(ur_http POST /definition/deploy "@${file}" text/plain)" \
        || ur_fail "Deployment ${definition_id} gescheitert: ${body}"
      jq -e '.successful == true' <<<"$body" >/dev/null || ur_fail "Deployment ${definition_id} gescheitert: ${body}"
    fi
  done
}

# Startet je Workflow eine Instanz und merkt sich die IDs.
ur_start_instances() {
  local body
  body="$(ur_http POST /definition/meta/Upgrade_Review/instance '{}')" || ur_fail "Start Upgrade_Review: ${body}"
  UR_REVIEW_ID="$(jq -r '.result.instanceId' <<<"$body")"
  body="$(ur_http POST /definition/meta/Upgrade_Service/instance '{}')" || ur_fail "Start Upgrade_Service: ${body}"
  UR_SERVICE_ID="$(jq -r '.result.instanceId' <<<"$body")"
  body="$(ur_http POST /definition/meta/Upgrade_Timer/instance '{}')" || ur_fail "Start Upgrade_Timer: ${body}"
  UR_TIMER_ID="$(jq -r '.result.instanceId' <<<"$body")"
  ur_info "Instanzen: Aufgabe ${UR_REVIEW_ID}, Auftrag ${UR_SERVICE_ID}, Timer ${UR_TIMER_ID}"
}

# Prueft ueber die API, dass die drei Instanzen warten: Zustand Waiting, die Aufgabe steht in
# der Aufgabenliste, der Auftrag in der Auftragsliste, der Timer als Anmeldung der Instanz.
ur_expect_waiting() {
  local label="$1" body id
  for id in "$UR_REVIEW_ID" "$UR_SERVICE_ID" "$UR_TIMER_ID"; do
    ur_assert_eq Waiting "$(ur_instance_state "$id")" "${label}: Instanz ${id:0:8} wartet"
  done
  body="$(ur_http GET /usertask)" || ur_fail "Aufgabenliste nicht lesbar: ${body}"
  ur_assert_eq "UserTask_1" "$(jq -r --arg id "$UR_REVIEW_ID" '[.result[]? | select(.processInstanceId == $id) | .token.currentFlowNodeId] | join(",")' <<<"$body")" \
    "${label}: Aufgabe der Instanz ${UR_REVIEW_ID:0:8} offen"
  body="$(ur_http GET /job)" || ur_fail "Auftragsliste nicht lesbar: ${body}"
  ur_assert_eq "$UR_JOB_TYPE" "$(jq -r --arg id "$UR_SERVICE_ID" '[.result[]? | select(.processInstanceId == $id) | .type] | join(",")' <<<"$body")" \
    "${label}: Auftrag der Instanz ${UR_SERVICE_ID:0:8} offen"
  body="$(ur_http GET "/instance/${UR_TIMER_ID}/subscription/timers")" || ur_fail "Timer nicht lesbar: ${body}"
  ur_assert_eq "TimerCatch_1" "$(jq -r '[.result[]? | .flowNodeId] | join(",")' <<<"$body")" \
    "${label}: Timer der Instanz ${UR_TIMER_ID:0:8} angemeldet (faellig $(jq -r '.result[0].dueAt' <<<"$body"))"
}

# Schliesst die drei Instanzen ab: Aufgabe abschliessen, Auftrag holen und zurueckmelden,
# danach API mit eingeschaltetem Timer-Scheduler neu starten - der bereits faellige Timer
# feuert beim Start. Erwartet anschliessend alle drei Instanzen im Zustand Completed.
ur_complete_instances() {
  local label="$1" body task job_ids job_id
  body="$(ur_http GET /usertask)" || ur_fail "Aufgabenliste nicht lesbar: ${body}"
  task="$(jq -c --arg id "$UR_REVIEW_ID" '[.result[]? | select(.processInstanceId == $id)][0]' <<<"$body")"
  [[ "$task" != null ]] || ur_fail "${label}: keine Aufgabe fuer ${UR_REVIEW_ID}"
  body="$(ur_http POST /usertask "$(jq -c '{flowNodeId: .token.currentFlowNodeId, tokenId: .token.id, processInstanceId: .processInstanceId, data: {bemerkung: "nach dem Update abgeschlossen"}}' <<<"$task")")" \
    || ur_fail "${label}: Aufgabe liess sich nicht abschliessen: ${body}"
  ur_pass "${label}: Aufgabe abgeschlossen"

  body="$(ur_http POST /job/fetch "$(jq -nc --arg type "$UR_JOB_TYPE" --arg worker "$UR_WORKER_ID" '{type: $type, workerId: $worker, maxJobs: 10, lockSeconds: 120}')")" \
    || ur_fail "${label}: Auftraege liessen sich nicht holen: ${body}"
  job_ids="$(jq -r --arg id "$UR_SERVICE_ID" '[.result[]? | select(.processInstanceId == $id) | .id] | join(",")' <<<"$body")"
  [[ -n "$job_ids" && "$job_ids" != *,* ]] || ur_fail "${label}: genau ein Auftrag fuer ${UR_SERVICE_ID} erwartet, geholt: '${job_ids}'"
  ur_pass "${label}: Auftrag geholt"
  job_id="$job_ids"
  body="$(ur_http POST "/job/${job_id}/complete" "$(jq -nc --arg worker "$UR_WORKER_ID" '{workerId: $worker, variables: {zahlung: "ok"}}')")" \
    || ur_fail "${label}: Auftrag liess sich nicht zurueckmelden: ${body}"
  ur_pass "${label}: Auftrag zurueckgemeldet"

  ur_restart_api "$(ur_slug "$label")-timer" true
  local attempt state=''
  for attempt in $(seq 1 30); do
    state="$(ur_instance_state "$UR_TIMER_ID")"
    [[ "$state" != Completed ]] || break
    sleep 1
  done
  ur_assert_eq Completed "$state" "${label}: Timer gefeuert nach Neustart mit Timer-Scheduler (Versuch ${attempt})"
  ur_assert_eq Completed "$(ur_instance_state "$UR_REVIEW_ID")" "${label}: Instanz ${UR_REVIEW_ID:0:8} (Aufgabe) abgeschlossen"
  ur_assert_eq Completed "$(ur_instance_state "$UR_SERVICE_ID")" "${label}: Instanz ${UR_SERVICE_ID:0:8} (Auftrag) abgeschlossen"
  body="$(ur_http GET "/instance/${UR_TIMER_ID}/subscription/timers")" || ur_fail "Timer nicht lesbar: ${body}"
  ur_assert_eq 0 "$(jq -r '[.result[]?] | length' <<<"$body")" "${label}: keine Timer-Anmeldung mehr"
}

# Instanzliste der API als sortierte Zeilen "<id> <zustand> <workflow> <aufgaben> <auftraege>".
ur_instance_list() {
  local body
  body="$(ur_http GET /instance)" || ur_fail "Instanzliste nicht lesbar: ${body}"
  jq -r "${UR_JQ_STATE} .result[] | [.instanceId, (.state | statename), .relatedDefinitionId, (.userTaskSubscriptionCount | tostring), (.serviceSubscriptionCount | tostring)] | join(\" \")" <<<"$body" | sort
}
