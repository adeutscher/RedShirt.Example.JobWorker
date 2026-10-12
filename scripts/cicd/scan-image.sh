#!/usr/bin/env bash
#
# Image scan stage: container vulnerability gate after docker-build.sh.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

image_name="${IMAGE_NAME:-job-worker}"
image_tag="${IMAGE_TAG:-ci-local}"
image_ref="$image_name:$image_tag"

report_dir="artifacts/scan"
mkdir -p "$report_dir"

if ! command -v trivy >/dev/null 2>&1; then
  echo "ERROR: trivy not on PATH (install on the runner to enable image scanning)." >&2
  exit 1
fi

trivy_args=(
  image
  --severity HIGH,CRITICAL
  --exit-code 1
  --format json
  --output "$report_dir/trivy-image.json"
)

if [[ -f trivy-policy.rego ]]; then
  trivy_args+=(--ignore-policy "trivy-policy.rego")
fi

echo "==> trivy image $image_ref"
trivy "${trivy_args[@]}" "$image_ref"

echo "==> image scan clean, report in $report_dir"
