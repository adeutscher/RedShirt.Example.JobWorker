
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

4. Before starting the worker, make sure that neither the `USE_KINESIS`, `USE_ACTIVEMQ`, or `USE_RABBITMQ` variables are set to 1:

```
export USE_ACTIVEMQ=0
export USE_KINESIS=0
export USE_RABBITMQ=0
```

## Kinesis

To initialize Kinesis and queue sample messages:

1. Bring up localstack and Redis:

    ```
    docker compose up -d localstack redis
    ```

2. Run the `make-local-resources.sh` script:

    ```
    ./make-local-resources.sh
    ```

3. Use the `send-sqs-message.py` script to send a message into SQS. Specify the number of seconds the worker should sleep for in the first argument:

    ```
    ./put-kinesis-job.py 12
    ```

4. Before starting the worker, make sure that neither the `USE_KINESIS` is set to `1` and that the `USE_ACTIVEMQ` and `USE_RABBITMQ` variables are not set to `1`:

    ```
    export USE_ACTIVEMQ=0
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

2. Run the `make-local-resources.sh` script:

    ```
    ./make-local-resources.sh
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

9. Before starting the worker, make sure that the `USE_RABBITMQ` is set to `1` and neither the `USE_KINESIS` or `USE_ACTIVEMQ` variables are set to `1`:

    ```
    export USE_RABBITMQ=0
    export USE_KINESIS=0
    export USE_RABBITMQ=1
    ```

## ActiveMQ

ActiveMQ Artemis takes a few more steps to set up than the other input sources.

To initialize RabbitMQ and queue messages:

1. Bring up localstack:

    ```
    docker compose up -d localstack
    ```

2. Run the `make-local-resources.sh` script:

    ```
    ./make-local-resources.sh
    ```

3. Bring up ActiveMQ:

    ```
    docker compose up -d activemq
    ```

4. Go to http://localhost:8161/

5. Sign in with the username 'admin' and password 'admin'.

6. Select the 'Addresses' tab.

7. Create a new multicast address named `/queue/ActiveQueue`.

8. Go to Artemis JMX and select our newly-created `/queue/ActiveQueue` address.

9. Within the Artemis JMX menu for `/queue/ActiveQueue` address, go to the Create Queue tab.

10. Create a multicast queue named `/queue/ActiveQueue`.

8. To insert a new message, you can do one of the following:

    * Use the `send-activemq-message.py` script (requires the `stomp.py` Python library)

    ```
    ./send-activemq-message.py 12
    ```

    * In Artemis JMX select the ActiveQueue address and then select the 'Send Message' tab. Example of a message JSON:

    ```
    {"SleepDurationSeconds": 12}
    ```

9. Before starting the worker, make sure that neither the `USE_ACTIVEMQ` is set to `1` and that the `USE_KINESIS` and `USE_RABBITMQ` variables are not set to `1`:

    ```
    export USE_ACTIVEMQ=1
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    ```