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
#
# FLOWZER_PG_CLIENT waehlt den Weg ausdruecklich:
#   auto    (Standard) lokales Werkzeug, falls vorhanden, sonst Container
#   local   nur das lokale Werkzeug
#   docker  immer der Container (FLOWZER_PG_IMAGE). Noetig, wenn die lokalen Clientwerkzeuge
#           aelter sind als der Server: pg_dump verweigert einen neueren Server.
flowzer_pg_run() {
  local tool="$1"
  shift
  local client="${FLOWZER_PG_CLIENT:-auto}"
  case "$client" in
    auto|local|docker) ;;
    *) flowzer_die "FLOWZER_PG_CLIENT muss auto, local oder docker sein (ist: ${client})." ;;
  esac

  if [[ "$client" != "docker" ]] && command -v "$tool" >/dev/null 2>&1; then
    "$tool" "$@"
    return $?
  fi
  [[ "$client" != "local" ]] \
    || flowzer_die "${tool} ist lokal nicht verfuegbar (FLOWZER_PG_CLIENT=local)."

  command -v docker >/dev/null 2>&1 \
    || flowzer_die "Weder ${tool} noch docker sind verfuegbar. PostgreSQL-Clientwerkzeuge installieren oder FLOWZER_PG_IMAGE nutzbar machen."

  local image="${FLOWZER_PG_IMAGE:-postgres:17-alpine}"
  local network_args=()
  [[ -n "${FLOWZER_PG_DOCKER_NETWORK:-}" ]] && network_args=(--network "${FLOWZER_PG_DOCKER_NETWORK}")

  docker run --rm --interactive \
    ${network_args[@]+"${network_args[@]}"} \
    --env PGHOST --env PGPORT --env PGDATABASE --env PGUSER --env PGPASSWORD --env PGCONNECT_TIMEOUT \
    --env PGOPTIONS \
    "$image" "$tool" "$@"
}

# Einzelwert aus der Datenbank lesen. Scheitert die Abfrage, erscheint die Meldung von psql auf
# stderr und die Funktion endet mit einem Fehlercode - ein leerer Wert ist nie ein stiller
# Ersatz fuer einen Fehler. Entfernt werden nur fuehrende und abschliessende Leerzeichen.
flowzer_pg_scalar() {
  local statement="$1"
  local output
  if ! output="$(flowzer_pg_run psql --no-align --tuples-only --quiet --no-psqlrc \
      --set=ON_ERROR_STOP=1 --command "$statement")"; then
    echo "Fehler: Datenbankabfrage gescheitert: ${statement}" >&2
    return 1
  fi
  output="${output#"${output%%[![:space:]]*}"}"
  output="${output%"${output##*[![:space:]]}"}"
  printf '%s' "$output"
}

# Schemanamen wie die API pruefen (PostgreSqlStorageOptions.Validate): Kleinbuchstaben, Ziffern
# und Unterstrich, Beginn mit Buchstabe oder Unterstrich, hoechstens 63 Zeichen, kein pg_-Praefix.
# Die Skripte setzen den Namen in SQL ein; die Pruefung schliesst Quoting-Fehler aus.
flowzer_validate_schema() {
  local name="$1"
  # Zeichen ausdruecklich aufgezaehlt: Bereiche wie [a-z] haengen in manchen Locales an der
  # Sortierfolge und passen dann auch auf Grossbuchstaben.
  local pattern='^[abcdefghijklmnopqrstuvwxyz_][abcdefghijklmnopqrstuvwxyz0123456789_]{0,62}$'
  [[ "$name" =~ $pattern && "$name" != pg_* ]] \
    || flowzer_die "Ungueltiger Schemaname '${name}': erlaubt sind Kleinbuchstaben, Ziffern und Unterstrich (Beginn mit Buchstabe oder Unterstrich, hoechstens 63 Zeichen, kein pg_-Praefix)."
}

# SHA-256 einer Datei als Hex-Zeichenkette; sha256sum (Linux) oder shasum (macOS).
flowzer_sha256() {
  local file="$1"
  local line
  if command -v sha256sum >/dev/null 2>&1; then
    line="$(sha256sum "$file")"
  elif command -v shasum >/dev/null 2>&1; then
    line="$(shasum -a 256 "$file")"
  else
    flowzer_die "Weder sha256sum noch shasum sind verfuegbar."
  fi
  printf '%s' "${line%% *}"
}

# Schreibt <datei>.sha256 im Format von `sha256sum` (Hash, zwei Leerzeichen, Dateiname ohne
# Pfad). So laesst sich die Pruefsumme im Sicherungsverzeichnis auch von Hand mit
# `sha256sum -c` beziehungsweise `shasum -a 256 -c` nachrechnen.
flowzer_write_checksum() {
  local file="$1"
  local digest
  digest="$(flowzer_sha256 "$file")"
  printf '%s  %s\n' "$digest" "${file##*/}" >"${file}.sha256"
}

# Prueft <datei> gegen <datei>.sha256. Rueckgabe: 0 passt, 1 weicht ab, 2 keine Pruefsummendatei.
flowzer_verify_checksum() {
  local file="$1"
  local checksum_file="${file}.sha256"
  [[ -f "$checksum_file" ]] || return 2
  local expected actual
  expected="$(head -n 1 "$checksum_file")"
  expected="${expected%% *}"
  actual="$(flowzer_sha256 "$file")"
  [[ -n "$expected" && "$expected" == "$actual" ]]
}

# Liest einen Wert aus einer .meta-Datei (Zeilen `schluessel=wert`). Die Datei wird bewusst nicht
# mit `source` eingelesen: Sie ist Datenbestand, kein Programmcode. Fehlt der Schluessel, ist die
# Ausgabe leer.
flowzer_meta_get() {
  local file="$1"
  local key="$2"
  local line
  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ "$line" == "${key}="* ]]; then
      printf '%s' "${line#*=}"
      return 0
    fi
  done <"$file"
  return 0
}

# Absoluter Pfad eines Verzeichnisses. Relative Angaben gelten wie bisher relativ zur
# Repository-Wurzel (dort liegt .data/ des Runtime-Stacks), nicht relativ zum Aufrufort.
flowzer_absolute_dir() {
  local dir="$1"
  local base="$2"
  [[ "$dir" == /* ]] || dir="${base}/${dir}"
  (CDPATH='' cd "$dir" && pwd)
}

flowzer_describe_target() {
  printf '%s@%s:%s/%s Schema %s' "${PGUSER:-<voreingestellt>}" "$PGHOST" "$PGPORT" "$PGDATABASE" "$1"
}
