#!/usr/bin/env bash
set -euo pipefail

api_url="${FLOWZER_API_URL:-http://localhost:5182}"
health_file="$(mktemp /tmp/flowzer-health.XXXXXX.json)"
ready_file="$(mktemp /tmp/flowzer-ready.XXXXXX.json)"
diagnostics_file="$(mktemp /tmp/flowzer-ops.XXXXXX.json)"
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

trap 'rm -f "$health_file" "$ready_file" "$diagnostics_file"' EXIT

echo "Checking API liveness: ${api_url}/health"
curl "${curl_opts[@]}" "${api_url}/health" >"$health_file"
cat "$health_file"
echo

echo "Checking API readiness: ${api_url}/health/ready"
curl "${curl_opts[@]}" "${api_url}/health/ready" >"$ready_file"
cat "$ready_file"
echo

echo "Checking API operations diagnostics: ${api_url}/operations/diagnostics"
curl "${curl_opts[@]}" "${api_url}/operations/diagnostics" >"$diagnostics_file"
grep -Eqi '"(successful|Successful)"[[:space:]]*:[[:space:]]*true' "$diagnostics_file"
cat "$diagnostics_file"
echo

# Die Oberflaeche laeuft lokal nicht im Container, sondern mit `npm run dev` in
# src/FlowzerConsole gegen diese API. Geprueft wird hier deshalb nur die API.
echo "API responded successfully."
