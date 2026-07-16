#!/bin/bash

cd "$(dirname "$(readlink -f "$0")")"

# Generate private key and certificate
openssl req -x509 -newkey rsa:4096 -sha256 -days 365 -nodes \
  -keyout config/azure-key-vault/emulator.local.key -out config/azure-key-vault/emulator.local.crt \
  -subj "/CN=azure-key-vault-emulator" \
  -addext "subjectAltName=DNS:azure-keyvault,DNS:localhost"

# Package into PFX format for ASP.NET Core Kestrel
openssl pkcs12 -export -out config/azure-key-vault/emulator.local.pfx \
  -inkey config/azure-key-vault/emulator.local.key -in config/azure-key-vault/emulator.local.crt \
  -password pass:YourSecurePassword

chmod o+r config/azure-key-vault/emulator*
