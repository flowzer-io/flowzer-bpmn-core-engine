#!/usr/bin/env bash
set -euo pipefail

# Spielt eine mit scripts/runtime/backup.sh erzeugte Sicherung zurueck und prueft danach den
# Migrationsstand.
#
# Aufruf:
#   scripts/runtime/restore.sh <sicherung.dump> [--connection <conninfo>] [--schema <name>]
#                              [--files <archiv.tgz>] [--force]
#
# Regeln:
#   * Der Stack muss gestoppt sein. Ein Restore unter laufender Schreiblast ist kein Restore.
#   * Das Ziel muss leer sein: Ist das Schema bereits mit Tabellen belegt, bricht das Skript ab.
#     --force loescht das Zielschema vorher vollstaendig (CASCADE) - bewusst und nur manuell.
#   * Zugangsdaten reisen ueber Umgebungsvariablen beziehungsweise ~/.pgpass, nie ueber die
#     Kommandozeile.
#   * Nach dem Restore wird der Migrationsstand gemeldet. Stammt die Sicherung von einem
#     aelteren Stand, fehlen Migrationen; sie werden mit `dotnet WebApiEngine.dll --migrate`
#     nachgezogen und mit `--check-config` bestaetigt.

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/runtime/pg-common.sh
source "${script_dir}/pg-common.sh"

repo_root="$(flowzer_repo_root)"
dump_file=''
files_archive=''
connection=''
schema="${STORAGE_SCHEMA:-flowzer}"
force=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --connection) connection="${2:-}"; shift 2 ;;
    --schema) schema="${2:-}"; shift 2 ;;
    --files) files_archive="${2:-}"; shift 2 ;;
    --force) force=1; shift ;;
    -h|--help) sed -n '3,22p' "${BASH_SOURCE[0]}"; exit 0 ;;
    -*) flowzer_die "Unbekanntes Argument: $1" ;;
    *) dump_file="$1"; shift ;;
  esac
done

[[ -n "$schema" ]] || flowzer_die "--schema darf nicht leer sein."
[[ -n "$dump_file" ]] || flowzer_die "Bitte die zurueckzuspielende Sicherung angeben (scripts/runtime/restore.sh <datei.dump>)."
[[ -f "$dump_file" ]] || flowzer_die "Sicherung ${dump_file} nicht gefunden."

conninfo="$(flowzer_resolve_connection "$connection")" \
  || flowzer_die "Keine Verbindungsangabe. STORAGE_MIGRATION_CONNECTION_STRING setzen oder --connection uebergeben."
flowzer_export_connection "$conninfo"

echo "Flowzer-Restore"
echo "Sicherung: ${dump_file}"
echo "Ziel:      $(flowzer_describe_target "$schema")"

reachable="$(flowzer_pg_scalar 'SELECT 1')"
[[ "$reachable" == "1" ]] || flowzer_die "Die Zieldatenbank ist nicht erreichbar."

existing_tables="$(flowzer_pg_scalar "SELECT count(*) FROM information_schema.tables WHERE table_schema = '${schema}'")"
[[ -n "$existing_tables" ]] || existing_tables=0

if [[ "$existing_tables" -gt 0 ]]; then
  if [[ "$force" -ne 1 ]]; then
    flowzer_die "Schema ${schema} enthaelt bereits ${existing_tables} Tabelle(n). Restore laeuft nur in eine leere Datenbank; mit --force wird das Zielschema vorher verworfen."
  fi
  echo "Verwerfe Schema ${schema} (--force) ..."
  flowzer_pg_run psql --quiet --no-psqlrc --set=ON_ERROR_STOP=1 \
    --command "DROP SCHEMA IF EXISTS \"${schema}\" CASCADE" >/dev/null
fi

echo "Spiele die Datenbanksicherung ein ..."
# --exit-on-error: ein halb eingespielter Bestand ist schlimmer als ein sichtbarer Abbruch.
flowzer_pg_run pg_restore --dbname "$PGDATABASE" --no-owner --no-privileges --exit-on-error <"$dump_file"

applied="$(flowzer_pg_scalar "SELECT count(*) FROM \"${schema}\".schema_migrations")"
highest="$(flowzer_pg_scalar "SELECT coalesce(max(version), 0) FROM \"${schema}\".schema_migrations")"
tables="$(flowzer_pg_scalar "SELECT count(*) FROM information_schema.tables WHERE table_schema = '${schema}'")"

if [[ -z "$applied" || "$applied" == "0" ]]; then
  echo
  echo "Warnung: In ${schema}.schema_migrations steht kein Eintrag. Entweder stammt die" >&2
  echo "Sicherung aus einer anderen Ablage oder das Schema wurde nicht vollstaendig" >&2
  echo "eingespielt. Den Bestand pruefen, bevor die API gestartet wird." >&2
  exit 1
fi

echo
echo "Wiederhergestellt: ${tables} Tabelle(n) in Schema ${schema}."
echo "Migrationsstand:   ${applied} angewendet, hoechste Version ${highest}."

if [[ -n "$files_archive" ]]; then
  [[ -f "$files_archive" ]] || flowzer_die "Dateisicherung ${files_archive} nicht gefunden."
  echo "Entpacke ${files_archive} nach ${repo_root} ..."
  tar -xzf "$files_archive" -C "$repo_root"
  echo "Dateiablage und Keyring wiederhergestellt."
fi

echo
echo "Naechste Schritte:"
echo "  1. dotnet WebApiEngine.dll --migrate       # fehlende Migrationen nachziehen"
echo "  2. dotnet WebApiEngine.dll --check-config  # Migrationsstand und Erreichbarkeit bestaetigen"
echo "  3. Stack starten und /health/ready pruefen"
