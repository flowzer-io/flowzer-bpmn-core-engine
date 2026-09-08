#!/usr/bin/env bash
set -euo pipefail

# Vergleicht die Weiterleitungsliste des Gateways mit den tatsaechlichen API-Routen.
# Fehlt eine Route, beantwortet die Oberflaeche sie mit ihrer Startseite: Der Aufruf
# bekommt 200 und niemals die erwartete Antwort. Genau so ist /job zuerst durchgerutscht.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Die Konsole liefert die API unter ihrer eigenen Adresse aus; ihre Liste muss
# vollstaendig sein.
declare -a entrypoints=(
  "$repo_root/deploy/console/entrypoint.sh"
)

# Eine Route gilt als weitergeleitet, wenn sie in einer Alternativenliste steht
# (`^/(a|b|c)(/|$)`) oder als eigener Ort mit Unterpfad (`^/route/`). Die zweite Form
# braucht es dort, wo die Oberflaeche selbst eine Seite gleichen Namens hat.
#
# Die Routen werden hier klein geschrieben verglichen. Das ist nur zulaessig, solange die
# Regeln im Entrypoint case-insensitiv sind (`location ~*`); die naechste Pruefung stellt
# das sicher.
route_is_proxied() {
  local entrypoint="$1"
  local route="$2"

  local alternatives
  alternatives="$(grep -o 'location ~\*\? \^/([^)]*)' "$entrypoint" | sed 's|.*(\(.*\))|\1|' | tr '|' '\n' | sort -u)"
  grep -qx "$route" <<<"$alternatives" && return 0

  grep -q "location ~\*\? \^/$route/" "$entrypoint"
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

# ASP.NET Core routet unabhaengig von Gross- und Kleinschreibung; die OpenAPI-Beschreibung
# nennt die Pfade mit grossem Anfangsbuchstaben. Eine case-sensitive Regel wuerde einen
# daraus erzeugten Client an der API vorbei auf die Startseite schicken — mit Status 200.
for entrypoint in "${entrypoints[@]}"; do
  if grep -q 'location ~ \^/' "$entrypoint"; then
    printf 'In %s gibt es case-sensitive Weiterleitungsregeln (location ~). Bitte location ~* verwenden.\n' "$entrypoint" >&2
    exit 1
  fi
done

# In einem nicht in Anfuehrungszeichen gesetzten Heredoc fuehrt die Shell Rueckwaerts-
# anfuehrungszeichen und $(...) aus. Ein Kommentar mit Backticks landete so als
# Fehlermeldung statt in der Konfiguration.
for entrypoint in "${entrypoints[@]}"; do
  if grep -q '`' "$entrypoint"; then
    printf 'In %s stehen Rueckwaertsanfuehrungszeichen; im Heredoc wuerden sie ausgefuehrt.\n' "$entrypoint" >&2
    exit 1
  fi
done

# Testzweck: Der BFF muss hinter einem TLS-Terminator das urspruengliche Schema und den
# vollstaendigen Host inklusive Nichtstandardport bis zur API erhalten. `$scheme` am
# Runtime-Gateway wuerde HTTPS zu internem HTTP herabstufen; `$host` entfernt den Port und
# laesst dadurch Origin-/CSRF-Pruefungen fehlschlagen.
runtime_gateway="$repo_root/deploy/nginx/runtime.conf"
console_entrypoint="$repo_root/deploy/console/entrypoint.sh"
if grep -Fq 'proxy_set_header X-Forwarded-Proto $scheme;' "$runtime_gateway"; then
  echo "Das Runtime-Gateway ueberschreibt X-Forwarded-Proto mit dem internen Schema." >&2
  exit 1
fi

grep -Fq 'proxy_set_header X-Forwarded-Proto $flowzer_forwarded_proto;' "$runtime_gateway"
grep -Fq 'proxy_set_header X-Forwarded-Proto \$flowzer_forwarded_proto;' "$console_entrypoint"
grep -Fq 'proxy_set_header Host $http_host;' "$runtime_gateway"
grep -Fq 'proxy_set_header Host \$http_host;' "$console_entrypoint"

# Testzweck: Die API darf Forwarded-Header nur aus dem explizit konfigurierten
# Containernetz auswerten. Ohne diese Runtime-Einstellung erzeugt der OIDC-Handler intern
# eine HTTP-Callback-Adresse und die vorgelagerte TLS-Terminierung ist wirkungslos.
runtime_compose="$repo_root/compose.runtime.yml"
if ! grep -q 'ForwardedHeaders__KnownNetworks__0:' "$runtime_compose"; then
  echo "compose.runtime.yml vertraut keinem konfigurierten Proxy-Netz." >&2
  exit 1
fi

# Testzweck: Das lokale Runtime-Gateway darf einen vom Client fälschbaren
# X-Forwarded-Proto-Header nicht standardmäßig im gesamten Hostnetz anbieten. Wer einen
# externen Container-Proxy nutzt, muss die Bindung deshalb bewusst öffnen.
if ! grep -Fq '${FLOWZER_RUNTIME_BIND_ADDRESS:-127.0.0.1}:${FLOWZER_RUNTIME_PORT:-5288}:8080' "$runtime_compose"; then
  echo "Das Runtime-Gateway bindet nicht standardmäßig ausschließlich an Loopback." >&2
  exit 1
fi

printf 'OK: Das Gateway leitet alle API-Routen weiter.\n'
