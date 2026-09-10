#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"
runtime_port="${FLOWZER_RUNTIME_PORT:-5288}"

mkdir -p "${repo_root}/.data/runtime-storage" "${repo_root}/.data/runtime-data-protection"
# Der Keyring schützt Session-, OIDC-Korrelations- und Antiforgery-Cookies.
# Der Container läuft als root; auf dem Host darf ihn trotzdem nur der Besitzer lesen.
chmod 700 "${repo_root}/.data/runtime-data-protection"

docker compose \
  -f "${repo_root}/compose.runtime.yml" \
  up -d --build --wait api console gateway

echo "Flowzer runtime stack started and is healthy."
echo "- Gateway:  http://localhost:${runtime_port}"
echo "- Health:   http://localhost:${runtime_port}/health"
echo "- Readiness:http://localhost:${runtime_port}/health/ready"
