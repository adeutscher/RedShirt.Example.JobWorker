#!/usr/bin/env python

import json
import stomp # pip install stomp.py
import sys

# Connection configuration
# Default STOMP port for ActiveMQ is 61616
conn = stomp.Connection([('localhost', 61616)])

# Connect with credentials
conn.connect('admin', 'admin', wait=True)

# Send a message to a queue
# The destination must be prefixed with '/queue/' or '/topic/'
body = {'SleepDurationSeconds': int(sys.argv[1])}
queue = '/queue/ActiveQueue'
# Note: This technically sends a binary payload.
#       The worker is still able to handle this.
conn.send(body=json.dumps(body), destination=queue)

# Disconnect
conn.disconnect()
print("Message sent successfully")
