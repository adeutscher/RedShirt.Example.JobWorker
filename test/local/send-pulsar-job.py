#!/usr/bin/env python

"""Publish a sleep job to the local Pulsar topic.

Requires the ``pulsar-client`` package and a running Pulsar from docker-compose
(with ``make-local-pulsar-resources.py`` already applied).

Host clients must use listener_name=external so topic lookup returns
pulsar://localhost:6650 instead of the in-compose hostname pulsar:6650.
"""

import json
import sys

import pulsar

SERVICE_URL = "pulsar://localhost:6650"
# Matches PULSAR_PREFIX_advertisedListeners "external" in docker-compose.yaml
LISTENER_NAME = "external"
TOPIC = "persistent://public/default/jobs"


def send_message(duration: int) -> None:
    body = {"SleepDurationSeconds": duration}
    client = pulsar.Client(SERVICE_URL, listener_name=LISTENER_NAME)
    try:
        producer = client.create_producer(TOPIC)
        try:
            message_id = producer.send(json.dumps(body).encode("utf-8"))
            print(f"Published message {message_id} to {TOPIC}")
        finally:
            producer.close()
    finally:
        client.close()


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(f"Usage: {sys.argv[0]} <sleep-duration-seconds>", file=sys.stderr)
        sys.exit(1)
    send_message(int(sys.argv[1]))
