
# Usage

Instructions for local testing.

## General

The scripts below assume that certain Python modules are installed in your environment.

Run the following to install the assumed modules:

```
pip install --user boto3 awscli awslocal stomp.py azure.servicebus azure.identity azure.keyvault kafka-python
```

## Idempotency

Idempotency testing relies on having a reliable way of setting a message ID.
If this template is still in general form, then I would advise testing using the RabbitMQ job source.

## Message Sources

### SQS

To initialize SQS and queue sample messages:

1. Bring up ministack and Redis:

```
docker compose up -d ministack redis
```

2. Run the `make-local-aws-resources.sh` script:

```
./make-local-aws-resources.sh
```

3. Use the `send-sqs-message.py` script to send a message into SQS. Specify the number of seconds the worker should sleep for in the first argument:

```
./send-sqs-message.py 12
```

4. Before starting the worker, make sure none of the `USE_` environment variables are set to **1**, and unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`):

```
export USE_ACTIVEMQ=0
export USE_KINESIS=0
export USE_KAFKA=0
export USE_AZURE_QUEUE_STORAGE=0
export USE_AZURE_SERVICE_BUS=0
export USE_NATS=0
export USE_RABBITMQ=0
unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
```

5. Bring up the worker:

    ```
    docker compose up worker
    ```

### Kinesis

To initialize Kinesis and queue sample messages:

1. Bring up ministack and Redis:

    ```
    docker compose up -d ministack redis
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```
    ./make-local-aws-resources.sh
    ```

3. Use the `send-sqs-message.py` script to send a message into SQS. Specify the number of seconds the worker should sleep for in the first argument:

    ```
    ./put-kinesis-job.py 12
    ```

4. Before starting the worker, make sure that neither the `USE_KINESIS` is set to `1` and that other `USE_` environment variables are not set to `1`.
   Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`):

    ```
    export USE_ACTIVEMQ=0
    export USE_KINESIS=1
    export USE_KAFKA=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_RABBITMQ=0
    ```

5. Bring up the worker:

    ```
    docker compose up worker
    ```

## Kafka

To initialize Kafka and queue sample messages:

1. Bring up ministack and Kafka:

    ```
    docker compose up -d ministack kafka
    ```

2. Run the `make-local-resources.sh` script (creates the SQS queue used for Kafka job failures):

    ```
    ./make-local-resources.sh
    ```

3. Use the `put-kafka-job.py` script (requires the `kafka-python` module) to publish a message to the `jobs` topic. Specify the number of seconds the worker should sleep for in the first argument:

    ```
    ./put-kafka-job.py 12
    ```

4. Before starting the worker, make sure that `USE_KAFKA` is set to `1` and that other `USE_` environment variables are not set to `1`:

    ```
    export USE_KAFKA=1
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_RABBITMQ=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    ```

5. Bring up the worker:

    ```
    docker compose up worker
    ```

### RabbitMQ

RabbitMQ takes a few more steps to set up than the other input sources.

To initialize RabbitMQ and queue messages:

1. Bring up ministack and Redis:

    ```
    docker compose up -d ministack redis
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```
    ./make-local-aws-resources.sh
    ```

3. Bring up RabbitMQ:

    ```
    docker compose up -d rabbitmq
    ```

4. Go to http://localhost:15672/

5. Sign in with the username 'foo' and password 'bar'.

6. Select the 'Queues and Streams' tab.

7. Create a new queue named `RabbitQueue`. Leave all other options at default.

8. Rather than cook up a new script for inserting messages, we will be using the Web GUI to submit messages for the moment. To insert a message into the queue, select `RabbitQueue` from the queue list and open the 'Publish message' section. Example of a message JSON payload:

    ```
    {"SleepDurationSeconds": 12}
    ```

    * If you are testing idempotency, then remember to also set an arbitrary value to the `message_id` property.

9. Before starting the worker, make sure that the `USE_RABBITMQ` is set to `1` and that other `USE_` environment variables are not set to `1`.
   Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`):

    ```
    export USE_RABBITMQ=0
    export USE_KINESIS=0
    export USE_KAFKA=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_RABBITMQ=1
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    ```

10. Bring up the worker:

    ```
    docker compose up worker
    ```

### ActiveMQ

ActiveMQ Artemis takes a few more steps to set up than the other input sources.

To initialize RabbitMQ and queue messages:

1. Bring up ministack and Redis:

    ```
    docker compose up -d ministack redis
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```
    ./make-local-aws-resources.sh
    ```

3. Bring up ActiveMQ:

    ```
    docker compose up -d activemq
    ```

4. Go to http://localhost:8161/

5. Sign in with the username 'admin' and password 'admin'.

6. Select the 'Addresses' tab.

7. Create a new multicast address named `/queue/ActiveQueue`.

8. As the list of addresses does not automatically refresh, navigate away from the 'Addresses' tab and then return to the 'Addresses' tab.

9. Go to Artemis JMX by selecting it as an option in the menu generated by clicking the 3-dot icon for the newly-created multi-cast address.

10. In Artemis JMX, select the newly-created `/queue/ActiveQueue` address.

11. Within the Artemis JMX menu for `/queue/ActiveQueue` address, go to the Create Queue tab.

12. Create an anycast queue named `/queue/ActiveQueue`.

13. To insert a new message, you can do one of the following:

    * Use the `send-activemq-message.py` script (requires the `stomp.py` Python library)

    ```
    ./send-activemq-message.py 12
    ```

    * In Artemis JMX, select the ActiveQueue address and then select the 'Send Message' tab. Example of a message JSON:

    ```
    {"SleepDurationSeconds": 12}
    ```

14. Before starting the worker, make sure that neither the `USE_ACTIVEMQ` is set to `1` and that other `USE_` environment variables are not set to `1`.
    Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`):

    ```
    export USE_ACTIVEMQ=1
    export USE_KINESIS=0
    export USE_KAFKA=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_RABBITMQ=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    ```

15. Bring up the worker:

    ```
    docker compose up worker
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

1. Bring up ministack and Redis:

    ```
    docker compose up -d ministack redis
    ```

2. Run the `make-local-aws-resources.sh` script:

    ```
    ./make-local-aws-resources.sh
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

6. Before starting the worker, make sure that the `USE_NATS` is set to `1` and that other `USE_` environment variables are not set to `1`.
   Unset `COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH` so compose uses the default SSM path (`/common/redis`):

    ```
    export USE_NATS=1
    export USE_ACTIVEMQ=0
    export USE_KAFKA=0
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    unset COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH
    ```

7. Bring up the worker:

    ```
    docker compose up worker
    ```

### Azure Queue Storage

Testing `Azure Queue Storage` will require:

* Visual Studio Code to be installed
* Within Visual Studio Code (VSCode), the `Azure Tools` and `Azureite` extensions must be installed.
* Azure Storage Explorer to be downloaded from [here](https://azure.microsoft.com/en-us/products/storage/storage-explorer)

For more information on using Visual Studio Code to interact with `azurite`, see [here](https://rajeevpentyala.com/2025/08/16/azurite-build-azure-queues-and-functions-locally-with-c/)

Testing `Azure Queue Storage` will require the various Azure-related python module to be installed:

```
pip install azure.identity azure.keyvault
```

#### VSCode Configuration

VSCode automatically knows how to point to your local `azurite` server after the service is started.

#### Testing Messages

1. Run `generate-azure-key-vault-cert.sh` to generate the certificate files necessary for the Azure Key Vault Emulator to work.

    ```
    ./generate-azure-key-vault-cert.sh
    ```

2. Bring up `azure-key-vault-emulator` (which shall be holding the connection string for Azure Queue Storage) and Redis:

    ```
    docker compose up -d azure-key-vault-emulator redis
    ```

3. Run `set-azure-key-vault-secrets.py` to set the connection strings for Azure Queue Storage, Azure Service Bus, and Redis (`common-distributed-redis`) in the Azure Key Vault emulator:

    ```
    ./set-azure-key-vault-secrets.py
    ```

4. Bring up `azureite`:

    ```
    docker compose up -d azurite
    ```

5. In VSCode, go to the Azure tab.

6. Look down in the `Workspace` section

7. Create the `test-azure-queue` queue.

8. Azure Storage Explorer should allow you to access the storage account for Azurite's `devstoreaccount1` without any configuration. After selecting the `test-azure-queue` queue, you can add a message to the queue. Please note that Storage Explorer's Add menu **stores the message as a Base64-encoded string by default**. So far, this seems to be unique to Storage Explorer. Because of this, **this template does not go out of its way to account for Base64**. However, but you may wish to consider it if you are adapting this into an application that uses Azure Queue Storage. Any messages added via Storage Explorer should be stored as **Plain UTF-8**. Message format.

    ```
    {"SleepDurationSeconds": 12}
    ```

9. Before starting the worker, make sure that the `USE_AZURE_QUEUE_STORAGE` is set to `1` and that other `USE_` environment variables are not set to `1`.
   You will also point Redis at the Key Vault secret name created by `set-azure-key-vault-secrets.py`, as the compose file's default is to use the SSM path (Azure Key Vault key and SSM Parameter Store path formats are entirely incompatible with one another):

    ```
    export USE_AZURE_QUEUE_STORAGE=1
    export USE_AZURE_SERVICE_BUS=0
    export USE_NATS=0
    export USE_KAFKA=0
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    export COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH=common-distributed-redis
    ```

10. Bring up the worker:

    ```
    docker compose up worker
    ```

### Azure Service Bus

Testing `Azure Service Bus` will require the various Azure-related python module to be installed:

```
pip install azure.servicebus azure.identity azure.keyvault
```

#### Testing Messages

1. Run `generate-azure-key-vault-cert.sh` to generate the certificate files necessary for the Azure Key Vault Emulator to work.

    ```
    ./generate-azure-key-vault-cert.sh
    ```

2. Bring up `azure-key-vault-emulator` (which shall be holding the connection string for Azure Service Bus) and Redis:

    ```
    docker compose up -d azure-key-vault-emulator redis
    ```

3. Run `set-azure-key-vault-secrets.py` to set the connection strings for Azure Queue Storage, Azure Service Bus, and Redis (`common-distributed-redis`) in the Azure Key Vault emulator:

    ```
    ./set-azure-key-vault-secrets.py
    ```

4. Set a value for the `LOCAL_MSSQL_SA_PASSWORD` environment environment variable to be used by the local SQL Server container that we will be spinning up (to set this for long-term, place it in your home directory's `~/.bashrc` file) (password must be at least 8 characters and contain a number and special character):

    ```
    export LOCAL_MSSQL_SA_PASSWORD="ExamplePassword1@"
    ```

5. Bring up `azure-service-bus-mssql`, the database back-end used by the service bus emulator:

    ```
    docker compose up -d azure-service-bus-mssql
    ```

6. The service bus emulator depends on a configuration file located at `config/azure-service-bus/service-bus-config.json`. Make sure that the container can read it with `chmod`:

    ```
    chmod o+r config/azure-service-bus/service-bus-config.json
    ```

7. Bring up `azure-service-bus-emulator`:

    ```
    docker compose up -d azure-service-bus-emulator
    ```

8. Give the service bus emulator a moment to start up (the amount of time to wait is controlled by the `SQL_WAIT_INTERVAL` variable in the `docker-compose.yaml` file).

9. The configuration file defined for the service bus emulator defines a queue named `test-queue`. To send to this queue, you can use the provided python script to send a job that will tell the worker to sleep for the specified number of seconds:

    ```
    ./send-azure-service-bus-job.py 12
    ```

10. Before starting the worker, make sure that the `USE_AZURE_SERVICE_BUS` is set to `1` and that other `USE_` environment variables are not set to `1`.
    You will also point Redis at the Key Vault secret name created by `set-azure-key-vault-secrets.py`, as the compose file's default is to use the SSM path (Azure Key Vault key and SSM Parameter Store path formats are entirely incompatible with one another):

    ```
    export USE_AZURE_QUEUE_STORAGE=0
    export USE_AZURE_SERVICE_BUS=1
    export USE_NATS=0
    export USE_KAFKA=0
    export USE_ACTIVEMQ=0
    export USE_KINESIS=0
    export USE_RABBITMQ=0
    export COMMON__DISTRIBUTED__REDIS__CONNECTION_STRING_PATH=common-distributed-redis
    ```

11. Bring up the worker:

    ```
    docker compose up worker
    ```