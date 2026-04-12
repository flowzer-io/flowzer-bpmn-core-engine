#!/usr/bin/env bash
set -euo pipefail

api_url="${FLOWZER_API_URL:-http://localhost:5182}"
frontend_url="${FLOWZER_FRONTEND_URL:-http://localhost:5269}"

echo "Checking API liveness: ${api_url}/health"
curl --fail --silent --show-error "${api_url}/health" >/tmp/flowzer-health.json
cat /tmp/flowzer-health.json
echo

echo "Checking API readiness: ${api_url}/health/ready"
curl --fail --silent --show-error "${api_url}/health/ready" >/tmp/flowzer-ready.json
cat /tmp/flowzer-ready.json
echo

echo "Checking frontend root: ${frontend_url}"
curl --fail --silent --show-error "${frontend_url}" >/tmp/flowzer-frontend.html
grep -q "FlowzerFrontend" /tmp/flowzer-frontend.html
echo "Frontend responded successfully."
