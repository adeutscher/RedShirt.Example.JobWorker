#!/usr/bin/env bash
#
# Build stage: restore, build, and test
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

# Use SLN_FILE when set. Otherwise require exactly one root .sln or .slnx.
resolve_sln_file() {
  if [[ -n "${SLN_FILE:-}" ]]; then
    printf '%s\n' "$SLN_FILE"
    return
  fi

  local matches=()
  shopt -s nullglob
  matches=( *.sln *.slnx )
  shopt -u nullglob

  if [[ ${#matches[@]} -eq 0 ]]; then
    echo "ERROR: no .sln or .slnx file in the repository root. Set SLN_FILE." >&2
    exit 1
  fi
  if [[ ${#matches[@]} -gt 1 ]]; then
    echo "ERROR: multiple solution files in the repository root. Set SLN_FILE." >&2
    printf '  %s\n' "${matches[@]}" >&2
    exit 1
  fi
  printf '%s\n' "${matches[0]}"
}

sln="$(resolve_sln_file)" || exit 1
configuration="${BUILD_CONFIGURATION:-Release}"

echo "==> dotnet restore $sln"
dotnet restore "$sln"

echo "==> dotnet build $sln (--no-restore, -c $configuration)"
dotnet build "$sln" --no-restore --configuration "$configuration"

echo "==> dotnet test $sln (--no-build, -c $configuration)"
dotnet test "$sln" --no-build --configuration "$configuration"

echo "==> build clean"
