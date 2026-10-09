#!/usr/bin/env bash
set -euo pipefail
# Temporäre Hosted-Diagnose, keine Pflicht-CI: nur feste lokale Image- und öffentliche Endpointmetadaten.
# Kein Pull, Login, Tokenbody, Header-/Env-/Docker-Authdump oder automatischer Retry.
printf 'runner_class=ubuntu-latest\n'
all_ok=true
images=(
  'testcontainers/ryuk@sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0'
  'nginx:1.27-alpine'
  'postgres:17-alpine'
)
labels=(ryuk nginx postgres)
scopes=('testcontainers%2Fryuk' 'library%2Fnginx' 'library%2Fpostgres')
for index in 0 1 2; do
  # false heißt nicht bewiesen, nicht zwingend abwesend: auch eine lokale Daemonstörung bleibt unbekannt.
  cache=false
  # TERM nach fünf Sekunden, KILL spätestens eine Sekunde später; kein hängender Inspect.
  if timeout --kill-after=1s 5s docker image inspect "${images[$index]}" >/dev/null 2>&1; then cache=true; fi
  printf 'image=%s cache_present_confirmed=%s\n' "${labels[$index]}" "$cache"
  exit_code=0
  # --disable ist zwingend die erste Option: keine curlrc-Auth-/Trace-/TLS-/Retry-Nebenpfade.
  # Kein --location: keine Umleitung des Probes. TLS bleibt unverändert validiert.
  # Öffentlich/anonym, keine Credentialquelle; Antwortkörper wird sofort verworfen.
  result="$(curl --disable --silent --output /dev/null --connect-timeout 5 --max-time 12 \
    --write-out '%{http_code} %{time_total}' \
    "https://auth.docker.io/token?account=githubactions&scope=repository%3A${scopes[$index]}%3Apull&service=registry.docker.io")" || exit_code=$?
  status=000 elapsed=0 request_ok=false
  # Nur die feste numerische Metadatenprojektion ausgeben, niemals eine unerwartete freie Antwort.
  if [[ "$result" =~ ^([0-9]{3})[[:space:]]([0-9]+\.[0-9]+)$ ]]; then
    status="${BASH_REMATCH[1]}" elapsed="${BASH_REMATCH[2]}"
  fi
  if [[ "$exit_code" == 0 && "$status" == 200 ]]; then request_ok=true; else all_ok=false; fi
  printf 'probe=%s http_status=%s elapsed_seconds=%s request_ok=%s\n' "${labels[$index]}" "$status" "$elapsed" "$request_ok"
done
# Endpoint-200 ist nur dieser frische Hosted-Netzpfad, kein Dockerpull-/Anmeldungs-/Test-/Produktnachweis.
[[ "$all_ok" == true ]]
