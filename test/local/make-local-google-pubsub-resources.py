#!/usr/bin/env python

"""
Create the topic and pull subscription used by local Google Pub/Sub testing.

Requires the Pub/Sub emulator to already be running (see test/local/readme.md).
"""

import os

from google.api_core.exceptions import AlreadyExists
from google.cloud import pubsub_v1

PROJECT_ID = "local-pubsub"
TOPIC_ID = "jobs"
SUBSCRIPTION_ID = "jobs-subscription"
ACK_DEADLINE_SECONDS = 60
EMULATOR_HOST = "localhost:8085"


def main():
    os.environ["PUBSUB_EMULATOR_HOST"] = EMULATOR_HOST

    publisher = pubsub_v1.PublisherClient()
    subscriber = pubsub_v1.SubscriberClient()

    topic_path = publisher.topic_path(PROJECT_ID, TOPIC_ID)
    subscription_path = subscriber.subscription_path(PROJECT_ID, SUBSCRIPTION_ID)

    try:
        publisher.create_topic(request={"name": topic_path})
        print(f"Created topic: {topic_path}")
    except AlreadyExists:
        print(f"Topic already exists: {topic_path}")

    try:
        subscriber.create_subscription(
            request={
                "name": subscription_path,
                "topic": topic_path,
                "ack_deadline_seconds": ACK_DEADLINE_SECONDS,
            }
        )
        print(f"Created subscription: {subscription_path}")
    except AlreadyExists:
        print(f"Subscription already exists: {subscription_path}")


if __name__ == "__main__":
    main()
