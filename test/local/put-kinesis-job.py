#!/usr/bin/env python

import boto3
import json
import sys

kinesis = boto3.client('kinesis', endpoint_url='http://localstack:4566')

body = {'SleepDurationSeconds': int(sys.argv[1])}
stream_arn = 'arn:aws:kinesis:us-east-1:000000000000:stream/input'
kinesis.put_record(StreamARN=stream_arn, PartitionKey='foo', Data=bytes(json.dumps(body), 'utf-8'))