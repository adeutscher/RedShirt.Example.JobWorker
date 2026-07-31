# RedShirt.Example.JobWorker

This template provided abstractions and implementations for polling/processing messages out of a variety of message
brokers. It is intended to be run out of a containerized environment.

Repo features:

* Initialisation script for quick namespace adjustment.
* Configuration is based on environment variables.
* Support for the following message sources:
    * [Amazon SQS](https://aws.amazon.com/sqs/)
    * [Amazon Kinesis](https://aws.amazon.com/kinesis/data-streams/)
    * [Apache ActiveMQ Artemis](https://artemis.apache.org/components/artemis/)
    * [Azure Queue Storage](https://learn.microsoft.com/en-us/azure/storage/queues/storage-queues-introduction)
    * [Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview)
    * [NATS](https://nats.io/)
    * [RabbitMQ](https://www.rabbitmq.com/)
* Idempotency support
* Documentation for local testing

# Configuration

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file.

## Batch Mode vs. Loader Mode

This template offers two different approaches to how messages are polled from a message source (internally referred to
as a job source):

* "Batch" mode will poll the source for a batch of messages and wait until all pulled messages have been processed
  before polling the job source again.
* "Loader" mode will maintain a buffer of messages in memory with the goal of reducing worker thread downtime.

Batch mode is the default mode for this template. To enable loader mode:

* Set the `JOBS__USE_LOADER_MODE` environment variable to `true`.
* If you wish to change the default or to have your application use only one polling strategy, then you can adjust the
  logic in `RedShirt.Example.JobWorker.Core` project's `Extensions/ServiceCollectionExtensions.cs` (as part of
  initializing this template)

### Important Note: Loader Mode + Kinesis

Important note: Before combining Loader mode with the Kinesis job source, please consider the below message about some
behaviours of the job source implementation that one should be aware of.

A Kinesis stream is fundamentally composed of multiple shards, and the shards contain the job record messages. A default
Kinesis stream will have 4 shards. The Kinesis job source implementation in this worker template operates by placing a
distributed lock on a shard until the worker has fully processed all the job record messages that it pulled from that
shard. The distributed lock prevents a parallel instance of the job worker from pulling the same records and duplicating
work (subject to any idempotency system the template implementation's work processor has in place).

Loader mode is designed to asynchronously poll for messages. This means that a single job worker instance using Loader
mode could potentially place a claim on all available shards on a Kinesis stream. This would limit potential for scaling
the stream consumer. The Kinesis job source might fundamentally be a better fit for the "Batch" mode message sourcing
for which it was originally designed. This noteworthiness could be a case for "Batch" mode to be refactored to have more
distinction between class roles rather than entirely replaced in the future.

If you choose to apply this template by combining Loader mode and Kinesis, please be aware of this warning.

## Idempotency

In order to properly implement the idempotent consumer pattern, the outcome of processing the same message repeatedly
must be the same as processing the message once.

This template has support for idempotent operations by way of Redis caches.

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file.

### Stability

Though being a proper idempotent consumer is the overall goal, this general template prioritizes overall stability over
strict idempotency. Non-critical exceptions encountered while interacting with Redis at the low-level are captured by a
safety layer.

If the application fails to interact with Redis, then the safety layers will enter a "disgrace" state, in which the
lower-level Redis services will not be attempted until the "disgrace" period has passed.

### Idempotency IDs

The Idempotency ID of a message is its unique identifier that allows the idempotency system to function. The application
will not crash if receives a job with a null idempotency ID, but it won't be able to act as an idempotent consumer.

* For message brokers like SQS or Azure Service Bus, the Idempotency ID value is set off of the messages ID from the
  system.
* For more stream-based job sources such as Kinesis, the Idempotency ID value is based on an indication of a record's
  position in the stream.

For many job sources and configurations, this identifier is automatically generated. However, there are some sources and
configurations where it is not set.

#### RabbitMQ Message IDs

Of the current roster of job sources, RabbitMQ is the only one with no option to automatically generate a message ID for
the application to take as an idempotency key. If you are using RabbitMQ and wish to make use of idempotency, then you
will need to make sure that your message publishers are providing a message ID.

In the RabbitMQ browser view, this can be done by manually specifying the `message_id` property.

In C#, this would look like this:

```csharp
var properties = channel.CreateBasicProperties();
properties.MessageId = Guid.NewGuid().ToString();
properties.Persistent = true; // Optional: make message persistent

var body = Encoding.UTF8.GetBytes("Hello RabbitMQ");

channel.BasicPublish(
    exchange: "my-exchange",
    routingKey: "my-routing-key",
    mandatory: false,
    basicProperties: properties,
    body: body);
```

# Initialisation

Below are the recommended steps for using this as a template:

1. To change the namespace of this solution en-masse for your purposes, use the `init-repo.sh` script:

    ```bash
    bash init-repo.sh New.Namespace.Here
    ```

2. In the `Core` project (in the `Services/SourceMessages/` directory), update `IJobDataModel` interface and
   `JobDataModel` implementation to reflect the needs of your project.
3. In the `Core` project (in the `Services/SourceMessages/` directory), update `SourceMessageConverter` and
   `SourceMessageSorter` to fit your needs of your project.
4. Update the `Core.Logic` project's implementation of the `IJobLogicRunner` interface to handle `IJobDataModel` jobs as
   needed by your project.
5. Select a message source type and prune the implementation projects for the sources that you are not using. This will
   involve changing the dependency injection setup in the root `RedShirt.Example.JobWorker` project's
   `Extensions/ServiceCollectionExtensions.cs` file.
    * The dependency injection setup in the root project assumes that the general template will be pruned down.
    * The dependency injection setup in the root project assumes that the chosen Secret Manager is SSM unless the chosen
      job source is explicitly Azure-based (see below for more details).

## Secret Managers

This general template has support for using a secret manager service. The services within the template interact with the
secret manager through the `ISecretManagerService` or `ISecretManagerCacheService` interfaces.
`ISecretManagerCacheService` maintains an in-memory cache of secrets in order to avoid overwhelming the secret manager
server by accident.

At the moment, there are two available implementations of `ISecretManagerService`:

* Amazon SSM Parameter Store
* Azure Key Vault

The Core library of this general template indirectly makes use of `ISecretManagerService`, requiring it to be configured
in dependency injection by default. This general template assumes that the chosen secret manager implementation is SSM.
The exception to this is if the chosen job source is either Azure Queue Storage or Azure Service Bus job sources, which
configures Azure Key Vault as the secret manager. The Azure-based job sources use Key vault with the assumption that
mixing
major cloud platforms would be unusual.

Please keep this in mind when adapting this template for your specific application.

The following job source implementations (read: pretty much all of them) rely on a Secret Manager as part of their
operations:

* NATS
* RabbitMQ
* ActiveMQ
* Azure Queue Storage
* Azure Service Bus
* AWS Kinesis (indirectly)

## Notes on Implementing Kinesis

A Kinesis stream is fundamentally composed of multiple shards, and the shards contain the job record messages. A default
Kinesis stream will have 4 shards.

Fundamentally, the Kinesis as a source of records is different from the other messaging technologies covered within this
template in a number of ways:

* Kinesis is a stream rather than a true message broker.
* As a stream, individual messages have no built-in mechanism to be reclaimed by the queue in the event of the container
  being stopped by a sudden and catastrophic problem outside its control (e.g. hardware failure). This is why the job
  worker will only proceed to move the tracker for a shard past the current batch of messages after all messages have
  been attempted. It is an intentional safety mechanism.
* The stream stores messages individually, but the iterator string used to progress in a short-term context only
  operates in batches.
* The stream stores messages in sequential order, but this template supports prioritizing messages received in an
  arbitrary order as defined by the chosen implementation of `ISourceMessageSorter`.

### Kinesis AI Audit Notes

The solutions to the above considerations for Kinesis are part of why an AI audit of the key Kinesis using the Composer
model took (and sometimes continues to take) issue with many points of the Kinesis job source's design. These objections
include but are not limited to:

* Worrying about "leaking" locks by keeping them for later storage rather than encapsulating all remaining operations in
  the method in a try-finally statement.
* Assuming that messages will not be acknowledged if the job failed to process, or that some message Ids will never be
  acknowledged for some other reason.
    * This one in particular might be solvable with a different method name that better implies that it is always
      called, but this is not a priority.
* Worrying about the "all-or-nothing" nature of processing a batch of messages from a shard. Composer is under the
  impression that progress can be incremented per message. It is wrong.

While Composer really doesn't like the Kinesis job source in particular, these issues are in fact fundamental to the
operation of Kinesis within this framework.

# Testing

Unit tests are written using XUnit/Moq.

For local development testing, see the `test/local/` folder.
