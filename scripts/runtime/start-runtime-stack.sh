#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"
runtime_port="${FLOWZER_RUNTIME_PORT:-5288}"

mkdir -p "${repo_root}/.data/runtime-storage"

docker compose \
  -f "${repo_root}/compose.runtime.yml" \
  up -d --build --wait api frontend gateway

echo "Flowzer runtime stack started and is healthy."
echo "- Gateway:  http://localhost:${runtime_port}"
echo "- Health:   http://localhost:${runtime_port}/health"
echo "- Readiness:http://localhost:${runtime_port}/health/ready"
