#!/usr/bin/env bash
#
# Docker build stage: produce the application image (build stage runs dotnet test).
#
# Uses a local-only tag by default so pull-request jobs do not need registry credentials.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

image_name="${IMAGE_NAME:-job-worker}"
image_tag="${IMAGE_TAG:-ci-local}"
image_ref="$image_name:$image_tag"

mkdir -p artifacts

echo "==> docker build --pull -t $image_ref"
docker build \
  --pull \
  --build-arg "BUILD_CONFIGURATION=${BUILD_CONFIGURATION:-Release}" \
  --build-arg "TESTS_ENABLE=${TESTS_ENABLE:-1}" \
  -t "$image_ref" .

echo "$image_ref" > artifacts/image-tag.txt

echo "==> built $image_ref"
