#!/usr/bin/env bash
# Gemeinsame Hilfsfunktionen fuer backup.sh und restore.sh.
#
# Wird mit `source` eingebunden und nie direkt aufgerufen. Grundregel: Zugangsdaten stehen
# ausschliesslich in Umgebungsvariablen (PGHOST, PGPORT, PGUSER, PGDATABASE, PGPASSWORD) oder
# in ~/.pgpass. Es landet nie ein Passwort in einer Kommandozeile und damit auch nicht in der
# Prozessliste des Hosts.

flowzer_repo_root() {
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  (cd "${script_dir}/../.." && pwd)
}

flowzer_die() {
  echo "Fehler: $*" >&2
  exit 1
}

# Liest die Verbindungsangabe aus dem Argument oder aus den Betriebsvariablen. Bevorzugt wird
# der Migrationszugang: Er besitzt das Schema und darf es vollstaendig lesen und wiederherstellen.
flowzer_resolve_connection() {
  local explicit="${1:-}"
  if [[ -n "$explicit" ]]; then
    printf '%s' "$explicit"
    return 0
  fi
  if [[ -n "${STORAGE_MIGRATION_CONNECTION_STRING:-}" ]]; then
    printf '%s' "$STORAGE_MIGRATION_CONNECTION_STRING"
    return 0
  fi
  if [[ -n "${STORAGE_CONNECTION_STRING:-}" ]]; then
    printf '%s' "$STORAGE_CONNECTION_STRING"
    return 0
  fi
  return 1
}

# Zerlegt eine Verbindungsangabe und exportiert die libpq-Variablen. Unterstuetzt beide
# Schreibweisen, die im Betrieb vorkommen:
#   ADO.NET:  Host=db;Port=5432;Database=flowzer;Username=app;Password=...
#   URI:      postgresql://app:...@db:5432/flowzer
flowzer_export_connection() {
  local conninfo="$1"
  local host='' port='' database='' user='' password=''

  if [[ "$conninfo" == postgres://* || "$conninfo" == postgresql://* ]]; then
    local rest="${conninfo#*://}"
    rest="${rest%%\?*}"
    local authority="$rest"
    local credentials=''
    if [[ "$rest" == *@* ]]; then
      credentials="${rest%%@*}"
      authority="${rest#*@}"
    fi
    if [[ -n "$credentials" ]]; then
      user="${credentials%%:*}"
      [[ "$credentials" == *:* ]] && password="${credentials#*:}"
    fi
    database="${authority#*/}"
    authority="${authority%%/*}"
    host="${authority%%:*}"
    [[ "$authority" == *:* ]] && port="${authority##*:}"
  else
    local pair key value
    local remainder="$conninfo"
    while [[ -n "$remainder" ]]; do
      pair="${remainder%%;*}"
      if [[ "$remainder" == *\;* ]]; then
        remainder="${remainder#*;}"
      else
        remainder=''
      fi
      [[ -z "$pair" ]] && continue
      key="${pair%%=*}"
      value="${pair#*=}"
      # Fuehrende/abschliessende Leerzeichen entfernen, Schluessel normalisieren.
      key="$(printf '%s' "$key" | tr -d ' ' | tr '[:upper:]' '[:lower:]')"
      value="${value#"${value%%[![:space:]]*}"}"
      value="${value%"${value##*[![:space:]]}"}"
      case "$key" in
        host|server|datasource) host="$value" ;;
        port) port="$value" ;;
        database|initialcatalog) database="$value" ;;
        username|userid|user) user="$value" ;;
        password|pwd) password="$value" ;;
        *) ;;
      esac
    done
  fi

  [[ -n "$host" ]] || flowzer_die "Die Verbindungsangabe nennt keinen Host."
  [[ -n "$database" ]] || flowzer_die "Die Verbindungsangabe nennt keine Datenbank."

  export PGHOST="$host"
  export PGPORT="${port:-5432}"
  export PGDATABASE="$database"
  [[ -n "$user" ]] && export PGUSER="$user"
  # PGPASSWORD nur setzen, wenn die Verbindungsangabe eines enthaelt. Sonst bleibt ~/.pgpass
  # beziehungsweise ein bereits gesetztes PGPASSWORD die Quelle.
  [[ -n "$password" ]] && export PGPASSWORD="$password"
  export PGCONNECT_TIMEOUT="${PGCONNECT_TIMEOUT:-10}"
  return 0
}

# Fuehrt ein libpq-Werkzeug aus: bevorzugt lokal, sonst in einem Wegwerf-Container. Alle
# Zugangsdaten reist ueber die Umgebung; `-e PGPASSWORD` gibt nur den Namen weiter, nie den Wert.
flowzer_pg_run() {
  local tool="$1"
  shift
  if command -v "$tool" >/dev/null 2>&1; then
    "$tool" "$@"
    return $?
  fi

  command -v docker >/dev/null 2>&1 \
    || flowzer_die "Weder ${tool} noch docker sind verfuegbar. PostgreSQL-Clientwerkzeuge installieren oder FLOWZER_PG_IMAGE nutzbar machen."

  local image="${FLOWZER_PG_IMAGE:-postgres:17-alpine}"
  local network_args=()
  [[ -n "${FLOWZER_PG_DOCKER_NETWORK:-}" ]] && network_args=(--network "${FLOWZER_PG_DOCKER_NETWORK}")

  docker run --rm --interactive \
    "${network_args[@]}" \
    --env PGHOST --env PGPORT --env PGDATABASE --env PGUSER --env PGPASSWORD --env PGCONNECT_TIMEOUT \
    "$image" "$tool" "$@"
}

# Einzelwert aus der Datenbank lesen; leere Ausgabe, wenn die Abfrage scheitert.
flowzer_pg_scalar() {
  local statement="$1"
  flowzer_pg_run psql --no-align --tuples-only --quiet --no-psqlrc --command "$statement" 2>/dev/null | tr -d '[:space:]'
}

flowzer_describe_target() {
  printf '%s@%s:%s/%s Schema %s' "${PGUSER:-<voreingestellt>}" "$PGHOST" "$PGPORT" "$PGDATABASE" "$1"
}
