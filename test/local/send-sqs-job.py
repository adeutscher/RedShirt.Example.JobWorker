#!/usr/bin/env python

import boto3
import json
import sys

sqs = boto3.client('sqs', endpoint_url='http://localstack:4566')

body = {'SleepDurationSeconds': int(sys.argv[1])}
queue_url = 'http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/input'
sqs.send_message(QueueUrl=queue_url, MessageBody=json.dumps(body))