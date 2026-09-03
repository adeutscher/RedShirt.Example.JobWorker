#!/bin/bash
set -euo pipefail

# Rotate the Bar OAuth client secret in ministack SSM and update WireMock's in-memory stubs
# (token bodyPatterns, access_token response, and Authorization bearer matchers).
# Does not rewrite files under wiremock/bar/mappings/ — a WireMock restart restores those defaults.
#
# Usage:
#   ./scripts/wiremock-bar/bar-rotate-oauth-credentials.sh [new-secret] [new-access-token]
#
# Examples:
#   ./scripts/wiremock-bar/bar-rotate-oauth-credentials.sh
#   ./scripts/wiremock-bar/bar-rotate-oauth-credentials.sh my-new-secret my-new-token
#
# Pair with ./scripts/wiremock-bar/bar-set-ssm-oauth-secret.sh (SSM only → token 401) then this script (SSM + WireMock → recovery).

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WIREMOCK_URL="${WIREMOCK_URL:-http://localhost:9101}"
SSM_PARAM_NAME="${BAR_CLIENT_SECRET_SSM_PATH:-/bar/oauth/client-secret}"
NEW_SECRET="${1:-rotated-bar-client-secret-$(date +%s)}"
NEW_TOKEN="${2:-rotated-bar-access-token-$(date +%s)}"

if ! command -v jq >/dev/null 2>&1; then
    echo "jq is required to update WireMock stubs via the Admin API." >&2
    exit 1
fi

if ! command -v awslocal >/dev/null 2>&1; then
    echo "awslocal is required to update the SSM parameter." >&2
    exit 1
fi

if ! curl -sf "${WIREMOCK_URL}/__admin/health" >/dev/null \
    && ! curl -sf "${WIREMOCK_URL}/__admin/mappings" >/dev/null; then
    echo "WireMock Admin API is not reachable at ${WIREMOCK_URL}." >&2
    echo "Start it with: (cd \"${ROOT_DIR}\" && docker compose up -d wiremock-bar)" >&2
    exit 1
fi

echo "Setting SSM ${SSM_PARAM_NAME} → ${NEW_SECRET}"
AWS_DEFAULT_REGION=us-east-1 awslocal ssm put-parameter --overwrite --type String \
    --name "${SSM_PARAM_NAME}" \
    --value "${NEW_SECRET}" >/dev/null

echo "Updating WireMock stubs at ${WIREMOCK_URL} (in-memory only)"
updated_count=0
while IFS= read -r stub; do
    id="$(jq -r '.id' <<<"${stub}")"
    changed=0

    if jq -e '
        (.request.urlPath == "/oauth/token")
        and (.request.bodyPatterns // [] | map(select(.contains? | type == "string" and startswith("client_secret="))) | length > 0)
      ' <<<"${stub}" >/dev/null 2>&1; then
        stub="$(jq --arg secret "${NEW_SECRET}" --arg token "${NEW_TOKEN}" '
            .request.bodyPatterns |= map(
              if (.contains? | type == "string" and startswith("client_secret="))
              then .contains = ("client_secret=" + $secret)
              else .
              end
            )
            | if .response.jsonBody.access_token? then .response.jsonBody.access_token = $token else . end
          ' <<<"${stub}")"
        changed=1
    fi

    if jq -e '.request.headers.Authorization.equalTo? | type == "string" and startswith("Bearer ")' \
        <<<"${stub}" >/dev/null 2>&1; then
        stub="$(jq --arg token "${NEW_TOKEN}" \
            '.request.headers.Authorization.equalTo = ("Bearer " + $token)' <<<"${stub}")"
        changed=1
    fi

    if [[ "${changed}" -eq 0 ]]; then
        continue
    fi

    curl -sf -X PUT \
        -H 'Content-Type: application/json' \
        -d "${stub}" \
        "${WIREMOCK_URL}/__admin/mappings/${id}" >/dev/null

    updated_count=$((updated_count + 1))
    echo "  updated stub ${id}"
done < <(curl -sf "${WIREMOCK_URL}/__admin/mappings" | jq -c '.mappings[]')

if [[ "${updated_count}" -eq 0 ]]; then
    echo "No WireMock stubs with OAuth secret or Bearer Authorization matchers were found." >&2
    exit 1
fi

SECRET_FORCE_COOLDOWN_SECONDS="${SECRET_FORCE_COOLDOWN_SECONDS:-1}"
TOKEN_REFRESH_COOLDOWN_SECONDS="${TOKEN_REFRESH_COOLDOWN_SECONDS:-1}"
WAIT_SECONDS="${SECRET_FORCE_COOLDOWN_SECONDS}"
if [[ "${TOKEN_REFRESH_COOLDOWN_SECONDS}" -gt "${WAIT_SECONDS}" ]]; then
    WAIT_SECONDS="${TOKEN_REFRESH_COOLDOWN_SECONDS}"
fi
WAIT_SECONDS=$((WAIT_SECONDS + 1))
echo "Waiting ${WAIT_SECONDS}s for local secret/token refresh cooldowns…"
sleep "${WAIT_SECONDS}"

echo "Done. Rotated ${updated_count} stub(s)."
echo "  client secret: ${NEW_SECRET}"
echo "  access token:  ${NEW_TOKEN}"
