
# Usage

Instructions for local testing.

## General

The scripts described below assume that certain Python modules are installed in your environment.

Run the following to install all of the assumed modules at once:

```bash
pip install --user boto3 awscli awslocal stomp.py azure-cli azure.servicebus azure-storage-queue azure.identity azure.keyvault kafka-python redis pika google-cloud-pubsub pulsar-client
```

## Idempotency

Idempotency testing relies on having a reliable way of setting a message ID.
If this template is still in general form, then I would advise testing using the RabbitMQ job source.

## Loader Mode

To enable loader mode in the general template, set `JOBS__LOADER_MODE__ENABLED` to `true`:

```bash
export JOBS__LOADER_MODE__ENABLED=true
```

Optionally raise `JOBS__LOADER_MODE__MINIMUM_BATCH_SIZE` so the loader waits until enough free backlog slots exist
before polling (default effective minimum is `1`):

```bash
export JOBS__LOADER_MODE__MINIMUM_BATCH_SIZE=5
```

## Message Sources

### SQS

To initialize SQS and queue sample messages:

1. Bring up ministack, Redis, and `wiremock-bar`:

```bash
docker compose up -d ministack redis wiremock-bar
```

2. Run the `make-local-aws-resources.sh` script:

```bash
./make-local-aws-resources.sh
```

3. Use the `send-sqs-message.py` script to send a message into SQS. Specify the number of seconds the worker should sleep for in the first argument:

```bash
./send-sqs-message.py 12
```

4. Before starting the worker, make sure none of the `USE_` environment variables are set to **1**, unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_KAFKA=0
    export USE_PULSAR=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_REDIS_STREAMS=0
    export USE_RABBITMQ=0
    export USE_GOOGLE_PUB_SUB=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

5. Bring up the worker:

    ```bash
    docker compose up worker
    ```

### Kinesis

To initialize Kinesis and queue sample messages:

1. Bring up ministack, Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d ministack redis wiremock-bar
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```bash
    ./make-local-aws-resources.sh
    ```

3. Use the `send-sqs-message.py` script to send a message into SQS. Specify the number of seconds the worker should sleep for in the first argument:

    ```bash
    ./put-kinesis-job.py 12
    ```

4. Before starting the worker, make sure that neither the `USE_KINESIS` is set to `1` and that other `USE_` environment variables are not set to `1`.
   Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_ACTIVEMQ=0
    export USE_KINESIS=1
    export USE_KAFKA=0
    export USE_PULSAR=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_REDIS_STREAMS=0
    export USE_RABBITMQ=0
    export USE_RABBITMQ_SUBSCRIBE=0
    export USE_GOOGLE_PUB_SUB=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

5. Bring up the worker:

    ```bash
    docker compose up worker
    ```

### Kafka

To initialize Kafka and queue sample messages:

1. Bring up ministack, Kafka, Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d ministack kafka redis wiremock-bar
    ```

2. Run the `make-local-aws-resources.sh` script (creates the SQS queue used for Kafka job failures):

    ```bash
    ./make-local-aws-resources.sh
    ```

3. Use the `put-kafka-job.py` script (requires the `kafka-python` module) to publish a message to the `jobs` topic. Specify the number of seconds the worker should sleep for in the first argument:

    ```bash
    ./put-kafka-job.py 12
    ```

4. Before starting the worker, make sure that `USE_KAFKA` is set to `1` and that other `USE_` environment variables are not set to `1`.
    Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_KAFKA=1
    export USE_PULSAR=0
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_REDIS_STREAMS=0
    export USE_RABBITMQ=0
    export USE_RABBITMQ_SUBSCRIBE=0
    export USE_GOOGLE_PUB_SUB=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

5. Bring up the worker:

    ```bash
    docker compose up worker
    ```

### Apache Pulsar

To initialize Apache Pulsar and queue sample messages:

1. Bring up ministack, Pulsar, Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d ministack pulsar redis wiremock-bar
    ```

2. Run the `make-local-aws-resources.sh` script (creates shared local AWS resources such as Redis SSM params):

    ```bash
    ./make-local-aws-resources.sh
    ```

3. Wait for Pulsar to become ready, then create the local topic with `make-local-pulsar-resources.py` (uses the admin HTTP API; no extra Python packages):

    ```bash
    ./make-local-pulsar-resources.py
    ```

4. Use the `send-pulsar-job.py` script (requires the `pulsar-client` module) to publish a message to `persistent://public/default/jobs`. Specify the number of seconds the worker should sleep for in the first argument:

    ```bash
    ./send-pulsar-job.py 12
    ```

5. Before starting the worker, make sure that `USE_PULSAR` is set to `1` and that other `USE_` environment variables are not set to `1`.
    Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_PULSAR=1
    export USE_KAFKA=0
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_RABBITMQ=0
    export USE_RABBITMQ_SUBSCRIBE=0
    export USE_REDIS_STREAMS=0
    export USE_GOOGLE_PUB_SUB=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

6. Bring up the worker:

    ```bash
    docker compose up worker
    ```

Pulsar dead-letter handling uses the client `DeadLetterPolicy` (`JOB_SOURCE__PULSAR__MAX_REDELIVER_COUNT`, default `3`). Failed jobs are negatively acknowledged so they redeliver into that policy; unacknowledged messages also become eligible for redelivery after `JOB_SOURCE__PULSAR__ACK_TIMEOUT_SECONDS` (default `300`). Undeliverable messages that exceed the redelivery count are moved to Pulsar's dead letter topic (default name `{topic}-{subscription}-DLQ`).

### RabbitMQ

RabbitMQ takes a few more steps to set up than the other input sources.

To initialize RabbitMQ and queue messages:

1. Bring up ministack, Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d ministack redis wiremock-bar
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```bash
    ./make-local-aws-resources.sh
    ```

3. Bring up RabbitMQ:

    ```bash
    docker compose up -d rabbitmq
    ```

4. Create the RabbitMQ queue (safe to re-run if it already exists). This requires the `pika` Python module:

    ```bash
    ./make-local-rabbitmq-resources.py
    ```

5. Use the `send-rabbitmq-job.py` script to publish a message to the `RabbitQueue` queue. Specify the number of seconds the worker should sleep for in the first argument. You may optionally provide a second argument to set the AMQP `message_id` property for idempotency testing:

    ```bash
    ./send-rabbitmq-job.py 12
    ./send-rabbitmq-job.py 12 example-idempotency-key
    ```

6. Before starting the worker, make sure that the `USE_RABBITMQ` is set to `1` and that other `USE_` environment variables are not set to `1`.
   Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_RABBITMQ=1
    export USE_RABBITMQ_SUBSCRIBE=false
    export USE_KINESIS=0
    export USE_KAFKA=0
    export USE_PULSAR=0
    export USE_ACTIVEMQ=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_REDIS_STREAMS=0
    export USE_GOOGLE_PUB_SUB=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

7. By default, RabbitMQ uses a short polling strategy. If you want to have RabbitMQ instead subscribe to a queue, then set `JOB_SOURCE__RABBITMQ__SUBSCRIBE`:

    ```bash
    export JOB_SOURCE__RABBITMQ__SUBSCRIBE=true
    ```
8. Bring up the worker:

    ```bash
    docker compose up worker
    ```

#### Credential Rotation

The following chain of commands might be useful if you are testing credential rotation combined with connection problems:

```bash
export RABBITMQ_DEFAULT_PASS=$(uuidgen); docker compose down rabbitmq; docker compose up -d rabbitmq ; sleep 4; ./make-local-rabbitmq-resources.py; awslocal ssm put-parameter --overwrite --type String --name /rabbitmq/password --value "${RABBITMQ_DEFAULT_PASS}"
```

### ActiveMQ

ActiveMQ Artemis takes a few more steps to set up than the other input sources.

To initialize ActiveMQ and queue messages:

1. Bring up ministack, Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d ministack redis wiremock-bar
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```bash
    ./make-local-aws-resources.sh
    ```

3. Bring up ActiveMQ:

    ```bash
    docker compose up -d activemq
    ```

    The local compose service raises Artemis `max-disk-usage` to `99` so publishes are not
    blocked when the host disk is already past the stock `90` threshold.

4. Create the ActiveMQ anycast queue (safe to re-run if it already exists). Uses the Artemis Jolokia HTTP API (stdlib only; no extra Python packages):

    ```bash
    ./make-local-activemq-resources.py
    ```

5. Use the `send-activemq-message.py` script to publish a message to the `/queue/ActiveQueue` queue. This requires the `stomp.py` Python module. Specify the number of seconds the worker should sleep for in the first argument. You may optionally provide a second argument to set the STOMP `correlation-id` header for idempotency testing:

    ```bash
    ./send-activemq-message.py 12
    ./send-activemq-message.py 12 example-idempotency-key
    ```

6. Before starting the worker, make sure that `USE_ACTIVEMQ` is set to `1` and that other `USE_` environment variables are not set to `1`.
    Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_ACTIVEMQ=1
    export USE_KINESIS=0
    export USE_KAFKA=0
    export USE_PULSAR=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_REDIS_STREAMS=0
    export USE_RABBITMQ=0
    export USE_GOOGLE_PUB_SUB=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

7. By default, ActiveMQ uses short polling. To subscribe with an async listener instead, set `JOB_SOURCE__ACTIVEMQ__SUBSCRIBE`:

    ```bash
    export JOB_SOURCE__ACTIVEMQ__SUBSCRIBE=true
    ```

8. Bring up the worker:

    ```bash
    docker compose up worker
    ```

#### Alternative: Web Console Testing

If you prefer not to use the Python scripts, you can create the address/queue and send messages from the Artemis web console:

1. Go to http://localhost:8161/

2. Sign in with the username `admin` and password `admin`.

3. Select the **Addresses** tab.

4. Create a new multicast address named `/queue/ActiveQueue`.

5. As the list of addresses does not automatically refresh, navigate away from the **Addresses** tab and then return to the **Addresses** tab.

6. Go to Artemis JMX by selecting it as an option in the menu generated by clicking the 3-dot icon for the newly-created multi-cast address.

7. In Artemis JMX, select the newly-created `/queue/ActiveQueue` address.

8. Within the Artemis JMX menu for the `/queue/ActiveQueue` address, go to the **Create Queue** tab.

9. Create an anycast queue named `/queue/ActiveQueue`.

10. To insert a new message, in Artemis JMX select the ActiveQueue address and then select the **Send Message** tab. Example message JSON:

    ```json
    {"SleepDurationSeconds": 12}
    ```

### NATS

NATS takes a few more steps to set up than the other input sources. It will also require the installation of the `nats` command.

#### CLI Installation

To install the `nats` command:

1. Go to the NATS Releases page on GitHub ([Link](https://github.com/nats-io/natscli/releases/)).
2. Download the package format of your choice.
3. Install
    * If you chose a package such as an `.rpm`, install using your package manager.
    * If you chose a `.zip` archive, unpack it to a location and add that location to your `$PATH` variable.

#### Testing Messages

1. Bring up ministack, Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d ministack redis wiremock-bar
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```bash
    ./make-local-aws-resources.sh
    ```
3. Bring up NATS:

    ```bash
    docker compose up -d nats
    ```
4. Create your stream:

    ```bash
    NATS_URL=nats://admin:admin@localhost:4222 nats stream add TestStream --subjects foo --replicas 1 --defaults
    ```

5. To insert a new sample message to sleep for 5 seconds:

    ```bash
    ./send-nats-message.sh 5
    ```

6. Before starting the worker, make sure that the `USE_NATS` is set to `1` and that other `USE_` environment variables are not set to `1`.
   Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_NATS=1
    export USE_ACTIVEMQ=0
    export USE_KAFKA=0
    export USE_PULSAR=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_KINESIS=0
    export USE_REDIS_STREAMS=0
    export USE_RABBITMQ=0
    export USE_RABBITMQ_SUBSCRIBE=0
    export USE_GOOGLE_PUB_SUB=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

7. By default, NATS uses short polling (`NextAsync` / `FetchNoWaitAsync`). To consume continuously via JetStream `ConsumeAsync` instead, set `JOB_SOURCE__NATS__SUBSCRIBE`:

    ```bash
    export JOB_SOURCE__NATS__SUBSCRIBE=true
    ```

8. Bring up the worker:

    ```bash
    docker compose up worker
    ```

### Redis Streams

Redis Streams testing requires the `redis` Python module to be installed.

#### Testing Messages

1. Bring up ministack, Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d ministack redis wiremock-bar
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```bash
    ./make-local-aws-resources.sh
    ```

3. Create the Redis stream consumer group (creates the `jobs` stream if needed). This is safe to re-run if the group already exists:

    ```bash
    ./make-local-redis-streams-resources.py
    ```

4. Use the `send-redis-streams-job.py` script to publish a message to the `jobs` stream. Specify the number of seconds the worker should sleep for in the first argument. You may optionally provide a second argument to set the `message_id` field for idempotency testing:

    ```bash
    ./send-redis-streams-job.py 12
    ./send-redis-streams-job.py 12 example-idempotency-key
    ```

5. Before starting the worker, make sure that `USE_REDIS_STREAMS` is set to `1` and that the other `USE_` environment variables are not set to `1`.
   Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_REDIS_STREAMS=1
    export USE_NATS=0
    export USE_ACTIVEMQ=0
    export USE_KAFKA=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    export USE_RABBITMQ_SUBSCRIBE=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

6. Bring up the worker:

    ```bash
    docker compose up worker
    ```

### Azure Queue Storage

Testing `Azure Queue Storage` with the below instructions will require the various Azure-related python module to be installed:

```bash
pip install azure-cli azure-storage-queue azure.identity azure.keyvault
```

#### VSCode

An option for testing `Azure Queue Storage` is to use Visual Studio Code (VSCode):

* Visual Studio Code to be installed
* Within Visual Studio Code (VSCode), the `Azure Tools` and `azurite` extensions must be installed.
* Azure Storage Explorer to be downloaded from [here](https://azure.microsoft.com/en-us/products/storage/storage-explorer)

For more information on using Visual Studio Code to interact with `azurite`, see [here](https://rajeevpentyala.com/2025/08/16/azurite-build-azure-queues-and-functions-locally-with-c/)

Using VSCode *used* to be the documented way of testing Azure Queue Storage messages, but after my own VSCode installation was uncooperative I opted to make a script-based setup. It's more consistent this way too.

##### VSCode Configuration

VSCode automatically knows how to point to your local `azurite` server after the service is started.

#### Testing Messages

1. Run `generate-azure-key-vault-cert.sh` to generate the certificate files necessary for the Azure Key Vault Emulator to work.

    ```bash
    ./generate-azure-key-vault-cert.sh
    ```

2. Bring up `azure-key-vault-emulator` (which shall be holding the connection string for Azure Queue Storage), Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d azure-key-vault-emulator redis wiremock-bar
    ```

3. Run `set-azure-key-vault-secrets.py` to set the connection strings for Azure Queue Storage, Azure Service Bus, and Redis (`common-distributed-redis`) in the Azure Key Vault emulator:

    ```bash
    ./set-azure-key-vault-secrets.py
    ```

4. Bring up `azurite`:

    ```bash
    docker compose up -d azurite
    ```

5. Create the `test-azure-queue` queue (if it does not already exist) and send a job that will tell the worker to sleep for the specified number of seconds:

    ```bash
    ./send-azure-queue-job.py 12
    ```

    The script sends a plain UTF-8 JSON body (`{"SleepDurationSeconds": 12}`). If you instead add messages with Azure Storage Explorer, note that its Add menu **stores the message as a Base64-encoded string by default**. So far, this seems to be unique to Storage Explorer. Because of this, **this template does not go out of its way to account for Base64**. However, you may wish to consider it if you are adapting this into an application that uses Azure Queue Storage. Any messages added via Storage Explorer should be stored as **Plain UTF-8**.

6. Before starting the worker, make sure that the `USE_AZURE_QUEUE_STORAGE` is set to `1` and that other `USE_` environment variables are not set to `1`.
   You will also point Redis at the Key Vault secret name created by `set-azure-key-vault-secrets.py`, as the compose file's default is to use the SSM path (Azure Key Vault key and SSM Parameter Store path formats are entirely incompatible with one another). Set `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` to the Bar OAuth Key Vault secret names from the same script:

    ```bash
    export USE_AZURE_QUEUE_STORAGE=1
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_REDIS_STREAMS=0
    export USE_KAFKA=0
    export USE_PULSAR=0
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    export USE_RABBITMQ_SUBSCRIBE=0
    export USE_GOOGLE_PUB_SUB=0
    export COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH=common-distributed-redis
    export CONNECTORS__BAR__CLIENT_ID_PATH=bar-oauth-client-id
    export CONNECTORS__BAR__CLIENT_SECRET_PATH=bar-oauth-client-secret
    ```

7. Bring up the worker:

    ```bash
    docker compose up worker
    ```

### Azure Service Bus

Testing `Azure Service Bus` will require the various Azure-related python module to be installed:

```bash
pip install azure.servicebus azure.identity azure.keyvault
```

#### Testing Messages

1. Run `generate-azure-key-vault-cert.sh` to generate the certificate files necessary for the Azure Key Vault Emulator to work.

    ```bash
    ./generate-azure-key-vault-cert.sh
    ```

2. Bring up `azure-key-vault-emulator` (which shall be holding the connection string for Azure Service Bus), Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d azure-key-vault-emulator redis wiremock-bar
    ```

3. Run `set-azure-key-vault-secrets.py` to set the connection strings for Azure Queue Storage, Azure Service Bus, and Redis (`common-distributed-redis`) in the Azure Key Vault emulator:

    ```bash
    ./set-azure-key-vault-secrets.py
    ```

4. Set a value for the `LOCAL_MSSQL_SA_PASSWORD` environment environment variable to be used by the local SQL Server container that we will be spinning up (to set this for long-term, place it in your home directory's `~/.bashrc` file) (password must be at least 8 characters and contain a number and special character):

    ```bash
    export LOCAL_MSSQL_SA_PASSWORD="ExamplePassword1@"
    ```

5. Bring up `azure-service-bus-mssql`, the database back-end used by the service bus emulator:

    ```bash
    docker compose up -d azure-service-bus-mssql
    ```

6. The service bus emulator depends on a configuration file located at `config/azure-service-bus/service-bus-config.json`. Make sure that the container can read it with `chmod`:

    ```bash
    chmod o+r config/azure-service-bus/service-bus-config.json
    ```

7. Bring up `azure-service-bus-emulator`:

    ```bash
    docker compose up -d azure-service-bus-emulator
    ```

8. Give the service bus emulator a moment to start up (the amount of time to wait is controlled by the `SQL_WAIT_INTERVAL` variable in the `docker-compose.yaml` file).

9. The configuration file defined for the service bus emulator defines a queue named `test-queue`. To send to this queue, you can use the provided python script to send a job that will tell the worker to sleep for the specified number of seconds:

    ```bash
    ./send-azure-service-bus-job.py 12
    ```

10. Before starting the worker, make sure that the `USE_AZURE_SERVICE_BUS` is set to `1` and that other `USE_` environment variables are not set to `1`.
    You will also point Redis at the Key Vault secret name created by `set-azure-key-vault-secrets.py`, as the compose file's default is to use the SSM path (Azure Key Vault key and SSM Parameter Store path formats are entirely incompatible with one another). Set `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` to the Bar OAuth Key Vault secret names from the same script:

    ```bash
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=1
    export USE_NATS=0
    export USE_REDIS_STREAMS=0
    export USE_KAFKA=0
    export USE_PULSAR=0
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    export USE_GOOGLE_PUB_SUB=0
    export COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH=common-distributed-redis
    export CONNECTORS__BAR__CLIENT_ID_PATH=bar-oauth-client-id
    export CONNECTORS__BAR__CLIENT_SECRET_PATH=bar-oauth-client-secret
    ```

11. Bring up the worker:

    ```bash
    docker compose up worker
    ```

### Google Pub/Sub

To initialize Google Pub/Sub and queue sample messages:

1. Bring up the Pub/Sub emulator, ministack, Redis, and `wiremock-bar`:

    ```bash
    docker compose up -d google-pubsub-emulator ministack redis wiremock-bar
    ```

2. Run the `make-local-aws-resources.sh` script (creates the Redis SSM parameter used for idempotency):

    ```bash
    ./make-local-aws-resources.sh
    ```

3. Create the local topic and pull subscription (emulator state is in-memory and is lost when the `pubsub` container is recreated):

    ```bash
    ./make-local-google-pubsub-resources.py
    ```

4. Use the `send-google-pubsub-job.py` script (requires the `google-cloud-pubsub` module) to publish a message to the `jobs` topic. Specify the number of seconds the worker should sleep for in the first argument:

    ```bash
    ./send-google-pubsub-job.py 12
    ```

5. Before starting the worker, make sure that `USE_GOOGLE_PUB_SUB` is set to `1` and that other `USE_` environment variables are not set to `1`.
   Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`), and unset `CONNECTORS__BAR__CLIENT_ID_PATH` and `CONNECTORS__BAR__CLIENT_SECRET_PATH` so compose uses the default SSM Bar OAuth paths:

    ```bash
    export USE_GOOGLE_PUB_SUB=1
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_KAFKA=0
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    unset CONNECTORS__BAR__CLIENT_ID_PATH
    unset CONNECTORS__BAR__CLIENT_SECRET_PATH
    ```

6. Bring up the worker:

    ```bash
    docker compose up worker
    ```

## Bar WireMock stubs

`wiremock-bar` mocks the Bar OAuth token endpoint and Bar HTTP API used by
`RedShirt.Example.JobWorker.Connectors.Bar.Implementation` (`BarApiClient` +
`OAuthTokenSource`). Mapping files live under `wiremock/bar/mappings/`. See also
[`docs/bar-connector.md`](../../docs/bar-connector.md) for adapting this connector to a real API.

Default credentials (from `make-local-aws-resources.sh`):

* SSM `/bar/oauth/client-id` → `local-bar-client-id`
* SSM `/bar/oauth/client-secret` → `local-bar-client-secret`
* Access token returned by the token stub → `local-bar-access-token`

Compose points the worker at `http://wiremock-bar:8080` for both `BaseUrl` and
`TokenUrl` (`…/oauth/token`), with scope form field `audience=https://bar.local/api`.
From the host use `http://localhost:9101`.

| Method | Path                       | Auth / body                                                             | Result                                                    |
|--------|----------------------------|-------------------------------------------------------------------------|-----------------------------------------------------------|
| POST   | `/oauth/token`             | form: `grant_type=client_credentials`, valid client id/secret, audience | 200 with `access_token` + `expires_in`                    |
| POST   | `/oauth/token`             | anything else                                                           | 401 `invalid_client`                                      |
| POST   | `/api/bar`                 | `Authorization: Bearer local-bar-access-token`                          | 200 with `{ "Id": <random int>, "Name": <request Name> }` |
| GET    | `/api/bar/{id}`            | valid Bearer                                                            | 200 with `{ "Id": {id}, "Name": "Bar-{id}" }`             |
| GET    | `/api/bar/404`             | valid Bearer                                                            | 404 (exercises not-found handling)                        |
| any    | `/api/bar` or `/api/bar/…` | missing/invalid Bearer                                                  | 401                                                       |

Bring up WireMock with ministack (for SSM) and Redis before running jobs that call Bar:

```bash
docker compose up -d ministack redis wiremock-bar
./make-local-aws-resources.sh
```

### Secret paths: SSM vs Azure Key Vault

By default, compose uses SSM Parameter Store paths (`/bar/oauth/client-id`, `/bar/oauth/client-secret`).
Azure Key Vault secret names cannot contain slashes; when testing with the Key Vault emulator instead of SSM,
set Key Vault–friendly paths before starting the worker:

```bash
export CONNECTORS__BAR__CLIENT_ID_PATH=bar-oauth-client-id
export CONNECTORS__BAR__CLIENT_SECRET_PATH=bar-oauth-client-secret
```

Seed those secrets with `set-azure-key-vault-secrets.py` (which sets `bar-oauth-client-id` and
`bar-oauth-client-secret` alongside the Azure queue/service bus and Redis entries). To return to SSM-backed
local testing, **unset** those overrides so compose falls back to the default `/bar/oauth/…` paths:

```bash
unset CONNECTORS__BAR__CLIENT_ID_PATH
unset CONNECTORS__BAR__CLIENT_SECRET_PATH
```

### Testing Unauthorized Behaviour

To put an invalid client secret in SSM (token endpoint will 401 once credentials are refreshed):

```bash
./scripts/wiremock-bar/bar-set-ssm-oauth-secret.sh 'bogus-secret-value-here'
```

Same caching caveat as other OAuth samples: a successfully obtained bearer token stays cached until it fails or expires. Setting a bad secret in SSM alone does not invalidate an already-cached token. To force WireMock to reject the current token (and exercise refresh), use the rotation script below so the worker's cached token no longer matches WireMock's Authorization matcher—or restart the worker after changing secrets.

Local Compose defaults `COMMON__SECRETS__CACHE__FORCE_COOLDOWN_SECONDS` and
`CONNECTORS__BAR__TOKEN_REFRESH_COOLDOWN_SECONDS` to short values so credential rotation can
recover on the next request. The rotate script waits briefly for those windows to
elapse before returning.

### Testing Credential / Token Rotations

To update the client secret in SSM *and* WireMock's in-memory stubs (token bodyPatterns, returned `access_token`, and API `Authorization` matchers):

```bash
./scripts/wiremock-bar/bar-rotate-oauth-credentials.sh
# or:
./scripts/wiremock-bar/bar-rotate-oauth-credentials.sh 'my-new-secret' 'my-new-access-token'
```

This only updates in-memory WireMock stubs. Restarting `wiremock-bar` restores the mapping files under `wiremock/bar/`. After the script finishes, process another job — the connector should 401 once with the old bearer, refresh client credentials + token, then succeed with the rotated bearer.
