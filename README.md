# RedShirt.Example.JobWorker

A .NET template for a containerized worker that polls jobs from whatever message broker you already run.

# Template

## Template Philosophy

The central philosophy of this template is flexibility and preparedness. A template maintained with lessons from past
projects can provide a stable foundation from which to launch future projects. The use of a template is more sustainable
and resource-efficient than adapting directly from past projects.

This template on its own will almost certainly be more feature-rich than any one project needs. I can't imagine a world
in which a single applied program needs to be prepared to receive messages from 11 different messaging technologies.
Because of this, this template is designed to make it convenient to prune away unused components. It is much easier to
delete an unneeded component than it is to generate a new component.

## Features

Repo features:

* Initialisation script for quick namespace adjustment.
* Configuration is based on environment variables.
* Support for the following message sources:
    * [Amazon SQS](https://aws.amazon.com/sqs/)
    * [Amazon Kinesis](https://aws.amazon.com/kinesis/data-streams/)
        * For more details on Kinesis, refer to [`docs/job-source-notes-kinesis.md`](docs/job-source-notes-kinesis.md)
    * [Apache ActiveMQ Artemis](https://artemis.apache.org/components/artemis/)
        * Supports short polling or consumer subscriptions.
    * [Apache Kafka](https://kafka.apache.org/)
        * For more details on Kafka, refer to [`docs/job-source-notes-kafka.md`](docs/job-source-notes-kafka.md)
    * [Apache Pulsar](https://pulsar.apache.org/)
    * [Azure Queue Storage](https://learn.microsoft.com/en-us/azure/storage/queues/storage-queues-introduction)
    * [Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview)
    * [Google Pub/Sub](https://cloud.google.com/pubsub/docs)
    * [NATS](https://nats.io/)
        * Supports short polling, long polling, or JetStream push-style subscribe consumption.
    * [RabbitMQ](https://www.rabbitmq.com/)
        * Supports short polling or consumer subscriptions.
    * [Redis Streams](https://redis.io/docs/latest/develop/data-types/streams/)
* Cache-based idempotency support
    * Prevents the same message from being run twice in the event that an executor loses custody of a message.
        * Messages could be dropped by connection issues with the message source or because of a protocol decision by
          the message source.
    * Prevents simultaneous execution of the same message in the event of a dropped message
    * Caches results to prevent re-running of a job if received non-concurrently
* Container health probes.
    * For more information, refer to [`docs/health-probes.md`](docs/health-probes.md)
* Sample API connector that respects rate limit responses.
    * For more information, see [`docs/bar-connector.md`](docs/bar-connector.md).
* Documentation for local testing (see `test/local/`).

# Core Architecture

The general architecture of the core application goes along these lines:

* Dependency injection is set up with an `IJobSource` implementation. The job source abstraction handles:
    * Pulling messages from a source
    * Acknowledging completed messages
    * Sending heartbeat signals back to the source to keep message ownership with the JobWorker instance.
* A job loader (`IJobLoader`) worker thread pulls messages from a job source and feeds it through an intake handler into
  an in-memory repository. The job loader has two configurable strategy options (described in more detail
  in [Batch Mode vs. Loader Mode](docs/message-sourcing-polling.md#batch-mode-vs-loader-mode)):
    * Batch Mode (default): Messages are processed in batches, with the next batch being pulled only after the previous
      one has completed. The size of the batches is determined by the `JOBS__FETCH_COUNT` environment variable.
    * Loader Mode: Messages are continually pulled to maintain an in-memory buffer of messages. The size of the backlog
      is determined by the `JOBS__FETCH_COUNT` environment variable.
* Messages in the in-memory repository can be prioritized by the implementation of the message sorter
  `ISourceMessageSorter`).
* Executor (`IJobExecutor`) worker threads pulls a message at a time from the in-memory repository. When it receives a
  message, it passes it through safety layers where it is invokes the job logic runner (`IJobLogicRunner`). The job
  logic runner shall run the message handling for this message type.
    * For this general template, the job logic runner implementation is currently a stub that shall sleep for the
      requested number of seconds.
* In the event that the job source implementation requires application-level heartbeats for long-running messages, a
  heartbeat monitor (`IHeartbeatMonitor`) worker thread shall periodically heartbeat messages according to the job
  source's recommendation.
    * Generally, job sources that require heartbeats are told to recommend a heartbeat interval of 75% of the maximum
      in-flight time for a message without heartbeats. For example, an SQS consumer configured with a visibility timeout
      of 60 seconds would recommend 45 seconds between heartbeats.
* In the event that the cache-based idempotency system is enabled, a monitor (`IIdempotencyMonitor`) worker thread shall
  periodically try to follow up on received messages that the idempotency system recognized as already in flight from
  another worker thread.
    * The other worker thread in question is not necessarily on the worker instance that received the redelivered
      message.
    * The reason for this alternative worker thread is to avoid holding up executor worker threads on account of waiting
      for another message, possibly on another worker instance, to finish.

# Configuration

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file.

## Message Sourcing Strategies

This template supports flexibility in how messages are sourced.

The default behaviour of each message source is short polling with an exponential back-off. Depending on the messaging
technology, the implementation may also support long polling or a subscription consumer.

## Idempotency

In order to properly implement the idempotent consumer pattern, the outcome of processing the same message repeatedly
must be the same as processing the message once.

This template has support for idempotent operations by way of Redis caches.

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file.

For more information on idempotency, see [`docs/idempotency.md`](docs/idempotency.md).

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
6. Consider revising/pruning the Markdown files such as this README or those located in the `docs/` directory. They
   assume that they are speaking for a general template and not for an applied application.

## Cached Idempotency vs Database

This general template uses Redis to cache results and drive its idempotency. However, if Redis does not meet your needs
for message permanence then you will need to implement a service to access another data store.

## Secret Managers

This general template has support for using a secret manager service. The services within the template interact with the
secret manager through the `ISecretManagerService` or `ISecretManagerCacheService` interfaces.
`ISecretManagerCacheService` maintains an in-memory cache of secrets in order to avoid overwhelming the secret manager
service by accident.

At the moment, there are three available implementations of `ISecretManagerService`:

* [AWS SSM Parameter Store](https://docs.aws.amazon.com/systems-manager/latest/userguide/systems-manager-parameter-store.html)
* [Azure Key Vault](https://azure.microsoft.com/en-us/products/key-vault)
* [Docker Secrets](https://docs.docker.com/reference/compose-file/secrets/)
  ([Secondary Link](https://docs.docker.com/engine/swarm/secrets/))

The Core library of this general template indirectly makes use of `ISecretManagerService`, requiring it to be configured
in dependency injection by default.

Many job source implementations rely on a secret manager implementation in order to securely hold credentials.

For more information, refer to [`docs/secret-managers.md`](docs/secret-managers.md).

# Testing

Unit tests are written using XUnit/Moq.

For local development testing, see the `test/local/` folder.
