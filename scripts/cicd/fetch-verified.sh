#!/usr/bin/env bash
#
# Download a file and fail unless it matches a pinned checksum.
#
# Usage: fetch-verified.sh <url> <sha256|sha512> <expected-hex-digest> <output-path>
#
# The expected digest is pinned by the caller (for example in the CI workflow), not read
# from the same server as the download, so a tampered release cannot vouch for itself.
# On a mismatch the downloaded file is deleted and the script exits non-zero.
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "Usage: $0 <url> <sha256|sha512> <expected-hex-digest> <output-path>" >&2
  exit 2
fi

url="$1"
algorithm="$2"
expected="${3,,}"
output="$4"

case "$algorithm" in
  sha256) expected_length=64 ;;
  sha512) expected_length=128 ;;
  *)
    echo "ERROR: unsupported algorithm '$algorithm' (use sha256 or sha512)." >&2
    exit 2
    ;;
esac

if [[ ! "$expected" =~ ^[0-9a-f]+$ || ${#expected} -ne $expected_length ]]; then
  echo "ERROR: expected $algorithm digest must be $expected_length hex characters." >&2
  exit 2
fi

if [[ "$url" != https://* ]]; then
  echo "ERROR: refusing to download over a non-HTTPS URL: $url" >&2
  exit 2
fi

echo "==> downloading $url"
curl --proto '=https' --tlsv1.2 -sSfL --retry 3 -o "$output" "$url"

actual="$("${algorithm}sum" "$output" | cut -d' ' -f1)"
if [[ "$actual" != "$expected" ]]; then
  rm -f "$output"
  echo "ERROR: $algorithm mismatch for $url" >&2
  echo "       expected $expected" >&2
  echo "       actual   $actual" >&2
  exit 1
fi

echo "==> $algorithm verified for $(basename "$output")"
