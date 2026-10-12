#!/usr/bin/env python

import os

import pika

queue_name = 'RabbitQueue'
exchange_name = 'RabbitExchange'

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

channel.exchange_declare(exchange=exchange_name, exchange_type='headers', durable=True)
print(f"Ensured exchange '{exchange_name}' exists.")

connection.close()
