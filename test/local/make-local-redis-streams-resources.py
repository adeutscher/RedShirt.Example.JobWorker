#!/usr/bin/env python

import redis

stream_name = 'jobs'
group_name = 'job-worker'

client = redis.Redis(host='localhost', port=6379, decode_responses=True)

try:
    client.xgroup_create(name=stream_name, groupname=group_name, id='0-0', mkstream=True)
    print(f"Created consumer group '{group_name}' on stream '{stream_name}'.")
except redis.ResponseError as e:
    if 'BUSYGROUP' in str(e):
        print(f"Consumer group '{group_name}' already exists on stream '{stream_name}'.")
    else:
        raise
