#!/usr/bin/env python

import os

import pika

queue_name = 'RabbitQueue'

credentials = pika.PlainCredentials(
    os.environ.get('RABBITMQ_DEFAULT_USER', 'foo'),
    os.environ.get('RABBITMQ_DEFAULT_PASS', 'bar'),
)
connection = pika.BlockingConnection(
    pika.ConnectionParameters(host='localhost', credentials=credentials)
)
channel = connection.channel()

channel.queue_declare(queue=queue_name, durable=True)
print(f"Ensured queue '{queue_name}' exists.")

connection.close()
