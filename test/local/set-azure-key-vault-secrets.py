#!/usr/bin/env python

from azure.core.credentials import AccessToken
from azure.keyvault.secrets import SecretClient
import os
import time

# Define your unique Key Vault URL
VAULT_URL = "https://127.0.0.1:4997"


class EmulatorCredential:
    def get_token(self, *scopes, **kwargs):
        # Default mock JWT accepted by the rokeller emulator
        mock_token = (
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9."
            "eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNzM1Njg5NjAwLCJleHAiOjQxMDI0NDQ4MDAsImlzcyI6Imh0dHBzOi8vbG9jYWxob3N0LyJ9."
            "42D_zJ3qM02NM_ExWU9S9jvNGMfpop3YuWT9lFqJ5yU"
        )
        # Return far-future expiration (e.g., year 2100)
        return AccessToken(mock_token, 4102444800)


def set(secret_name, secret_value):
    # Authenticate against Azure
    credential = EmulatorCredential()

    # Initialize the Secret Client
    client = SecretClient(
        vault_url=VAULT_URL,
        credential=credential,
        verify=False,
        connection_verify=False,
        verify_challenge_resource=False,
    )
    print(f'Setting {secret_name}')
    client.set_secret(secret_name, secret_value)


if __name__ == '__main__':
    os.environ["AZURE_TENANT_ID"] = "00000000-0000-0000-0000-000000000000"
    os.environ["AZURE_CLIENT_ID"] = "devstoreaccount1"
    os.environ["AZURE_CLIENT_SECRET"] = (
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="
    )
    os.environ["AZURE_KEYVAULT_DISABLE_CHALLENGE_RESOURCE_VERIFICATION"] = "true"
    os.environ["REQUESTS_CA_BUNDLE"] = ""
    set(
        'azure-queue-storage-connection-string',
        "QueueEndpoint=http://azurite:10001/devstoreaccount1;DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;",
    )
    set(
        'azure-service-bus-connection-string',
        "Endpoint=sb://azure-service-bus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    )
    set(
        'common/redis',
        'redis:6379'
    )
