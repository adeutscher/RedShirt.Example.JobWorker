#!/usr/bin/env python

from azure.servicebus import ServiceBusClient, ServiceBusMessage
import json
import sys

# Local emulator connection string
CONNECTION_STR = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
QUEUE_NAME = "test-queue"  # Must match a queue defined in your emulator config


def send_message(duration):
    # Instantiate the client targeting local emulator
    body = {'SleepDurationSeconds': int(duration)}
    client = ServiceBusClient.from_connection_string(CONNECTION_STR)

    with client:
        sender = client.get_queue_sender(queue_name=QUEUE_NAME)
        with sender:
            message = ServiceBusMessage(json.dumps(body))
            sender.send_messages(message)
            print("Message sent to emulator successfully!")


if __name__ == "__main__":
    send_message(sys.argv[1])
