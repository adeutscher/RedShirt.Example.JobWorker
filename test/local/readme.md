
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

4. Before starting the worker, make sure none of the `USE_` environment variables are set to **1**:

```
export USE_ACTIVEMQ=0
export USE_KINESIS=0
export USE_AZURE_QUEUE_STORAGE=0
export USE_NATS=0
export USE_RABBITMQ=0
```

5. Bring up the worker:

    ```
    docker compose up worker
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

4. Before starting the worker, make sure that neither the `USE_KINESIS` is set to `1` and that other `USE_` environment variables are not set to `1`:

    ```
    export USE_ACTIVEMQ=0
    export USE_KINESIS=1
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_NATS=0
    export USE_RABBITMQ=0
    ```

5. Bring up the worker:

    ```
    docker compose up worker
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

9. Before starting the worker, make sure that the `USE_RABBITMQ` is set to `1` and that other `USE_` environment variables are not set to `1`:

    ```
    export USE_RABBITMQ=0
    export USE_KINESIS=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_NATS=0
    export USE_RABBITMQ=1
    ```

10. Bring up the worker:

    ```
    docker compose up worker
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

11. To insert a new message, you can do one of the following:

    * Use the `send-activemq-message.py` script (requires the `stomp.py` Python library)

    ```
    ./send-activemq-message.py 12
    ```

    * In Artemis JMX select the ActiveQueue address and then select the 'Send Message' tab. Example of a message JSON:

    ```
    {"SleepDurationSeconds": 12}
    ```

12. Before starting the worker, make sure that neither the `USE_ACTIVEMQ` is set to `1` and that other `USE_` environment variables are not set to `1`:

    ```
    export USE_ACTIVEMQ=1
    export USE_KINESIS=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_NATS=0
    export USE_RABBITMQ=0
    ```

13. Bring up the worker:

    ```
    docker compose up worker
    ```

## NATS

NATS takes a few more steps to set up than the other input sources. It will also require the installation of the `nats` command.

### CLI Installation

To install the `nats` command:

1. Go to the NATS Releases page on GitHub ([Link](https://github.com/nats-io/natscli/releases/)).
2. Download the package format of your choice.
3. Install
    * If you chose a package such as an `.rpm`, install using your package manager.
    * If you chose a `.zip` archive, unpack it to a location and add that location to your `$PATH` variable.

### Testing Messages

1. Bring up localstack:

    ```
    docker compose up -d localstack
    ```

2. Run the `make-local-resources.sh` script:

    ```
    ./make-local-resources.sh
    ```
3. Bring up NATS:

    ```
    docker compose up -d nats
    ```
4. Create your stream:

    ```
    NATS_URL=nats://admin:admin@localhost:4222 nats stream add TestStream --subjects foo --replicas 1 --defaults
    ```

5. To insert a new sample message to sleep for 5 seconds:

    ```
    ./send-nats-message.sh 5
    ```

6. Before starting the worker, make sure that the `USE_NATS` is set to `1` and that other `USE_` environment variables are not set to `1`:

    ```
    export USE_NATS=1
    export USE_ACTIVEMQ=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    ```

7. Bring up the worker:

    ```
    docker compose up worker
    ```

## Azure Queue Storage

Testing `Azure Queue Storage` will require:

* Visual Studio Code to be installed
* Within Visual Studio Code (VSCode), the `Azure Tools` and `Azureite` extensions must be installed.
* Azure Storage Explorer to be downloaded from [here](https://azure.microsoft.com/en-us/products/storage/storage-explorer)

For more information on using Visual Studio Code to interact with `azurite`, see [here](https://rajeevpentyala.com/2025/08/16/azurite-build-azure-queues-and-functions-locally-with-c/)

### VSCode Configuration

VSCode automatically knows how to point to your local `azurite` server after the service is started.


### Testing Messages

1. Bring up `azureite`:

    ```
    docker compose up -d `azurite`
    ```

2. In VSCode, go to the Azure tab.

3. Look down in the `Workspace` section

4. Create the `test-azure-queue` queue.

5. Azure Storage Explorer should allow you to access the storage account for Azurite's `devstoreaccount1` without any configuration. After selecting the `test-azure-queue` queue, you can add a message to the queue. Please note that Storage Explorer's Add menu **stores the message as a Base64-encoded string by default**. So far, this seems to be unique to Storage Explorer. Because of this, **this template does not go out of its way to account for Base64**. However, but you may wish to consider it if you are adapting this into an application that uses Azure Queue Storage. Any messages added via Storage Explorer should be stored as **Plain UTF-8**. Message format.

    ```
    {"SleepDurationSeconds": 12}
    ```

6. Before starting the worker, make sure that the `USE_AZURE_QUEUE_STORAGE` is set to `1` and that other `USE_` environment variables are not set to `1`:

    ```
    export USE_AZURE_QUEUE_STORAGE=1
    export USE_NATS=0
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    ```

7. Bring up the worker:

    ```
    docker compose up worker
    ```