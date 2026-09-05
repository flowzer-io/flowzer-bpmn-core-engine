#!/usr/bin/env bash
set -euo pipefail

# Vergleicht die Weiterleitungsliste des Gateways mit den tatsaechlichen API-Routen.
# Fehlt eine Route, beantwortet die Oberflaeche sie mit ihrer Startseite: Der Aufruf
# bekommt 200 und niemals die erwartete Antwort. Genau so ist /job zuerst durchgerutscht.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Beide Oberflaechen liefern die API unter ihrer eigenen Adresse aus; beide Listen
# muessen vollstaendig sein.
declare -a entrypoints=(
  "$repo_root/deploy/frontend/entrypoint.sh"
  "$repo_root/deploy/console/entrypoint.sh"
)

# Eine Route gilt als weitergeleitet, wenn sie in einer Alternativenliste steht
# (`^/(a|b|c)(/|$)`) oder als eigener Ort mit Unterpfad (`^/route/`). Die zweite Form
# braucht es dort, wo die Oberflaeche selbst eine Seite gleichen Namens hat.
route_is_proxied() {
  local entrypoint="$1"
  local route="$2"

  local alternatives
  alternatives="$(grep -o 'location ~ \^/([^)]*)' "$entrypoint" | sed 's|.*(\(.*\))|\1|' | tr '|' '\n' | sort -u)"
  grep -qx "$route" <<<"$alternatives" && return 0

  grep -q "location ~ \^/$route/" "$entrypoint"
}

# Die Routen der Controller stehen in ihren Route-Attributen.
declare -a missing=()
while read -r route; do
  [ -n "$route" ] || continue
  for entrypoint in "${entrypoints[@]}"; do
    if ! route_is_proxied "$entrypoint" "$route"; then
      missing+=("$(basename "$(dirname "$entrypoint")"): $route")
    fi
  done
done < <(
  grep -rho 'Route("[^"]*")' "$repo_root/src/WebApiEngine/Controller" \
    | sed 's|Route("\(.*\)")|\1|' \
    | sed 's|\[controller\]||' \
    | tr '[:upper:]' '[:lower:]' \
    | grep -v '^$' \
    | sort -u
)

# Controller mit [controller]-Platzhalter tragen ihren Namen als Route.
while read -r name; do
  route="$(tr '[:upper:]' '[:lower:]' <<<"${name%Controller}")"
  grep -q 'Route("\[controller\]")' "$repo_root/src/WebApiEngine/Controller/$name.cs" || continue
  for entrypoint in "${entrypoints[@]}"; do
    if ! route_is_proxied "$entrypoint" "$route"; then
      missing+=("$(basename "$(dirname "$entrypoint")"): $route")
    fi
  done
done < <(cd "$repo_root/src/WebApiEngine/Controller" && ls *.cs | sed 's|\.cs$||')

if [ "${#missing[@]}" -gt 0 ]; then
  printf 'Diese API-Routen leitet das Gateway nicht weiter: %s\n' "${missing[*]}" >&2
  printf 'Ergaenze sie im jeweiligen entrypoint.sh unter deploy/.\n' >&2
  exit 1
fi

# In einem nicht in Anfuehrungszeichen gesetzten Heredoc fuehrt die Shell Rueckwaerts-
# anfuehrungszeichen und $(...) aus. Ein Kommentar mit Backticks landete so als
# Fehlermeldung statt in der Konfiguration.
for entrypoint in "${entrypoints[@]}"; do
  if grep -q '`' "$entrypoint"; then
    printf 'In %s stehen Rueckwaertsanfuehrungszeichen; im Heredoc wuerden sie ausgefuehrt.\n' "$entrypoint" >&2
    exit 1
  fi
done

printf 'OK: Das Gateway leitet alle API-Routen weiter.\n'
