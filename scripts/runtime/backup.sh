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
    -h|--help) sed -n '3,25p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) flowzer_die "Unbekanntes Argument: $1" ;;
  esac
done

[[ -n "$schema" ]] || flowzer_die "--schema darf nicht leer sein."

mkdir -p "$out_dir"
# Sicherungen enthalten Fachdaten; nur der Besitzer darf sie lesen.
chmod 700 "$out_dir"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
dump_file="${out_dir}/${timestamp}.dump"
files_file="${out_dir}/${timestamp}-files.tgz"
meta_file="${out_dir}/${timestamp}.meta"

echo "Flowzer-Sicherung ${timestamp}"
echo "Ziel: ${out_dir}"

database_state='uebersprungen'
if [[ "$include_database" -eq 1 ]]; then
  if conninfo="$(flowzer_resolve_connection "$connection")"; then
    flowzer_export_connection "$conninfo"
    echo "Datenbank: $(flowzer_describe_target "$schema")"

    migrations="$(flowzer_pg_scalar "SELECT count(*) || '/' || coalesce(max(version), 0) FROM \"${schema}\".schema_migrations" || true)"
    [[ -n "$migrations" ]] || migrations='unbekannt'

    echo "Migrationsstand (Anzahl/hoechste Version): ${migrations}"
    echo "Schreibe ${dump_file} ..."
    umask 077
    flowzer_pg_run pg_dump --format=custom --no-owner --no-privileges --schema="$schema" >"$dump_file"
    database_state="$(du -h "$dump_file" | cut -f1) in ${dump_file##*/}"
    printf 'created_at=%s\ndatabase=%s\nhost=%s\nschema=%s\nmigrations=%s\n' \
      "$timestamp" "$PGDATABASE" "$PGHOST" "$schema" "$migrations" >"$meta_file"
    echo "Datenbanksicherung fertig (${database_state})."
  else
    echo "Keine Verbindungsangabe gefunden - die Datenbank wird nicht gesichert." >&2
    echo "STORAGE_MIGRATION_CONNECTION_STRING setzen oder --connection uebergeben." >&2
    database_state='keine Verbindungsangabe'
  fi
fi

files_state='uebersprungen'
if [[ "$include_files" -eq 1 ]]; then
  storage_dir="${FLOWZER_STORAGE_DIR:-${repo_root}/.data/runtime-storage}"
  keyring_dir="${FLOWZER_KEYRING_DIR:-${repo_root}/.data/runtime-data-protection}"
  sources=()
  [[ -d "$storage_dir" ]] && sources+=("${storage_dir#"${repo_root}"/}")
  [[ -d "$keyring_dir" ]] && sources+=("${keyring_dir#"${repo_root}"/}")

  if [[ "${#sources[@]}" -gt 0 ]]; then
    echo "Schreibe ${files_file} ..."
    # Der Keyring gehoert zwingend in dieselbe Sicherung: ohne ihn verlieren nach einem
    # Restore alle BFF-Sitzungen, OIDC-Korrelationen und Antiforgery-Token ihre Gueltigkeit.
    umask 077
    tar -czf "$files_file" -C "$repo_root" "${sources[@]}"
    files_state="$(du -h "$files_file" | cut -f1) in ${files_file##*/}"
    echo "Dateisicherung fertig (${files_state})."
  else
    echo "Keine Dateiablage und kein Keyring gefunden - nichts zu sichern."
    files_state='nicht vorhanden'
  fi
fi

echo
echo "Zusammenfassung"
echo "  Datenbank: ${database_state}"
echo "  Dateien:   ${files_state}"
echo
echo "Wiederherstellung: scripts/runtime/restore.sh ${dump_file}"
