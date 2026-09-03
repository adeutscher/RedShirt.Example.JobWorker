#!/bin/bash
set -euo pipefail

# Set the Bar OAuth client secret in ministack SSM (/bar/oauth/client-secret).
# Does not update WireMock stubs.
# Default value is intentionally invalid against the default WireMock mappings (local-bar-client-secret).
# To rotate a secret WireMock will also accept: ./scripts/wiremock-bar/bar-rotate-oauth-credentials.sh [new-secret] [new-token]
#
# Usage:
#   ./scripts/wiremock-bar/bar-set-ssm-oauth-secret.sh [secret]
#
# Examples:
#   ./scripts/wiremock-bar/bar-set-ssm-oauth-secret.sh
#   ./scripts/wiremock-bar/bar-set-ssm-oauth-secret.sh 'bogus-secret-value-here'

SECRET="${1:-bad-bar-client-secret}"

echo "Setting SSM /bar/oauth/client-secret → ${SECRET}"
AWS_DEFAULT_REGION=us-east-1 awslocal ssm put-parameter --overwrite --type String \
    --name /bar/oauth/client-secret \
    --value "${SECRET}"
