#!/usr/bin/env python

import json
import os
import sys

from google.cloud import pubsub_v1

PROJECT_ID = "local-pubsub"
TOPIC_ID = "jobs"
EMULATOR_HOST = "localhost:8085"


def send_message(duration):
    os.environ["PUBSUB_EMULATOR_HOST"] = EMULATOR_HOST

    publisher = pubsub_v1.PublisherClient()
    topic_path = publisher.topic_path(PROJECT_ID, TOPIC_ID)

    body = {"SleepDurationSeconds": int(duration)}
    future = publisher.publish(topic_path, json.dumps(body).encode("utf-8"))
    message_id = future.result()
    print(f"Published message {message_id} to {topic_path}")


if __name__ == "__main__":
    send_message(sys.argv[1])
