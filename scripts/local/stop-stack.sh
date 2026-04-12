#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

docker compose \
  -f "${repo_root}/compose.local.yml" \
  down

echo "Flowzer local stack stopped."
