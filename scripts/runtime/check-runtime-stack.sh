#!/usr/bin/env bash
set -euo pipefail

runtime_port="${FLOWZER_RUNTIME_PORT:-5288}"
gateway_url="${FLOWZER_GATEWAY_URL:-http://localhost:${runtime_port}}"
health_file="$(mktemp /tmp/flowzer-runtime-health.XXXXXX.json)"
ready_file="$(mktemp /tmp/flowzer-runtime-ready.XXXXXX.json)"
diagnostics_file="$(mktemp /tmp/flowzer-runtime-ops.XXXXXX.json)"
console_file="$(mktemp /tmp/flowzer-runtime-console.XXXXXX.json)"
curl_opts=(
  --fail
  --silent
  --show-error
  --connect-timeout 5
  --max-time 15
  --retry 10
  --retry-delay 1
  --retry-all-errors
  --retry-connrefused
)

trap 'rm -f "$health_file" "$ready_file" "$diagnostics_file" "$console_file"' EXIT

echo "Checking runtime gateway liveness: ${gateway_url}/health"
curl "${curl_opts[@]}" "${gateway_url}/health" >"$health_file"
cat "$health_file"
echo

echo "Checking runtime gateway readiness: ${gateway_url}/health/ready"
curl "${curl_opts[@]}" "${gateway_url}/health/ready" >"$ready_file"
cat "$ready_file"
echo

echo "Checking runtime operations diagnostics: ${gateway_url}/operations/diagnostics"
curl "${curl_opts[@]}" "${gateway_url}/operations/diagnostics" >"$diagnostics_file"
grep -Eqi '"(successful|Successful)"[[:space:]]*:[[:space:]]*true' "$diagnostics_file"
cat "$diagnostics_file"
echo

# Die Laufzeitkonfiguration der Konsole entsteht erst beim Start des Containers. Sie ist
# damit der aussagekraeftigste Beleg dafuer, dass die Oberflaeche wirklich hochgelaufen ist —
# die Startseite liefert nginx auch dann, wenn der Einstiegspunkt nichts geschrieben hat.
echo "Checking runtime console config: ${gateway_url}/config.json"
curl "${curl_opts[@]}" "${gateway_url}/config.json" >"$console_file"
grep -q '"apiBaseUrl"' "$console_file"
echo "Runtime console responded successfully."
