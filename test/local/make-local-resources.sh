#!/bin/bash

awslocal sqs create-queue --queue-name input

awslocal kinesis create-stream --stream-name input
awslocal sqs create-queue --queue-name kinesis-failures

awslocal dynamodb create-table --table-name checkpoint \
        --attribute-definitions AttributeName=ShardId,AttributeType=S \
        --key-schema AttributeName=ShardId,KeyType=HASH \
        --provisioned-throughput ReadCapacityUnits=5,WriteCapacityUnits=5
awslocal dynamodb update-time-to-live --table-name checkpoint \
                --time-to-live-specification Enabled=true,AttributeName=ExpirationTime