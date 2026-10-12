#!/usr/bin/env python

import argparse
import json
import os

import pika

queue_name = 'RabbitQueue'
exchange_name = 'RabbitExchange'


def main() -> None:
    parser = argparse.ArgumentParser(
        description='Publish a JSON test job for the local RabbitMQ job worker.'
    )
    parser.add_argument(
        'sleep_seconds',
        type=int,
        help='Value for SleepDurationSeconds in the job payload',
    )
    parser.add_argument(
        'message_id',
        nargs='?',
        default=None,
        help='Optional AMQP message_id for idempotency testing',
    )
    parser.add_argument(
        '-e',
        '--exchange',
        action='store_true',
        help=f'Publish to the {exchange_name} exchange instead of the {queue_name} queue',
    )
    args = parser.parse_args()

    body = {'SleepDurationSeconds': args.sleep_seconds}
    properties = pika.BasicProperties(content_type='application/json')
    if args.message_id is not None:
        properties.message_id = args.message_id

    credentials = pika.PlainCredentials(
        os.environ.get('RABBITMQ_DEFAULT_USER', 'foo'),
        os.environ.get('RABBITMQ_DEFAULT_PASS', 'bar'),
    )
    connection = pika.BlockingConnection(
        pika.ConnectionParameters(host='localhost', credentials=credentials)
    )
    channel = connection.channel()

    if args.exchange:
        channel.basic_publish(
            exchange=exchange_name,
            routing_key='',
            body=json.dumps(body),
            properties=properties,
        )
        print(
            f"Sent job to exchange '{exchange_name}' "
            f"(SleepDurationSeconds={args.sleep_seconds})."
        )
    else:
        channel.basic_publish(
            exchange='',
            routing_key=queue_name,
            body=json.dumps(body),
            properties=properties,
        )
        print(
            f"Sent job to queue '{queue_name}' "
            f"(SleepDurationSeconds={args.sleep_seconds})."
        )

    connection.close()


if __name__ == '__main__':
    main()
