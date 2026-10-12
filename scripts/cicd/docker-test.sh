#!/usr/bin/env bash
#
# Docker test stage: smoke-check the image produced by docker-build.sh.
#
# Unit and integration tests already run in the Dockerfile build stage when
# TESTS_ENABLE=1. This script verifies the runtime image artifact is well-formed.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

image_name="${IMAGE_NAME:-job-worker}"
image_tag="${IMAGE_TAG:-ci-local}"
image_ref="$image_name:$image_tag"

if [[ -f artifacts/image-tag.txt ]]; then
  recorded="$(tr -d '[:space:]' < artifacts/image-tag.txt)"
  if [[ "$recorded" != "$image_ref" ]]; then
    echo "NOTE: artifacts/image-tag.txt is $recorded (using $image_ref from env)" >&2
  fi
fi

echo "==> docker image inspect $image_ref"
docker image inspect "$image_ref" >/dev/null

entrypoint_json="$(docker image inspect "$image_ref" --format '{{json .Config.Entrypoint}}')"
echo "==> entrypoint: $entrypoint_json"

if [[ "$entrypoint_json" != '["./RedShirt.Example.JobWorker"]' ]]; then
  echo "ERROR: unexpected entrypoint on $image_ref." >&2
  exit 1
fi

echo "==> docker test passed"
