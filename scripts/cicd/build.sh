#!/usr/bin/env bash
#
# Build stage: restore, build, and test
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

sln="${SLN_FILE:-RedShirt.Example.JobWorker.slnx}"
configuration="${BUILD_CONFIGURATION:-Release}"

echo "==> dotnet restore $sln"
dotnet restore "$sln"

echo "==> dotnet build $sln (--no-restore, -c $configuration)"
dotnet build "$sln" --no-restore --configuration "$configuration"

echo "==> dotnet test $sln (--no-build, -c $configuration)"
dotnet test "$sln" --no-build --configuration "$configuration"

echo "==> build clean"
