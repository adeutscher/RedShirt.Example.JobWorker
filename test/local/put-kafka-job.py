#!/usr/bin/env python

import json
import sys

from kafka import KafkaProducer

producer = KafkaProducer(
    bootstrap_servers=['localhost:9092'],
    value_serializer=lambda v: json.dumps(v).encode('utf-8'),
)

body = {'SleepDurationSeconds': int(sys.argv[1])}
topic = 'jobs'
producer.send(topic, body)
producer.flush()
