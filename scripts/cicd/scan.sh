#!/usr/bin/env bash
#
# Scan stage: secret detection and NuGet vulnerability audit for the solution, plus any
# .sln or .slnx files directly under tools/ (e.g. scaffolding). Fails if any project has a
# known-vulnerable package, direct or transitive.
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

# dotnet list package exits 0 even when it finds vulnerable packages, so look for its
# "has the following vulnerable packages" line instead (in English, whatever the runner locale).
# Every solution is audited before failing.
vulnerable_solutions=()
audit_solution() {
  local solution="$1"
  local report="$2"
  echo "==> dotnet list package: vulnerable dependencies ($solution)"
  DOTNET_CLI_UI_LANGUAGE=en dotnet list "$solution" package --vulnerable --include-transitive | tee "$report"
  if grep -q "has the following vulnerable packages" "$report"; then
    vulnerable_solutions+=( "$solution (see $report)" )
  fi
}

audit_solution "$sln" "$report_dir/dotnet-vulnerable-packages.txt"

# Tool solutions are kept out of the main solution, so audit them separately.
tool_solutions=()
while IFS= read -r path; do
  tool_solutions+=( "$path" )
done < <(find tools -maxdepth 1 -type f \( -name '*.sln' -o -name '*.slnx' \) 2>/dev/null | LC_ALL=C sort)

for tool_sln in "${tool_solutions[@]}"; do
  audit_solution "$tool_sln" "$report_dir/dotnet-vulnerable-packages-tools-$(basename "${tool_sln%.*}").txt"
done

if [[ ${#vulnerable_solutions[@]} -gt 0 ]]; then
  echo "ERROR: vulnerable NuGet packages found in:" >&2
  printf '  %s\n' "${vulnerable_solutions[@]}" >&2
  exit 1
fi

echo "==> scan clean, reports in $report_dir"
