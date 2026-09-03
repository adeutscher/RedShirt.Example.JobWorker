#!/bin/bash

export AWS_DEFAULT_REGION=us-east-1

# Common
awslocal ssm put-parameter --overwrite --type String --name /common/redis --value "redis:6379"

# SQS
awslocal sqs create-queue --queue-name input

# Kinesis
awslocal kinesis create-stream --stream-name input
awslocal sqs create-queue --queue-name kinesis-failures

awslocal dynamodb create-table --table-name checkpoint \
        --attribute-definitions AttributeName=ShardId,AttributeType=S \
        --key-schema AttributeName=ShardId,KeyType=HASH \
        --provisioned-throughput ReadCapacityUnits=5,WriteCapacityUnits=5
awslocal dynamodb update-time-to-live --table-name checkpoint \
                --time-to-live-specification Enabled=true,AttributeName=ExpirationTime

# Kafka
awslocal sqs create-queue --queue-name kafka-failures

# RabbitMQ

awslocal ssm put-parameter --overwrite --type String --name /rabbitmq/user --value "${RABBITMQ_DEFAULT_USER:-foo}"
awslocal ssm put-parameter --overwrite --type String --name /rabbitmq/password --value "${RABBITMQ_DEFAULT_PASS:-bar}"

# ActiveMQ Artemis

awslocal ssm put-parameter --overwrite --type String --name /activemq/user --value admin
awslocal ssm put-parameter --overwrite --type String --name /activemq/password --value admin

# NATS

awslocal ssm put-parameter --overwrite --type String --name /nats/user --value admin
awslocal ssm put-parameter --overwrite --type String --name /nats/password --value admin

# Bar OAuth (WireMock bar connector)

awslocal ssm put-parameter --overwrite --type String \
    --name /bar/oauth/client-id \
    --value "local-bar-client-id"

awslocal ssm put-parameter --overwrite --type String \
    --name /bar/oauth/client-secret \
    --value "local-bar-client-secret"
