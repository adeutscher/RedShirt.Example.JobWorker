#!/usr/bin/env python

import redis

# Connect to localhost on default port 6379
client = redis.Redis(host='localhost', port=6379, decode_responses=True)

def enumerate_keys(pattern='*'):
    cursor = 0
    while True:
        # SCAN returns a tuple of (new_cursor, list_of_keys)
        cursor, keys = client.scan(cursor=cursor, match=pattern, count=100)
        for key in keys:
            print(key)
        if cursor == 0:
            break

if __name__ == '__main__':
    enumerate_keys()
