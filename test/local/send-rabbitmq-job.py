#!/usr/bin/env python

import json
import sys

import pika

queue_name = 'RabbitQueue'
body = {'SleepDurationSeconds': int(sys.argv[1])}
properties = pika.BasicProperties(content_type='application/json')

if len(sys.argv) > 2:
    properties.message_id = sys.argv[2]

credentials = pika.PlainCredentials('foo', 'bar')
connection = pika.BlockingConnection(
    pika.ConnectionParameters(host='localhost', credentials=credentials))
channel = connection.channel()

channel.basic_publish(
    exchange='',
    routing_key=queue_name,
    body=json.dumps(body),
    properties=properties)

connection.close()
