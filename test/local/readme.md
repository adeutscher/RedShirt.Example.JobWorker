
# Usage

## SQS

To initialize SQS and queue sample messages:

1. Bring up localstack:

```
docker compose up -d localstack
```

2. Run the `make-local-resources.sh` script:

```
./make-local-resources.sh
```

3. Use the `send-sqs-message.py` script to send a message into SQS. Specify the number of seconds the worker should sleep for in the first argument:

```
./send-sqs-message.py 12
```

4. Before starting the worker, make sure that neither the `USE_KINESIS` or `USE_RABBITMQ` variables are set to 1:

```
export USE_KINESIS=0
export USE_RABBITMQ=0
```

## Kinesis

To initialize Kinesis and queue sample messages:

1. Bring up localstack:

```
docker compose up -d localstack
```

2. Run the `make-local-resources.sh` script:

```
./make-local-resources.sh
```

3. Use the `send-sqs-message.py` script to send a message into SQS. Specify the number of seconds the worker should sleep for in the first argument:

```
./put-kinesis-job.py 12
```

4. Before starting the worker, make sure that neither the `USE_KINESIS` is set to `1` and `USE_RABBITMQ` variables is not set to `1`:

```
export USE_KINESIS=1
export USE_RABBITMQ=0
```

## RabbitMQ

RabbitMQ takes a few more steps to set up than the other input sources.

To initialize RabbitMQ and queue messages:

1. Bring up localstack:

```
docker compose up -d localstack
```

3. Bring up RabbitMQ:

```
docker compose up -d rabbitmq
```

4. Go to http://localhost:15672/

5. Sign in with the username 'foo' and password 'bar'.

6. Select the 'Queues and Streams' tab.

7. Create a new queue named `RabbitQueue`. Leave all other options at default.

8. Rather than cook up a new script for inserting messages, we will be using the Web GUI to submit messages for the moment. To insert a message into the queue, select `RabbitQueue` from the queue list and open the 'Publish message' section. Example of a message JSON:

```
{"SleepDurationSeconds": 12}
```

9. Before starting the worker, make sure that neither the `USE_RABBITMQ` is set to `1` and `USE_KINESIS` variables is not set to `1`:

```
export USE_KINESIS=0
export USE_RABBITMQ=1
```