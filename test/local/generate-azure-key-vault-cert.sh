#!/bin/bash
set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")" || exit 1

# Generate private key and self-signed certificate for the Azure Key Vault emulator.
# The SAN must cover every name that clients use to reach the emulator:
#   * azure-key-vault-emulator.vault.azure.net: the worker, inside the compose network
#   * localhost and 127.0.0.1: helper scripts on the host, such as set-azure-key-vault-secrets.py
openssl req -x509 -newkey rsa:4096 -sha256 -days 365 -nodes \
  -keyout config/azure-key-vault/emulator.local.key -out config/azure-key-vault/emulator.local.crt \
  -subj "/CN=azure-key-vault-emulator" \
  -addext "subjectAltName=DNS:azure-key-vault-emulator.vault.azure.net,DNS:azure-keyvault,DNS:localhost,IP:127.0.0.1"

# Package into PFX format for ASP.NET Core Kestrel
openssl pkcs12 -export -out config/azure-key-vault/emulator.local.pfx \
  -inkey config/azure-key-vault/emulator.local.key -in config/azure-key-vault/emulator.local.crt \
  -password pass:YourSecurePassword

# The emulator container reads the PFX as a different user, so it must be readable by others.
# The PEM private key is not used by any container, so it stays private to you.
chmod 600 config/azure-key-vault/emulator.local.key
chmod o+r config/azure-key-vault/emulator.local.pfx config/azure-key-vault/emulator.local.crt

# Publish only the public certificate for clients to pin. This directory is mounted into the worker,
# so the worker never sees the private key or PFX.
cp config/azure-key-vault/emulator.local.crt config/azure-key-vault-ca/emulator.local.crt
chmod 644 config/azure-key-vault-ca/emulator.local.crt
