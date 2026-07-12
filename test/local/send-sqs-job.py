#!/usr/bin/env python

import boto3
import json
import sys

sqs = boto3.client(
    'sqs',
    endpoint_url='http://localhost:4566',
    region_name='us-east-1',
    aws_access_key_id='test',
    aws_secret_access_key='test',
)

body = {'SleepDurationSeconds': int(sys.argv[1])}
queue_url = 'http://localhost:4566/000000000000/input'
sqs.send_message(QueueUrl=queue_url, MessageBody=json.dumps(body))
