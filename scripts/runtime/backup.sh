#!/usr/bin/env bash
set -euo pipefail

# Sichert eine Flowzer-Installation reproduzierbar:
#   1. die PostgreSQL-Ablage als pg_dump-Archiv (-Fc, nur das Flowzer-Schema)
#   2. optional die Dateiablage und den Data-Protection-Keyring als Tar-Archiv
#
# Zugangsdaten reisen ausschliesslich ueber Umgebungsvariablen beziehungsweise ~/.pgpass;
# in der Prozessliste des Hosts steht nie ein Passwort.
#
# Aufruf:
#   scripts/runtime/backup.sh [--connection <conninfo>] [--schema <name>] [--out <verzeichnis>]
#                             [--no-files] [--files-only]
#
# Ohne --connection wird STORAGE_MIGRATION_CONNECTION_STRING, ersatzweise
# STORAGE_CONNECTION_STRING gelesen. Fehlen beide, wird nur die Dateiablage gesichert.
#
# Ergebnis je Lauf (<ts> = UTC-Zeitstempel):
#   <ts>.dump             pg_dump-Archiv; erst nach erfolgreicher Pruefung mit `pg_restore --list`
#                         unter diesem Namen abgelegt, bei Abbruch bleibt keine halbe Datei liegen
#   <ts>.dump.sha256      Pruefsumme im Format von sha256sum
#   <ts>-files.tgz(.sha256)  Dateiablage und Keyring (sofern vorhanden) samt Pruefsumme
#   <ts>.meta             Herkunft und Stand: Host, Datenbank, Schema, Server- und pg_dump-Version,
#                         App-Version (FLOWZER_APP_VERSION, ersatzweise FLOWZER_IMAGE_TAG),
#                         Migrationsstand und die absoluten Quellpfade der gesicherten Verzeichnisse.
#                         Wird zuletzt geschrieben; restore.sh liest sie.
#
# Verzeichnisse: FLOWZER_STORAGE_DIR und FLOWZER_KEYRING_DIR duerfen absolut sein; relative
# Angaben gelten relativ zur Repository-Wurzel (Standard .data/runtime-storage und
# .data/runtime-data-protection). Im Archiv stehen sie unter ihrem Verzeichnisnamen.
#
# Aufbewahrung: Die Skripte legen nur ab und loeschen nichts. Wie lange Sicherungen liegen
# bleiben, entscheidet die Aufbewahrungsregel der Installation (#325). Wichtig dabei: Eine
# Sicherung, die vor dem Ablauf einer Aufbewahrungsfrist entstanden ist, enthaelt die
# inzwischen geloeschten Vorgaenge weiterhin. Sicherungen unterliegen deshalb derselben
# Frist wie der Produktivbestand und sind am Ende der Frist zu vernichten.

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/runtime/pg-common.sh
source "${script_dir}/pg-common.sh"

repo_root="$(flowzer_repo_root)"
connection=''
schema="${STORAGE_SCHEMA:-flowzer}"
out_dir="${FLOWZER_BACKUP_DIR:-${repo_root}/backups}"
include_files=1
include_database=1

while [[ $# -gt 0 ]]; do
  case "$1" in
    --connection) connection="${2:-}"; shift 2 ;;
    --schema) schema="${2:-}"; shift 2 ;;
    --out) out_dir="${2:-}"; shift 2 ;;
    --no-files) include_files=0; shift ;;
    --files-only) include_database=0; shift ;;
    -h|--help) sed -n '3,36p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) flowzer_die "Unbekanntes Argument: $1" ;;
  esac
done

flowzer_validate_schema "$schema"

# Sicherungen enthalten Fachdaten; nur der Besitzer darf sie lesen.
umask 077
mkdir -p "$out_dir"
chmod 700 "$out_dir"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
dump_file="${out_dir}/${timestamp}.dump"
files_file="${out_dir}/${timestamp}-files.tgz"
meta_file="${out_dir}/${timestamp}.meta"

for existing in "$dump_file" "${dump_file}.sha256" "$files_file" "${files_file}.sha256" "$meta_file"; do
  [[ ! -e "$existing" ]] || flowzer_die "${existing} existiert bereits; eine Sekunde warten und erneut sichern."
done

# Halbfertige Dateien tragen die Endung .tmp und werden bei jedem Abbruch entfernt.
temp_files=()
cleanup() {
  local status=$?
  trap '' INT TERM
  local file
  for file in ${temp_files[@]+"${temp_files[@]}"}; do
    rm -f "$file"
  done
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

meta_lines=()
meta_add() {
  local key="$1"
  local value="$2"
  # Eine Zeile je Eintrag: Zeilenumbrueche in Werten wuerden die Datei unlesbar machen.
  value="${value//$'\n'/ }"
  meta_lines+=("${key}=${value}")
}

echo "Flowzer-Sicherung ${timestamp}"
echo "Ziel: ${out_dir}"

meta_add meta_format 2
meta_add created_at "$timestamp"
meta_add app_version "${FLOWZER_APP_VERSION:-${FLOWZER_IMAGE_TAG:-unknown}}"
meta_add schema "$schema"

database_state='uebersprungen'
if [[ "$include_database" -eq 1 ]]; then
  if conninfo="$(flowzer_resolve_connection "$connection")"; then
    flowzer_export_connection "$conninfo"
    echo "Datenbank: $(flowzer_describe_target "$schema")"

    server_version="$(flowzer_pg_scalar "SHOW server_version")" \
      || flowzer_die "Die Datenbank ist nicht erreichbar."
    history_state="$(flowzer_pg_scalar "SELECT (SELECT count(*) FROM pg_namespace WHERE nspname = '${schema}') || '/' || (to_regclass('\"${schema}\".schema_migrations') IS NOT NULL)")"
    [[ "${history_state%%/*}" == "1" ]] || flowzer_die "Schema ${schema} existiert in ${PGDATABASE} nicht."
    [[ "${history_state#*/}" == "true" ]] \
      || flowzer_die "In Schema ${schema} fehlt schema_migrations - das ist keine migrierte Flowzer-Ablage."
    migrations="$(flowzer_pg_scalar "SELECT count(*) || '/' || coalesce(max(version), 0) FROM \"${schema}\".schema_migrations")"
    migration_count="${migrations%%/*}"
    migration_max="${migrations#*/}"
    pg_dump_version="$(flowzer_pg_run pg_dump --version)"

    echo "Server: PostgreSQL ${server_version}; Werkzeug: ${pg_dump_version}"
    echo "Migrationsstand (Anzahl/hoechste Version): ${migrations}"
    echo "Schreibe ${dump_file} ..."
    temp_files+=("${dump_file}.tmp")
    flowzer_pg_run pg_dump --format=custom --no-owner --no-privileges --schema="$schema" >"${dump_file}.tmp"

    # Pruefung vor dem Ablegen: Das Archiv muss sich lesen lassen und die Migrationshistorie
    # samt Daten enthalten. Ein abgeschnittenes oder leeres Archiv wird hier sichtbar, nicht
    # erst beim Restore.
    toc="$(flowzer_pg_run pg_restore --list <"${dump_file}.tmp")" \
      || flowzer_die "pg_restore --list kann das neue Archiv nicht lesen; die Sicherung ist unbrauchbar."
    grep -q "^[0-9]*; [0-9]* [0-9]* TABLE ${schema} schema_migrations " <<<"$toc" \
      || flowzer_die "Das Archiv enthaelt keine Tabelle ${schema}.schema_migrations."
    grep -q "^[0-9]*; [0-9]* [0-9]* TABLE DATA ${schema} schema_migrations " <<<"$toc" \
      || flowzer_die "Das Archiv enthaelt keine Daten fuer ${schema}.schema_migrations."
    toc_entries="$(grep -c '^[0-9]' <<<"$toc")"

    mv "${dump_file}.tmp" "$dump_file"
    flowzer_write_checksum "$dump_file"
    database_state="$(du -h "$dump_file" | awk '{print $1}') in ${dump_file##*/}, ${toc_entries} Archiveintraege geprueft"

    meta_add database "$PGDATABASE"
    meta_add host "$PGHOST"
    meta_add port "$PGPORT"
    meta_add postgres_server_version "$server_version"
    meta_add pg_dump_version "$pg_dump_version"
    meta_add schema_migrations_count "$migration_count"
    meta_add schema_migrations_max "$migration_max"
    # Frueheres Format (Anzahl/hoechste Version); aeltere restore.sh-Staende lesen nur diesen Wert.
    meta_add migrations "$migrations"
    meta_add dump_file "${dump_file##*/}"
    meta_add dump_toc_entries "$toc_entries"
    echo "Datenbanksicherung fertig (${database_state})."
  else
    echo "Keine Verbindungsangabe gefunden - die Datenbank wird nicht gesichert." >&2
    echo "STORAGE_MIGRATION_CONNECTION_STRING setzen oder --connection uebergeben." >&2
    database_state='keine Verbindungsangabe'
  fi
fi

files_state='uebersprungen'
if [[ "$include_files" -eq 1 ]]; then
  tar_args=()
  members=()
  for kind in storage keyring; do
    if [[ "$kind" == storage ]]; then
      configured="${FLOWZER_STORAGE_DIR:-.data/runtime-storage}"
    else
      configured="${FLOWZER_KEYRING_DIR:-.data/runtime-data-protection}"
    fi
    [[ "$configured" == /* ]] || configured="${repo_root}/${configured}"
    [[ -d "$configured" ]] || continue

    source_dir="$(flowzer_absolute_dir "$configured" /)"
    [[ "$source_dir" != "/" ]] || flowzer_die "Das Wurzelverzeichnis / laesst sich nicht als ${kind} sichern."
    [[ "$source_dir" != *$'\n'* ]] || flowzer_die "Verzeichnisnamen mit Zeilenumbruch werden nicht unterstuetzt: ${source_dir}"
    member="${source_dir##*/}"
    for known in ${members[@]+"${members[@]}"}; do
      [[ "$known" != "$member" ]] \
        || flowzer_die "Dateiablage und Keyring heissen beide '${member}'; im Archiv waeren sie nicht zu unterscheiden."
    done
    members+=("$member")
    parent_dir="${source_dir%/*}"
    [[ -n "$parent_dir" ]] || parent_dir='/'
    tar_args+=(-C "$parent_dir" "$member")
    meta_add "files_${kind}_dir" "$source_dir"
    meta_add "files_${kind}_member" "$member"
  done

  if [[ "${#members[@]}" -gt 0 ]]; then
    echo "Schreibe ${files_file} ..."
    # Der Keyring gehoert zwingend in dieselbe Sicherung: ohne ihn verlieren nach einem
    # Restore alle BFF-Sitzungen, OIDC-Korrelationen und Antiforgery-Token ihre Gueltigkeit.
    temp_files+=("${files_file}.tmp")
    tar -czf "${files_file}.tmp" "${tar_args[@]}"
    tar -tzf "${files_file}.tmp" >/dev/null \
      || flowzer_die "Das neue Dateiarchiv laesst sich nicht lesen."
    mv "${files_file}.tmp" "$files_file"
    flowzer_write_checksum "$files_file"
    meta_add files_archive "${files_file##*/}"
    files_state="$(du -h "$files_file" | awk '{print $1}') in ${files_file##*/} (${members[*]})"
    echo "Dateisicherung fertig (${files_state})."
  else
    echo "Keine Dateiablage und kein Keyring gefunden - nichts zu sichern."
    files_state='nicht vorhanden'
  fi
fi

if [[ -f "$dump_file" || -f "$files_file" ]]; then
  temp_files+=("${meta_file}.tmp")
  printf '%s\n' "${meta_lines[@]}" >"${meta_file}.tmp"
  mv "${meta_file}.tmp" "$meta_file"
fi

echo
echo "Zusammenfassung"
echo "  Datenbank: ${database_state}"
echo "  Dateien:   ${files_state}"
if [[ -f "$meta_file" ]]; then
  echo "  Metadaten: ${meta_file##*/}"
fi
if [[ -f "$dump_file" ]]; then
  echo
  if [[ -f "$files_file" ]]; then
    echo "Wiederherstellung: scripts/runtime/restore.sh ${dump_file} --files ${files_file}"
  else
    echo "Wiederherstellung: scripts/runtime/restore.sh ${dump_file}"
  fi
fi
