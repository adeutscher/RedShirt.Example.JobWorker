#!/usr/bin/env python

import sys

import redis

key = sys.argv[1]
client = redis.Redis(host='localhost', port=6379, decode_responses=True)
value = client.get(key)
print(value)
