#!/bin/bash

NAMESPACE_OLD="RedShirt.Example.JobWorker"
NAMESPACE_NEW="${1}"

rename_project() {
  local directory
  directory="$(dirname "${1}")"
  local name
  name="$(basename "${1}")"
  local csproj
  csproj="${1}/${name}.csproj"

  local nameNew
  nameNew="$(sed "s/${NAMESPACE_OLD}/${NAMESPACE_NEW}/g" <<< "${name}")"
  local csprojNew
  csprojNew="${1}/${nameNew}.csproj"

  mv "${csproj}" "${csprojNew}"
  mv "${1}" "${directory}/${nameNew}"
}

if [ -z "${NAMESPACE_NEW}" ]; then
  echo "No new namespace provided."
  exit 1
fi

while read -r f; do
  rename_project "${f}"
done <<< "$(find . -type d -name "${NAMESPACE_OLD}*")"

mv "${NAMESPACE_OLD}.sln" "${NAMESPACE_NEW}.sln"

while read -r f; do
  sed -i "s/${NAMESPACE_OLD}/${NAMESPACE_NEW}/g" "${f}"
done <<< "$(find . -name "*.cs" -o -name '*.csproj' -o -name '*.md' -o -name '*.sln' -o -name '*.sh' -o -name 'Dockerfile')"
