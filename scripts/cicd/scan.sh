#!/usr/bin/env bash
#
# Scan stage: secret detection and NuGet vulnerability audit for the solution.
#
# No registry credentials. Safe on untrusted pull requests when only gitleaks and
# dotnet list package run against the checkout.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

sln="${SLN_FILE:-RedShirt.Example.JobWorker.slnx}"
report_dir="artifacts/scan"
mkdir -p "$report_dir"

if command -v gitleaks >/dev/null 2>&1; then
  echo "==> gitleaks: secret detection"
  gitleaks detect \
    --source="." \
    --report-format=json \
    --report-path="$report_dir/gitleaks.json" \
    --verbose \
    --redact
else
  echo "ERROR: gitleaks not on PATH (install on the runner to enable secret scanning)." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet not on PATH." >&2
  exit 1
fi

echo "==> dotnet list package: vulnerable dependencies"
dotnet list "$sln" package --vulnerable --include-transitive \
  | tee "$report_dir/dotnet-vulnerable-packages.txt"

echo "==> scan clean, reports in $report_dir"
