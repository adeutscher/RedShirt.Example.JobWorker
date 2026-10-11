#!/usr/bin/env bash
#
# Lint stage: repository hygiene and static checks safe on untrusted pull requests.
#
# No registry credentials required.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

echo "==> whitespace and conflict markers"
git diff --check

echo "==> Dockerfile present"
if [[ ! -f Dockerfile ]]; then
  echo "ERROR: Dockerfile not found at repository root." >&2
  exit 1
fi

if command -v hadolint >/dev/null 2>&1; then
  echo "==> hadolint"
  hadolint Dockerfile
else
  echo "==> hadolint not on PATH, skipping (install on the runner to enable)"
fi

echo "==> lint clean"
