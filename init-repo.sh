#!/bin/bash

# Initialise this template repository as a new module.
# This is destructive: files are edited and renamed in place.
#
# The current namespace is read from the solution file at the repository root,
# so the script can be run again to move an initialised repository to another namespace.
#
# Usage: ./init-repo.sh New.Namespace

set -euo pipefail

cd "$(dirname "${0}")" || exit 1

NAMESPACE_NEW="${1:-}"

# Escape a literal string for use in a sed regular expression.
sed_escape_pattern() {
  sed 's/[][\.*^$/]/\\&/g' <<< "${1}"
}

# Escape a literal string for use in a sed replacement.
sed_escape_replacement() {
  sed 's/[\&/]/\\&/g' <<< "${1}"
}

rename_project() {
  local directory
  directory="$(dirname "${1}")"
  local name
  name="$(basename "${1}")"
  local csproj
  csproj="${1}/${name}.csproj"

  local nameNew
  nameNew="${NAMESPACE_NEW}${name#"${NAMESPACE_OLD}"}"
  local csprojNew
  csprojNew="${1}/${nameNew}.csproj"

  if [ -f "${csproj}" ]; then
    mv "${csproj}" "${csprojNew}"
  fi
  mv "${1}" "${directory}/${nameNew}"
}

if [ -z "${NAMESPACE_NEW}" ]; then
  echo "No new namespace provided."
  exit 1
fi

# C# namespace: dotted identifiers (letter or underscore, then letters, digits, or underscores).
if ! [[ "${NAMESPACE_NEW}" =~ ^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$ ]]; then
  echo "Namespace must look like a C# namespace (e.g. MyCompany.MyApp)."
  exit 1
fi

shopt -s nullglob
SOLUTIONS=(*.slnx)
shopt -u nullglob
if [ "${#SOLUTIONS[@]}" -ne 1 ]; then
  echo "Expected exactly one .slnx file in $(pwd) to detect the current namespace, found ${#SOLUTIONS[@]}."
  exit 1
fi
NAMESPACE_OLD="${SOLUTIONS[0]%.slnx}"

if [ "${NAMESPACE_NEW}" = "${NAMESPACE_OLD}" ]; then
  echo "New namespace must differ from ${NAMESPACE_OLD}."
  exit 1
fi

# Match whole names only, so that Foo.Bar does not also rewrite Foo.Barn or Foo.Bar_X.
NAMESPACE_OLD_PATTERN="\\b$(sed_escape_pattern "${NAMESPACE_OLD}")\\b"
# The regex-escaped form (Foo\.Bar), as written in .gitleaks.toml path allowlists.
NAMESPACE_OLD_REGEX_PATTERN="$(sed_escape_pattern "${NAMESPACE_OLD//./\\.}")\\b"
NAMESPACE_NEW_REGEX_REPLACEMENT="$(sed_escape_replacement "${NAMESPACE_NEW//./\\.}")"

echo "Renaming namespace ${NAMESPACE_OLD} -> ${NAMESPACE_NEW}"

# Paths that never hold template sources: VCS metadata, build output, local container data, and this script.
PRUNE=(\( -path './.git' -o -name 'bin' -o -name 'obj' -o -path './test/local/data' -o -path './init-repo.sh' \) -prune -o)

while read -r f; do
  [ -z "${f}" ] && continue
  rename_project "${f}"
done <<< "$(find . "${PRUNE[@]}" -type d \( -name "${NAMESPACE_OLD}" -o -name "${NAMESPACE_OLD}.*" \) -print)"

mv "${NAMESPACE_OLD}.slnx" "${NAMESPACE_NEW}.slnx"

while read -r f; do
  [ -z "${f}" ] && continue
  sed -i \
    -e "s/${NAMESPACE_OLD_REGEX_PATTERN}/${NAMESPACE_NEW_REGEX_REPLACEMENT}/g" \
    -e "s/${NAMESPACE_OLD_PATTERN}/${NAMESPACE_NEW}/g" \
    "${f}"
done <<< "$(find . "${PRUNE[@]}" -type f \( \
  -name '*.cs' -o \
  -name '*.csproj' -o \
  -name '*.md' -o \
  -name '*.mdc' -o \
  -name '*.slnx' -o \
  -name '*.sh' -o \
  -name 'Dockerfile' -o \
  -path './.gitleaks.toml' -o \
  -path './test/local/docker-compose.yaml' -o \
  \( -path './.github/*' -a \( -name '*.yml' -o -name '*.yaml' \) \) \
\) -print)"
