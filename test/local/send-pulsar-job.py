#!/usr/bin/env python

import json
import sys

import pulsar

body = {"SleepDurationSeconds": int(sys.argv[1])}
topic = "persistent://public/default/jobs"

client = pulsar.Client("pulsar://localhost:6650")
producer = client.create_producer(topic)
producer.send(json.dumps(body).encode("utf-8"))
producer.close()
client.close()
