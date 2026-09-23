#!/usr/bin/env bash
# Installations- und Auth-Abnahme (R1b): baut den isolierten Stack aus diesem Repository,
# startet ihn mit eigener Test-CA und eigenem Keycloak, führt die Playwright-Abnahme aus und
# räumt anschließend vollständig auf (inklusive Volumes).
#
#   tests/installation-auth/run.sh           Lauf mit Aufräumen
#   tests/installation-auth/run.sh --keep    Stack nach dem Lauf stehen lassen
#
# Umgebungsvariablen:
#   PLAYWRIGHT_SKIP_BROWSER_INSTALL=1   Chromium nicht selbst installieren (z. B. in der CI)
#   FLOWZER_INSTALLATION_AUTH_SUBNET    alternatives Compose-Netz innerhalb 172.16.0.0/12
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_NAME="flowzer-installation-auth"
COMPOSE=(docker compose -p "$PROJECT_NAME" -f "$SCRIPT_DIR/compose.yml")
LOG_DIR="$SCRIPT_DIR/logs"
KEEP=0

usage() {
  sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
}

for argument in "$@"; do
  case "$argument" in
    --keep) KEEP=1 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unbekannter Schalter: $argument" >&2; usage >&2; exit 64 ;;
  esac
done

for tool in docker node npm; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Voraussetzung fehlt: $tool" >&2
    exit 69
  fi
done

started_at=$(date +%s)

phase() {
  printf '\n==> %s (bisher %ss)\n' "$1" "$(($(date +%s) - started_at))"
}

# Testwerte aus den Env-Dateien (Client-Secrets, Datenbankpasswörter) werden vor dem Ablegen
# der Logs ersetzt. Es sind Testwerte, aber Logs sollen auch so keine Secrets enthalten.
redact() {
  local file="$1" value
  while IFS= read -r value; do
    [ -n "$value" ] || continue
    VALUE="$value" perl -pi -e 's/\Q$ENV{VALUE}\E/***/g' "$file"
  done < <(grep -hoE '(ClientSecret|Password)=[^;[:space:]]+' "$SCRIPT_DIR"/env/*.env | sed 's/^[^=]*=//' | sort -u)
}

collect_logs() {
  mkdir -p "$LOG_DIR"
  echo "Sammle Diagnose nach $LOG_DIR ..."
  "${COMPOSE[@]}" ps -a > "$LOG_DIR/compose-ps.txt" 2>&1 || true
  "${COMPOSE[@]}" logs --no-color --timestamps > "$LOG_DIR/compose.log" 2>&1 || true
  redact "$LOG_DIR/compose.log" || true
}

finish() {
  local status=$?
  if [ "$status" -ne 0 ]; then
    collect_logs
  fi

  if [ "$KEEP" -eq 1 ]; then
    echo "Stack bleibt stehen (--keep). Aufräumen: ${COMPOSE[*]} down -v --remove-orphans"
  else
    phase "Aufräumen"
    "${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
  fi

  local ended_at
  ended_at=$(date +%s)
  if [ "$status" -eq 0 ]; then
    printf '\nInstallations- und Auth-Abnahme bestanden (%ss).\n' "$((ended_at - started_at))"
  else
    printf '\nInstallations- und Auth-Abnahme fehlgeschlagen (Exit %s, %ss).\n' "$status" "$((ended_at - started_at))" >&2
  fi
  exit "$status"
}
trap finish EXIT

rm -rf "$LOG_DIR"

phase "Testläufer vorbereiten"
if [ ! -d "$SCRIPT_DIR/node_modules" ] || [ "$SCRIPT_DIR/package-lock.json" -nt "$SCRIPT_DIR/node_modules/.package-lock.json" ]; then
  npm --prefix "$SCRIPT_DIR" ci --no-audit --no-fund
fi
if [ "${PLAYWRIGHT_SKIP_BROWSER_INSTALL:-0}" != "1" ]; then
  (cd "$SCRIPT_DIR" && npx playwright install chromium)
fi

phase "Reste eines früheren Laufs entfernen"
"${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1 || true

phase "Stack bauen und starten"
"${COMPOSE[@]}" up -d --build --wait --wait-timeout 600

phase "Versionen"
"${COMPOSE[@]}" images
"${COMPOSE[@]}" ps -a --format 'table {{.Service}}\t{{.Status}}'

phase "Abnahme ausführen (check-config, Golden Path, Negativfälle, Verzeichnis, Neustart)"
(cd "$SCRIPT_DIR" && npx playwright test)
