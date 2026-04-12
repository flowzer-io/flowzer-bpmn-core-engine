#!/usr/bin/env bash
set -euo pipefail

runtime_port="${FLOWZER_RUNTIME_PORT:-5288}"
gateway_url="${FLOWZER_GATEWAY_URL:-http://localhost:${runtime_port}}"
health_file="$(mktemp /tmp/flowzer-runtime-health.XXXXXX.json)"
ready_file="$(mktemp /tmp/flowzer-runtime-ready.XXXXXX.json)"
diagnostics_file="$(mktemp /tmp/flowzer-runtime-ops.XXXXXX.json)"
frontend_file="$(mktemp /tmp/flowzer-runtime-frontend.XXXXXX.html)"
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

trap 'rm -f "$health_file" "$ready_file" "$diagnostics_file" "$frontend_file"' EXIT

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

echo "Checking runtime frontend root: ${gateway_url}"
curl "${curl_opts[@]}" "${gateway_url}" >"$frontend_file"
grep -q "FlowzerFrontend" "$frontend_file"
echo "Runtime frontend responded successfully."
