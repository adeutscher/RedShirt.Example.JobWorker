#!/usr/bin/env python

from azure.core.exceptions import ResourceExistsError
from azure.storage.queue import QueueClient
import json
import sys

# Local Azurite connection string (host-side; worker uses azurite hostname via Key Vault)
CONNECTION_STR = (
    "DefaultEndpointsProtocol=http;"
    "AccountName=devstoreaccount1;"
    "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
    "QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;"
)
QUEUE_NAME = "test-azure-queue"


def send_message(duration):
    body = {"SleepDurationSeconds": int(duration)}
    # Do not Base64-encode; this template expects plain UTF-8 message bodies.
    client = QueueClient.from_connection_string(CONNECTION_STR, QUEUE_NAME)

    try:
        client.create_queue()
    except ResourceExistsError:
        pass

    client.send_message(json.dumps(body))
    print("Message sent to Azurite successfully!")


if __name__ == "__main__":
    send_message(sys.argv[1])
