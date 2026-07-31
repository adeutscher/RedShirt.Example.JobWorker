#!/bin/bash

export AWS_DEFAULT_REGION=us-east-1

# Common
awslocal ssm put-parameter --overwrite --type String --name /common/redis --value "redis:6379"

# SQS
awslocal sqs create-queue --queue-name input

# Kinesis
awslocal kinesis create-stream --stream-name input
awslocal sqs create-queue --queue-name kinesis-failures

# Kafka
awslocal sqs create-queue --queue-name kafka-failures

awslocal dynamodb create-table --table-name checkpoint \
        --attribute-definitions AttributeName=ShardId,AttributeType=S \
        --key-schema AttributeName=ShardId,KeyType=HASH \
        --provisioned-throughput ReadCapacityUnits=5,WriteCapacityUnits=5
awslocal dynamodb update-time-to-live --table-name checkpoint \
                --time-to-live-specification Enabled=true,AttributeName=ExpirationTime

# RabbitMQ

awslocal ssm put-parameter --overwrite --type String --name /rabbitmq/user --value foo
awslocal ssm put-parameter --overwrite --type String --name /rabbitmq/password --value bar

# ActiveMQ Artemis

awslocal ssm put-parameter --overwrite --type String --name /activemq/user --value admin
awslocal ssm put-parameter --overwrite --type String --name /activemq/password --value admin

# NATS

awslocal ssm put-parameter --overwrite --type String --name /nats/user --value admin
awslocal ssm put-parameter --overwrite --type String --name /nats/password --value admin
