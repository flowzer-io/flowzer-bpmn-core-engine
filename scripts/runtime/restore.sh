#!/usr/bin/env bash
set -euo pipefail

# Spielt eine mit scripts/runtime/backup.sh erzeugte Sicherung zurueck, setzt die Rechte der
# Laufzeitrolle neu und prueft danach Migrationsstand und Rechte.
#
# Aufruf:
#   scripts/runtime/restore.sh <sicherung.dump> [--connection <conninfo>] [--schema <name>]
#                              [--files <archiv.tgz>] [--files-root <verzeichnis>]
#                              [--overwrite-files] [--runtime-role <rolle>] [--force]
#                              [--require-checksum] [--allow-same-database]
#
# Regeln:
#   * Der Stack muss gestoppt sein. Ein Restore unter laufender Schreiblast ist kein Restore.
#   * Pruefsumme: <sicherung>.sha256 (und <archiv>.sha256) muss passen. Fehlt sie, gibt es eine
#     Warnung; mit --require-checksum bricht das Skript ab.
#   * Herkunft: <sicherung ohne .dump>.meta nennt Host und Datenbank der Quelle. Ein Ziel mit
#     demselben Host und Datenbanknamen wird verweigert, ausser mit --allow-same-database (etwa
#     beim bewussten Zurueckspielen in die Originaldatenbank nach einem Datenverlust). Fehlt die
#     .meta, laesst sich die Herkunft nicht pruefen; --force verlangt dann --allow-same-database.
#   * Das Ziel muss leer sein. Als leer gilt auch ein Schema, in dem nur die vom Rollenskript
#     deploy/postgresql/01-datenbank-und-rollen.sql vorab angelegte, leere schema_migrations
#     liegt; sie wird vor dem Restore entfernt. Enthaelt das Schema Daten, bricht das Skript ab;
#     --force verwirft das Zielschema vorher vollstaendig (DROP SCHEMA ... CASCADE).
#   * Laufzeitrechte: Nach dem Restore laeuft deploy/postgresql/02-laufzeitrechte.sql mit der
#     Migrationsverbindung, wenn die Laufzeitrolle bekannt ist (--runtime-role oder
#     FLOWZER_RUNTIME_ROLE). Sonst erscheint eine Warnung mit dem Befehl zum Nachholen.
#   * Abschlusspruefung: Migrationsstand des Ziels = Stand in der .meta; die Laufzeitrolle hat
#     USAGE auf dem Schema, volle Datenrechte auf den Tabellen und auf schema_migrations nur
#     SELECT. Ist FLOWZER_RUNTIME_PASSWORD gesetzt, liest das Skript zusaetzlich mit einer
#     eigenen Verbindung als Laufzeitrolle.
#   * Dateien: --files spielt Dateiablage und Keyring an die in der .meta hinterlegten absoluten
#     Pfade zurueck, mit --files-root stattdessen nach <verzeichnis>/<name>. Das sind auf demselben
#     Host die Pfade der Quellinstallation. Wuerde dabei eine vorhandene Datei ersetzt, bricht das
#     Skript vor jeder Aenderung ab; nur --overwrite-files erlaubt das Ueberschreiben
#     (zusaetzliche Dateien im Ziel bleiben liegen).
#   * Zugangsdaten reisen ueber Umgebungsvariablen beziehungsweise ~/.pgpass, nie ueber die
#     Kommandozeile.
#   * Stammt die Sicherung von einem aelteren Stand, fehlen Migrationen; sie werden mit
#     `dotnet WebApiEngine.dll --migrate` nachgezogen und mit `--check-config` bestaetigt.

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/runtime/pg-common.sh
source "${script_dir}/pg-common.sh"

repo_root="$(flowzer_repo_root)"
rights_sql="${repo_root}/deploy/postgresql/02-laufzeitrechte.sql"
dump_file=''
files_archive=''
files_root=''
overwrite_files=0
connection=''
schema="${STORAGE_SCHEMA:-flowzer}"
runtime_role="${FLOWZER_RUNTIME_ROLE:-}"
force=0
require_checksum=0
allow_same_database=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --connection) connection="${2:-}"; shift 2 ;;
    --schema) schema="${2:-}"; shift 2 ;;
    --files) files_archive="${2:-}"; shift 2 ;;
    --files-root) files_root="${2:-}"; shift 2 ;;
    --overwrite-files) overwrite_files=1; shift ;;
    --runtime-role) runtime_role="${2:-}"; shift 2 ;;
    --force) force=1; shift ;;
    --require-checksum) require_checksum=1; shift ;;
    --allow-same-database) allow_same_database=1; shift ;;
    -h|--help) sed -n '3,40p' "${BASH_SOURCE[0]}"; exit 0 ;;
    -*) flowzer_die "Unbekanntes Argument: $1" ;;
    *) dump_file="$1"; shift ;;
  esac
done

flowzer_validate_schema "$schema"
[[ -n "$dump_file" ]] || flowzer_die "Bitte die zurueckzuspielende Sicherung angeben (scripts/runtime/restore.sh <datei.dump>)."
[[ -f "$dump_file" ]] || flowzer_die "Sicherung ${dump_file} nicht gefunden."
if [[ -n "$files_archive" ]]; then
  [[ -f "$files_archive" ]] || flowzer_die "Dateisicherung ${files_archive} nicht gefunden."
fi
[[ -z "$files_root" || -n "$files_archive" ]] || flowzer_die "--files-root gilt nur zusammen mit --files."
[[ -z "$files_root" || "$files_root" == /* ]] || files_root="$(pwd)/${files_root}"
if [[ -n "$runtime_role" ]]; then
  # Der Name landet in SQL-Literalen; Anfuehrungszeichen, Backslash und Leerraum sind deshalb
  # ausgeschlossen. Echte Rollennamen der Installation brauchen sie nicht.
  [[ "$runtime_role" != *[\'\"\\[:space:]]* ]] \
    || flowzer_die "Ungueltiger Name der Laufzeitrolle: ${runtime_role}"
fi
[[ -f "$rights_sql" ]] || flowzer_die "${rights_sql} fehlt; restore.sh braucht das Rechteskript aus demselben Repository."

warnings=()
warn() {
  warnings+=("$*")
  echo "Warnung: $*" >&2
}

echo "Flowzer-Restore"
echo "Sicherung: ${dump_file}"

# 1. Pruefsummen - vor jedem Zugriff auf das Ziel ------------------------------------------
check_archive() {
  local file="$1"
  local status=0
  flowzer_verify_checksum "$file" || status=$?
  case "$status" in
    0) echo "Pruefsumme: ${file##*/} passt." ;;
    1) flowzer_die "Pruefsumme von ${file##*/} weicht von ${file##*/}.sha256 ab. Die Datei ist beschaedigt oder veraendert; nichts wurde angefasst." ;;
    *)
      if [[ "$require_checksum" -eq 1 ]]; then
        flowzer_die "${file##*/}.sha256 fehlt (--require-checksum)."
      fi
      warn "${file##*/}.sha256 fehlt - die Unversehrtheit von ${file##*/} ist nicht belegt."
      ;;
  esac
}
check_archive "$dump_file"
[[ -z "$files_archive" ]] || check_archive "$files_archive"

# 2. Herkunft aus der .meta -------------------------------------------------------------------
meta_file="${dump_file%.dump}.meta"
meta_host=''
meta_database=''
meta_count=''
meta_max=''
if [[ -f "$meta_file" ]]; then
  meta_schema="$(flowzer_meta_get "$meta_file" schema)"
  meta_host="$(flowzer_meta_get "$meta_file" host)"
  meta_database="$(flowzer_meta_get "$meta_file" database)"
  meta_count="$(flowzer_meta_get "$meta_file" schema_migrations_count)"
  meta_max="$(flowzer_meta_get "$meta_file" schema_migrations_max)"
  if [[ -z "$meta_count" ]]; then
    # Aelteres Format: migrations=<anzahl>/<hoechste Version>
    legacy_migrations="$(flowzer_meta_get "$meta_file" migrations)"
    if [[ "$legacy_migrations" == */* ]]; then
      meta_count="${legacy_migrations%%/*}"
      meta_max="${legacy_migrations#*/}"
    fi
  fi
  echo "Herkunft:  ${meta_host:-?}/${meta_database:-?} Schema ${meta_schema:-?}, gesichert $(flowzer_meta_get "$meta_file" created_at)," \
    "App $(flowzer_meta_get "$meta_file" app_version), Server $(flowzer_meta_get "$meta_file" postgres_server_version)," \
    "Migrationsstand ${meta_count:-?}/${meta_max:-?}"
  if [[ -n "$meta_schema" && "$meta_schema" != "$schema" ]]; then
    flowzer_die "Die Sicherung enthaelt Schema ${meta_schema}, Ziel ist Schema ${schema}. pg_restore stellt Objekte immer im gesicherten Schema her; --schema ${meta_schema} verwenden."
  fi
else
  warn "${meta_file##*/} fehlt - Herkunft und Migrationsstand der Sicherung lassen sich nicht pruefen."
fi

# 3. Dateien planen - Zielpfade und Konflikte, bevor irgendetwas geaendert wird ---------------
file_labels=()
file_members=()
file_parents=()
file_targets=()
legacy_root=''
if [[ -n "$files_archive" ]]; then
  archive_entries="$(tar -tzf "$files_archive")" || flowzer_die "Dateisicherung ${files_archive##*/} laesst sich nicht lesen."
  while IFS= read -r entry; do
    [[ -n "$entry" ]] || continue
    if [[ "$entry" == /* || "$entry" == ".." || "$entry" == ../* || "$entry" == */../* || "$entry" == */.. ]]; then
      flowzer_die "Dateisicherung enthaelt einen unzulaessigen Pfad: ${entry}"
    fi
  done <<<"$archive_entries"

  if [[ -f "$meta_file" ]]; then
    for kind in storage keyring; do
      member="$(flowzer_meta_get "$meta_file" "files_${kind}_member")"
      source_dir="$(flowzer_meta_get "$meta_file" "files_${kind}_dir")"
      [[ -n "$member" ]] || continue
      [[ "$member" != */* && "$member" != "." && "$member" != ".." ]] \
        || flowzer_die "Ungueltiger Archivname '${member}' in ${meta_file##*/}."
      if [[ -n "$files_root" ]]; then
        target_dir="${files_root%/}/${member}"
      else
        [[ "$source_dir" == /* ]] || flowzer_die "${meta_file##*/} nennt keinen absoluten Pfad fuer ${kind}; --files-root angeben."
        target_dir="$source_dir"
      fi
      [[ "${target_dir##*/}" == "$member" ]] \
        || flowzer_die "Zielpfad ${target_dir} passt nicht zum Archivnamen ${member}."
      parent_dir="${target_dir%/*}"
      [[ -n "$parent_dir" ]] || parent_dir='/'
      if [[ "$kind" == storage ]]; then file_labels+=("Dateiablage"); else file_labels+=("Keyring"); fi
      file_members+=("$member")
      file_parents+=("$parent_dir")
      file_targets+=("$target_dir")
    done
  fi
  if [[ "${#file_members[@]}" -eq 0 ]]; then
    # Archive aelterer Staende enthalten Pfade relativ zur Repository-Wurzel.
    legacy_root="${files_root:-$repo_root}"
  fi

  # Welche Dateien des Archivs gibt es am Ziel schon? Genau die wuerden ueberschrieben.
  conflicts=0
  first_conflict=''
  while IFS= read -r entry; do
    [[ -n "$entry" && "$entry" != */ ]] || continue
    destination=''
    if [[ -n "$legacy_root" ]]; then
      destination="${legacy_root%/}/${entry}"
    else
      for index in "${!file_members[@]}"; do
        if [[ "$entry" == "${file_members[$index]}/"* ]]; then
          destination="${file_parents[$index]%/}/${entry}"
          break
        fi
      done
    fi
    if [[ -n "$destination" && ( -e "$destination" || -L "$destination" ) ]]; then
      conflicts=$((conflicts + 1))
      [[ -n "$first_conflict" ]] || first_conflict="$destination"
    fi
  done <<<"$archive_entries"
  if [[ "$conflicts" -gt 0 ]]; then
    if [[ "$overwrite_files" -ne 1 ]]; then
      flowzer_die "Die Dateisicherung wuerde ${conflicts} vorhandene Datei(en) ersetzen, etwa ${first_conflict}. Auf demselben Host sind das die Dateien der Quellinstallation. --files-root auf ein leeres Verzeichnis setzen oder bewusst --overwrite-files angeben; nichts wurde geaendert."
    fi
    warn "--overwrite-files: ${conflicts} vorhandene Datei(en) werden ersetzt, etwa ${first_conflict}."
  fi
fi

# 4. Ziel ----------------------------------------------------------------------------------
conninfo="$(flowzer_resolve_connection "$connection")" \
  || flowzer_die "Keine Verbindungsangabe. STORAGE_MIGRATION_CONNECTION_STRING setzen oder --connection uebergeben."
flowzer_export_connection "$conninfo"
echo "Ziel:      $(flowzer_describe_target "$schema")"

lower() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]'; }
if [[ -n "$meta_host" && -n "$meta_database" ]]; then
  if [[ "$(lower "$meta_host")" == "$(lower "$PGHOST")" && "$meta_database" == "$PGDATABASE" ]]; then
    if [[ "$allow_same_database" -ne 1 ]]; then
      flowzer_die "Das Ziel ${PGHOST}/${PGDATABASE} ist die Quelle dieser Sicherung. Ein Restore dorthin ueberschreibt den Bestand, aus dem sie stammt; nur bewusst mit --allow-same-database."
    fi
    warn "Ziel ist die Quelle der Sicherung (--allow-same-database)."
  fi
elif [[ "$force" -eq 1 && "$allow_same_database" -ne 1 ]]; then
  flowzer_die "Ohne Host und Datenbank der Quelle (.meta) ist nicht auszuschliessen, dass das Ziel die Quelle ist. --force nur zusammen mit --allow-same-database."
fi

reachable="$(flowzer_pg_scalar 'SELECT 1')" || flowzer_die "Die Zieldatenbank ist nicht erreichbar."
[[ "$reachable" == "1" ]] || flowzer_die "Die Zieldatenbank ist nicht erreichbar."
migration_role="$(flowzer_pg_scalar 'SELECT current_user')"

if [[ -n "$runtime_role" ]]; then
  role_exists="$(flowzer_pg_scalar "SELECT count(*) FROM pg_roles WHERE rolname = '${runtime_role}'")"
  [[ "$role_exists" == "1" ]] || flowzer_die "Laufzeitrolle ${runtime_role} existiert im Zielcluster nicht."
fi

# Zustand des Zielschemas: vorhanden? andere Objekte als schema_migrations? Historie vorhanden?
target_state="$(flowzer_pg_scalar "SELECT (SELECT count(*) FROM pg_namespace WHERE nspname = '${schema}')
  || '/' || (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = '${schema}' AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
               AND c.relname <> 'schema_migrations')
  || '/' || (to_regclass('\"${schema}\".schema_migrations') IS NOT NULL)")"
IFS=/ read -r schema_exists other_objects history_exists <<<"$target_state"
history_rows=0
if [[ "$history_exists" == "true" ]]; then
  history_rows="$(flowzer_pg_scalar "SELECT count(*) FROM \"${schema}\".schema_migrations")"
fi

usage_before=''
if [[ "$schema_exists" != "1" ]]; then
  echo "Schema ${schema} fehlt im Ziel; es wird angelegt."
  flowzer_pg_run psql --quiet --no-psqlrc --set=ON_ERROR_STOP=1 \
    --command "CREATE SCHEMA \"${schema}\"" >/dev/null
elif [[ "$other_objects" == "0" && "$history_rows" == "0" ]]; then
  if [[ "$history_exists" == "true" ]]; then
    # Vom Rollenskript vorab angelegt. Der Dump bringt die Tabelle samt Inhalt selbst mit.
    echo "Schema ${schema} ist leer bis auf eine leere schema_migrations; sie wird entfernt."
    flowzer_pg_run psql --quiet --no-psqlrc --set=ON_ERROR_STOP=1 \
      --command "DROP TABLE \"${schema}\".schema_migrations" >/dev/null
  else
    echo "Schema ${schema} ist leer."
  fi
else
  if [[ "$force" -ne 1 ]]; then
    flowzer_die "Schema ${schema} ist nicht leer (${other_objects} weitere Objekte, ${history_rows} Eintraege in schema_migrations). Restore laeuft nur in ein leeres Schema; mit --force wird das Zielschema vorher verworfen."
  fi
  # Rechte haengen am Schema und gehen mit ihm verloren. Wer vorher USAGE hatte, erscheint in
  # der Warnung, falls die Laufzeitrolle nicht angegeben ist.
  usage_before="$(flowzer_pg_scalar "SELECT coalesce(string_agg(DISTINCT a.grantee::regrole::text, ', '), '')
    FROM pg_namespace n, aclexplode(n.nspacl) a
    WHERE n.nspname = '${schema}' AND a.privilege_type = 'USAGE'
      AND a.grantee <> n.nspowner AND a.grantee <> 0")"
  echo "Verwerfe Schema ${schema} (--force) ..."
  PGOPTIONS='-c client_min_messages=warning' flowzer_pg_run psql --quiet --no-psqlrc --set=ON_ERROR_STOP=1 \
    --single-transaction \
    --command "DROP SCHEMA \"${schema}\" CASCADE" \
    --command "CREATE SCHEMA \"${schema}\"" >/dev/null
fi

# 5. Einspielen ------------------------------------------------------------------------------
echo "Spiele die Datenbanksicherung ein ..."
# --schema: nur die Objekte im Schema, ohne CREATE SCHEMA - das Schema steht bereits (mit den
# Rechten des Rollenskripts oder frisch angelegt). --single-transaction: ein halb eingespielter
# Bestand ist schlimmer als ein sichtbarer Abbruch; scheitert ein Objekt, bleibt nichts liegen.
flowzer_pg_run pg_restore --dbname "$PGDATABASE" --no-owner --no-privileges \
  --single-transaction --exit-on-error --schema="$schema" <"$dump_file"

# 6. Laufzeitrechte --------------------------------------------------------------------------
# Hatte vor einem --force genau eine Rolle USAGE, ist sie mit hoher Wahrscheinlichkeit die
# Laufzeitrolle; der Befehl zum Nachholen nennt sie dann schon.
suggested_role='<laufzeitrolle>'
[[ -z "$usage_before" || "$usage_before" == *,* ]] || suggested_role="$usage_before"
# Mit Verbindungsangaben, damit der kopierte Befehl das Restore-Ziel trifft und nicht die
# Voreinstellung des Aufrufers (haeufig die Datenbank "postgres").
manual_rights="psql -h ${PGHOST} -p ${PGPORT} -U ${migration_role} -d ${PGDATABASE} -v migrationsrolle=${migration_role} -v laufzeitrolle=${suggested_role} -v schema=${schema} -f deploy/postgresql/02-laufzeitrechte.sql"
if [[ -n "$runtime_role" ]]; then
  echo "Setze Rechte der Laufzeitrolle ${runtime_role} (02-laufzeitrechte.sql) ..."
  flowzer_pg_run psql --quiet --no-psqlrc --set=ON_ERROR_STOP=1 \
    --set=schema="$schema" --set=migrationsrolle="$migration_role" --set=laufzeitrolle="$runtime_role" \
    <"$rights_sql" >/dev/null
else
  echo >&2
  warn "Laufzeitrolle unbekannt - die Rechte der Laufzeit wurden NICHT gesetzt."
  if [[ "$force" -eq 1 ]]; then
    echo "  --force hat mit dem Schema auch USAGE und die Default-Privileges verworfen;" >&2
    echo "  ohne Nachholen kann die API das Schema nicht lesen." >&2
    [[ -z "$usage_before" ]] || echo "  USAGE hatten vor dem Verwerfen: ${usage_before}" >&2
  else
    echo "  schema_migrations traegt dann womoeglich mehr als SELECT fuer die Laufzeit." >&2
  fi
  echo "  Nachholen mit der Migrationsverbindung, verbunden mit ${PGDATABASE}:" >&2
  echo "    ${manual_rights}" >&2
  echo "  oder restore.sh kuenftig mit --runtime-role <laufzeitrolle> aufrufen." >&2
fi

# 7. Dateien ---------------------------------------------------------------------------------
if [[ -n "$files_archive" ]]; then
  if [[ -n "$legacy_root" ]]; then
    warn "Keine Pfadzuordnung in der .meta; entpacke ${files_archive##*/} wie bisher nach ${legacy_root}."
    mkdir -p "$legacy_root"
    tar -xzf "$files_archive" -C "$legacy_root"
  else
    for index in "${!file_members[@]}"; do
      echo "Spiele ${file_labels[$index]} nach ${file_targets[$index]} zurueck ..."
      mkdir -p "${file_parents[$index]}"
      tar -xzf "$files_archive" -C "${file_parents[$index]}" "${file_members[$index]}"
    done
  fi
  echo "Dateiablage und Keyring wiederhergestellt."
fi

# 8. Abschlusspruefung ----------------------------------------------------------------------
failures=()
applied_state="$(flowzer_pg_scalar "SELECT count(*) || '/' || coalesce(max(version), 0) FROM \"${schema}\".schema_migrations")"
applied="${applied_state%%/*}"
highest="${applied_state#*/}"
tables="$(flowzer_pg_scalar "SELECT count(*) FROM information_schema.tables WHERE table_schema = '${schema}'")"

if [[ "$applied" == "0" ]]; then
  failures+=("In ${schema}.schema_migrations steht kein Eintrag. Entweder stammt die Sicherung aus einer anderen Ablage oder das Schema wurde nicht vollstaendig eingespielt.")
fi
if [[ -n "$meta_count" && -n "$meta_max" ]]; then
  if [[ "$applied" != "$meta_count" || "$highest" != "$meta_max" ]]; then
    failures+=("Migrationsstand ${applied}/${highest} weicht von der Sicherung ab (${meta_count}/${meta_max}).")
  fi
fi

rights_state='nicht geprueft (Laufzeitrolle unbekannt)'
if [[ -n "$runtime_role" ]]; then
  # usage | create | Tabellen ohne volle Datenrechte | schema_migrations nur lesend | Sequenzen ohne Recht
  rights_row="$(flowzer_pg_scalar "SELECT has_schema_privilege('${runtime_role}', n.oid, 'USAGE')
    || '/' || has_schema_privilege('${runtime_role}', n.oid, 'CREATE')
    || '/' || (SELECT count(*) FROM pg_class c WHERE c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
               AND c.relname <> 'schema_migrations'
               AND NOT (has_table_privilege('${runtime_role}', c.oid, 'SELECT')
                        AND has_table_privilege('${runtime_role}', c.oid, 'INSERT')
                        AND has_table_privilege('${runtime_role}', c.oid, 'UPDATE')
                        AND has_table_privilege('${runtime_role}', c.oid, 'DELETE')))
    || '/' || (SELECT has_table_privilege('${runtime_role}', c.oid, 'SELECT')
                      AND NOT has_table_privilege('${runtime_role}', c.oid, 'INSERT')
                      AND NOT has_table_privilege('${runtime_role}', c.oid, 'UPDATE')
                      AND NOT has_table_privilege('${runtime_role}', c.oid, 'DELETE')
               FROM pg_class c WHERE c.relnamespace = n.oid AND c.relname = 'schema_migrations')
    || '/' || (SELECT count(*) FROM pg_class c WHERE c.relnamespace = n.oid
               AND CASE WHEN c.relkind = 'S' THEN NOT has_sequence_privilege('${runtime_role}', c.oid, 'USAGE') ELSE false END)
    FROM pg_namespace n WHERE n.nspname = '${schema}'")"
  IFS=/ read -r has_usage has_create tables_missing history_read_only sequences_missing <<<"$rights_row"
  [[ "$has_usage" == "true" ]] || failures+=("Laufzeitrolle ${runtime_role} hat kein USAGE auf Schema ${schema}.")
  [[ "$has_create" == "false" ]] || failures+=("Laufzeitrolle ${runtime_role} darf in Schema ${schema} Objekte anlegen (CREATE).")
  [[ "$tables_missing" == "0" ]] || failures+=("${tables_missing} Tabelle(n) ohne SELECT/INSERT/UPDATE/DELETE fuer ${runtime_role}.")
  [[ "$history_read_only" == "true" ]] || failures+=("${runtime_role} hat auf schema_migrations nicht genau SELECT.")
  [[ "$sequences_missing" == "0" ]] || failures+=("${sequences_missing} Sequenz(en) ohne USAGE fuer ${runtime_role}.")
  rights_state="USAGE, Datenrechte auf allen Tabellen, schema_migrations nur lesend (has_*_privilege)"

  if [[ -n "${FLOWZER_RUNTIME_PASSWORD:-}" ]]; then
    # Eigene Verbindung als Laufzeitrolle, Passwort nur ueber die Umgebung.
    if runtime_read="$(PGUSER="$runtime_role" PGPASSWORD="$FLOWZER_RUNTIME_PASSWORD" \
        flowzer_pg_scalar "SELECT count(*) FROM \"${schema}\".schema_migrations")"; then
      if [[ "$runtime_read" == "$applied" ]]; then
        rights_state="${rights_state}; Lesen als ${runtime_role} ueber eigene Verbindung bestaetigt"
      else
        failures+=("Als ${runtime_role} gelesen: ${runtime_read} Eintraege in schema_migrations statt ${applied}.")
      fi
    else
      failures+=("Verbindung als ${runtime_role} kann ${schema}.schema_migrations nicht lesen.")
    fi
  fi
fi

echo
echo "Wiederhergestellt: ${tables} Tabelle(n) in Schema ${schema}."
echo "Migrationsstand:   ${applied} angewendet, hoechste Version ${highest}."
echo "Laufzeitrechte:    ${rights_state}"

if [[ "${#failures[@]}" -gt 0 ]]; then
  echo >&2
  echo "Abschlusspruefung fehlgeschlagen:" >&2
  printf '  - %s\n' "${failures[@]}" >&2
  echo "Den Bestand pruefen, bevor die API gestartet wird." >&2
  exit 1
fi

echo
if [[ "${#warnings[@]}" -gt 0 ]]; then
  echo "Restore abgeschlossen mit ${#warnings[@]} Warnung(en):"
  printf '  - %s\n' "${warnings[@]}"
else
  echo "Restore abgeschlossen, Abschlusspruefung ohne Befund."
fi
echo
echo "Naechste Schritte:"
echo "  1. dotnet WebApiEngine.dll --migrate       # nur falls das Paket neuer ist als die Sicherung"
echo "  2. dotnet WebApiEngine.dll --check-config  # Migrationsstand und Erreichbarkeit bestaetigen"
echo "  3. Stack starten und /health/ready pruefen"
