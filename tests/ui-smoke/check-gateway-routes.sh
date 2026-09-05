#!/usr/bin/env bash
set -euo pipefail

# Vergleicht die Weiterleitungsliste des Gateways mit den tatsaechlichen API-Routen.
# Fehlt eine Route, beantwortet die Oberflaeche sie mit ihrer Startseite: Der Aufruf
# bekommt 200 und niemals die erwartete Antwort. Genau so ist /job zuerst durchgerutscht.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
entrypoint="$repo_root/deploy/frontend/entrypoint.sh"

proxied="$(grep -o 'location ~ \^/([^)]*)' "$entrypoint" | sed 's|.*(\(.*\))|\1|' | tr '|' '\n' | sort -u)"

# Die Routen der Controller stehen in ihren Route-Attributen.
declare -a missing=()
while read -r route; do
  [ -n "$route" ] || continue
  if ! grep -qx "$route" <<<"$proxied"; then
    missing+=("$route")
  fi
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
  if grep -q 'Route("\[controller\]")' "$repo_root/src/WebApiEngine/Controller/$name.cs" \
     && ! grep -qx "$route" <<<"$proxied"; then
    missing+=("$route")
  fi
done < <(cd "$repo_root/src/WebApiEngine/Controller" && ls *.cs | sed 's|\.cs$||')

if [ "${#missing[@]}" -gt 0 ]; then
  printf 'Diese API-Routen leitet das Gateway nicht weiter: %s\n' "${missing[*]}" >&2
  printf 'Ergaenze sie in deploy/frontend/entrypoint.sh.\n' >&2
  exit 1
fi

printf 'OK: Das Gateway leitet alle API-Routen weiter.\n'
