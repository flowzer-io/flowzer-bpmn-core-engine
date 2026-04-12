#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

mkdir -p "${repo_root}/.data/flowzer-storage"

docker compose \
  -f "${repo_root}/compose.local.yml" \
  up -d --wait api frontend

echo "Flowzer local stack started and is healthy."
echo "- API:      http://localhost:5182/swagger/index.html"
echo "- Health:   http://localhost:5182/health"
echo "- Readiness:http://localhost:5182/health/ready"
echo "- Frontend: http://localhost:5269"
