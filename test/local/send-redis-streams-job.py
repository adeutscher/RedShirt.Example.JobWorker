#!/usr/bin/env python

import json
import sys

import redis

stream_name = 'jobs'
body = {'SleepDurationSeconds': int(sys.argv[1])}
values = {'body': json.dumps(body)}

if len(sys.argv) > 2:
    values['message_id'] = sys.argv[2]

client = redis.Redis(host='localhost', port=6379, decode_responses=True)
client.xadd(stream_name, values)
