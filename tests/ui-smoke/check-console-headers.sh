#!/usr/bin/env bash
set -euo pipefail

# Startet die nginx-Konfiguration der Konsole in genau dem Basisimage, das Dockerfile.console
# verwendet, und prueft die Antwortkopfzeilen — fuer beide Varianten des Entrypoints (mit und
# ohne API-Weiterleitung).
#
# Testzweck: Nach einem Release sahen Anwender weiter die alte Oberflaeche, bis sie hart neu
# luden. index.html trug keine Cache-Anweisung; der Browser durfte sie heuristisch
# wiederverwenden und lud damit die alten Bundles. Zugleich fehlten auf config.json die
# Sicherheitskopfzeilen, weil ein add_header in einem location-Block alle add_header der
# server-Ebene aufhebt.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
image="$(sed -n 's/^FROM \(nginx:[^ ]*\) AS runtime$/\1/p' "$repo_root/Dockerfile.console")"
[ -n "$image" ] || { echo "Basisimage der Konsole nicht gefunden." >&2; exit 1; }

work="$(mktemp -d)"
# Ein eigenes Netz bringt den eingebetteten Docker-DNS (127.0.0.11) mit, den der Entrypoint
# als Resolver eintraegt; ohne ihn liefe jede Weiterleitung erst in den Resolver-Timeout.
network="flowzer-console-headers-$$"
docker network create "$network" >/dev/null
containers=()
cleanup() {
  for container in "${containers[@]}"; do docker rm -f "$container" >/dev/null 2>&1 || true; done
  docker network rm "$network" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

failures=()
fail() { failures+=("$1"); }

# Kopfzeilen einer Adresse ohne Zeilenende-Zeichen; verglichen wird ohne Gross/Klein.
headers_of() {
  curl -sS -D - -o /dev/null "http://127.0.0.1:$1$2" | tr -d '\r'
}

expect_header() {
  local variant="$1" port="$2" path="$3" expected="$4"
  local headers
  headers="$(headers_of "$port" "$path")"
  grep -qixF "$expected" <<<"$headers" \
    || fail "$variant $path: erwartet '$expected', erhalten: $(tr '\n' ' ' <<<"$headers")"
}

expect_no_header() {
  local variant="$1" port="$2" path="$3" name="$4"
  if headers_of "$port" "$path" | grep -qi "^$name:"; then
    fail "$variant $path: '$name' darf hier nicht gesetzt sein"
  fi
}

expect_status() {
  local variant="$1" port="$2" path="$3" expected="$4"
  local status
  status="$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port$path")"
  [ "$status" = "$expected" ] || fail "$variant $path: Status $status statt $expected"
}

check_variant() {
  local variant="$1"; shift
  local html="$work/$variant"
  mkdir -p "$html/assets"
  printf '<!doctype html><title>Flowzer</title>' >"$html/index.html"
  printf 'console.log(1)' >"$html/assets/index-abc123.js"

  local container
  container="$(docker run -d --network "$network" -p 127.0.0.1::8080 "$@" \
    -v "$repo_root/deploy/console/entrypoint.sh:/entrypoint.sh:ro" \
    -v "$html:/usr/share/nginx/html" \
    --entrypoint /bin/sh "$image" /entrypoint.sh)"
  containers+=("$container")
  local port
  port="$(docker port "$container" 8080/tcp | head -n1 | sed 's/.*://')"

  local ready=false
  for _ in $(seq 1 50); do
    if curl -fsS -o /dev/null "http://127.0.0.1:$port/index.html" 2>/dev/null; then ready=true; break; fi
    sleep 0.2
  done
  if [ "$ready" != true ]; then
    fail "$variant: nginx startet nicht: $(docker logs "$container" 2>&1 | tail -n5)"
    return
  fi

  # Einstieg und Router-Adressen: Der Browser muss vor jeder Verwendung nachfragen, damit
  # nach einem Release sofort die neuen Bundles geladen werden.
  for path in / /index.html /workflows/abc; do
    expect_header "$variant" "$port" "$path" 'cache-control: no-cache'
  done

  # Gebaute Bundles tragen ihren Inhalt im Dateinamen und aendern sich nie.
  expect_header "$variant" "$port" /assets/index-abc123.js 'cache-control: public, max-age=31536000, immutable'
  # Ein fehlendes Bundle ist ein 404 und nicht die Startseite — sonst laege HTML ein Jahr
  # lang unter einer Skriptadresse im Cache.
  expect_status "$variant" "$port" /assets/fehlt-xyz.js 404
  expect_no_header "$variant" "$port" /assets/fehlt-xyz.js 'cache-control'

  expect_header "$variant" "$port" /config.json 'cache-control: no-store'

  for path in / /workflows/abc /assets/index-abc123.js /config.json; do
    expect_header "$variant" "$port" "$path" 'x-frame-options: DENY'
    expect_header "$variant" "$port" "$path" 'x-content-type-options: nosniff'
    expect_header "$variant" "$port" "$path" 'referrer-policy: strict-origin-when-cross-origin'
  done
}

check_variant ohne-weiterleitung
check_variant mit-weiterleitung -e FLOWZER_API_UPSTREAM=api:8080

# Die Weiterleitung selbst muss die Sicherheitskopfzeilen ebenfalls tragen; die API ist hier
# nicht erreichbar, nginx antwortet deshalb mit einem Fehler, der sie trotzdem enthaelt.
proxy_port="$(docker port "${containers[1]}" 8080/tcp | head -n1 | sed 's/.*://')"
expect_header mit-weiterleitung "$proxy_port" /health/ready 'x-frame-options: DENY'
expect_no_header mit-weiterleitung "$proxy_port" /health/ready 'cache-control'

if [ "${#failures[@]}" -gt 0 ]; then
  printf 'Kopfzeilen der Konsole stimmen nicht:\n' >&2
  printf '  - %s\n' "${failures[@]}" >&2
  exit 1
fi

printf 'OK: Konsole liefert Cache- und Sicherheitskopfzeilen wie erwartet.\n'
