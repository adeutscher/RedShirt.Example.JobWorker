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
    * [Apache ActiveMQ Artemis](https://artemis.apache.org/components/artemis/)
    * [Apache Kafka](https://kafka.apache.org/)
    * [Apache Pulsar](https://pulsar.apache.org/)
    * [Azure Queue Storage](https://learn.microsoft.com/en-us/azure/storage/queues/storage-queues-introduction)
    * [Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview)
    * [Google Pub/Sub](https://cloud.google.com/pubsub/docs)
    * [NATS](https://nats.io/)
    * [RabbitMQ](https://www.rabbitmq.com/)
        * Supports short polling or consumer subscriptions
    * [Redis Streams](https://redis.io/docs/latest/develop/data-types/streams/)
* Cache-based idempotency support
    * Prevents the same message from being run twice in the event that an executor loses custody of a message.
        * Messages could be dropped by connection issues with the message source or because of a protocol decision by
          the message source.
    * Prevents simultaneous execution of the same message in the event of a dropped message
    * Caches results to prevent re-running of a job if received non-concurrently
* Container health probes
* Documentation for local testing (see `test/local/`)

# Core Architecture

The general architecture of the core application goes along these lines:

* Dependency injection is set up with an `IJobSource` implementation. The job source abstraction handles:
    * Pulling messages from a source
    * Acknowledging completed messages
    * Sending heartbeat signals back to the source to keep message ownership with the JobWorker instance.
* A job loader (`IJobLoader`) worker thread pulls messages from a job source and feeds it through an intake handler into
  an in-memory repository. The job loader has two configurable strategy options (described in more detail
  in [Batch Mode vs. Loader Mode](#batch-mode-vs-loader-mode)):
    * Batch Mode (default): Messages are processed in batches, with the next batch being pulled only after the previous
      one has completed.
    * Loader Mode: Messages are continually pulled to maintain an in-memory buffer of messages.
* Messages in the in-memory repository can be prioritized by the implementation of the message sorter
  `ISourceMessageSorter`).
* Executor (`IJobExecutor`) worker threads pulls a message at a time from the in-memory repository. When it receives a
  message, it passes it through safety layers where it is invokes the job logic runner (`IJobLogicRunner`). The job
  logic runner shall run the message handling for this message type.
    * For this general template, the job logic runner implementation is currently a stub that shall sleep for the
      requested number of seconds.
* In the event that the job source implementation requires application-level heartbeats for long-running messages, a
  heartbeat maintainer (`IHeartbeatMaintainer`) worker thread shall periodically heartbeat messages according to the job
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

## Health probes

The JobWorker application has a configurable set of health pages. When health endpoints are enabled, health is currently
determined by the amount of time since the most recent major exception caught in the `RedShirt.Example.JobWorker.Core`
project when it interacts with a service from the
`RedShirt.Example.JobWorker.Common.Distributed` package or the `IJobSource` implementation in one of the `JobManagement`
packages.

| Endpoint          | Purpose    | Healthy response       | Unhealthy response           |
|-------------------|------------|------------------------|------------------------------|
| `GET /live`       | Liveness   | `200` plain text `OK`  | N/A                          |
| `GET /health`     | Health     | `200` plain text `OK`  | `503` plain text `unhealthy` |
| `GET /statistics` | Statistics | `200` JSON (see below) | N/A                          |

Environment variables related to health:

* `HEALTH__ENABLED`: HTTP listener with health pages (default: `true`). When `false`, the worker runs without binding a
  health port.
* `HEALTH__PORT`: TCP port for health endpoints, bound on `0.0.0.0` (default: `8080`).
* `HEALTH__RECENT_INCIDENT_THRESHOLD_SECONDS`: Amount of seconds after a major exception in `Core` project for which the
  system will be considered unhealthy.
* `JOBS__HALT_ON_FAILURE`: Related. If set to `true`, then the application shall immediately throw major exceptions to
  crash the application, making the health system moot. Only recommended for local development.

### Statistics Example

This is an example of the returned statistics model (C# definitions can be found in
`RedShirt.Example.JobWorker.Common.Health` in `Models/StatisticsModel.cs`:

```json
{
  "lifetime": {
    "successfulTimings": {
      "average": "00:00:00",
      "max": "00:00:00",
      "min": "00:00:00"
    },
    "totals": {
      "received": 0,
      "successful": 0,
      "cancelled": 0,
      "failed": 0,
      "invalidData": 0
    }
  },
  "uptime": "00:12:34.5678900"
}
```

## Message Sourcing Strategies

This template supports flexibility in how messages are sourced.

The default behaviour of each message source is short polling with an exponential back-off. Depending on the messaging
technology, the implementation may also support long polling or a subscription consumer.

### Polling

#### Batch Mode vs. Loader Mode

This template offers two different approaches to how messages are polled from a message source (internally referred to
as a job source):

* "Batch" mode will poll the source for a batch of messages and wait until all pulled messages have been processed
  before polling the job source again.
* "Loader" mode will maintain a buffer of messages in memory with the goal of reducing worker thread downtime.

Batch mode is the default mode for this template. To enable loader mode:

* Set the `JOBS__USE_LOADER_MODE` environment variable to `true`.
* If you wish to change the default or to have your application use only one polling strategy, then you can adjust the
  logic in the `RedShirt.Example.JobWorker.Core` project's `Extensions/ServiceCollectionExtensions.cs` (as part of
  initializing this template).

Some job sources work better with Loader Mode than others, as the below subsections will explain.

##### Important Note: Loader Mode + Kinesis

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

#### Long Polling

Several job sources can wait on the broker for the first messages of a poll instead of returning immediately when the
queue is idle. This increases the responsiveness of the JobWorker to new messages.

If you choose to configure your job worker for long polling, please consider the following points:

* This template's long polling strategy is fundamentally based around increased responsiveness by maintaining an open
  line for messages to be received. If one is using long polling, I would strongly advise configuring a low value to the
  Core job loader loop's incremental back-off limit (set by the `JOBS__MAX_IDLE_WAIT_SECONDS` variable). Leaving this at
  a high value as is encouraged for short-polling would leave your application flickering between periods of low
  responsiveness during back-off and periods of high responsiveness during long polling.
* Long polling implementations lean towards grabbing the first available messages and running with them, even if the
  batch is not fully fulfilled. If you wish to use multiple worker threads (as configured with the
  `JOBS__WORKER_THREAD_COUNT` environment variable), then this could leave some threads under-utilized in batch mode. I
  would suggest pairing long polling with Loader mode (setting `JOBS__USE_LOADER_MODE` to `true).

Long polling is configured on job sources that support it with a `WAIT_TIME_SECONDS` environment variable. A value of
`0` (the local compose default) is short-polling. A positive value is the number of seconds to wait on the **first**
request of a message-source fetch. Follow-up requests that attempt to fulfill the overall batch size request omit the
wait to avoid delaying the delivery of already-received messages.

| Job source        | Environment variable                               | Effective range              |
|-------------------|----------------------------------------------------|------------------------------|
| Amazon SQS        | `JOB_SOURCE__SQS__WAIT_TIME_SECONDS`               | 0–20 (SQS long-poll maximum) |
| Apache Pulsar     | `JOB_SOURCE__PULSAR__WAIT_TIME_SECONDS`            | 0 or greater                 |
| Azure Service Bus | `JOB_SOURCE__AZURE_SERVICE_BUS__WAIT_TIME_SECONDS` | 0 or greater                 |
| Google Pub/Sub    | `JOB_SOURCE__GOOGLE_PUB_SUB__WAIT_TIME_SECONDS`    | 0–60                         |
| NATS              | `JOB_SOURCE__NATS__WAIT_TIME_SECONDS`              | 0 or greater                 |

The other job sources in this template (ActiveMQ, Kafka, Kinesis, Pulsar, RabbitMQ, Redis Streams, and Azure Queue
Storage) do not support long polling at this time. This is due to the constraints of the underlying technology or
interface library.

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file. Because some job
sources do not support long polling and long polling is a recent addition to the template, the defaults in the local
testing stack are still tuned for short polling.

### Subscriptions

Subscribing to a source is an option for allowing a job worker to be more responsive to messages without bombarding the
source with poll requests.

Subscribing is configured on job sources that support it with a `SUBSCRIBE` environment variable. Setting this value to
`true` will enable it.

| Job source | Environment variable              |
|------------|-----------------------------------|
| RabbitMQ   | `JOB_SOURCE__RABBITMQ__SUBSCRIBE` |

## Idempotency

In order to properly implement the idempotent consumer pattern, the outcome of processing the same message repeatedly
must be the same as processing the message once.

This template has support for idempotent operations by way of Redis caches.

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file.

### Feature History

Prior to its implementation in this template, the idea for idempotency came from working on an unrelated RabbitMQ
message consumer application. The connection to RabbitMQ would become unstable when the server was under load. Because
of how RabbitMQ functions, this meant that ownership of the messages would be lost by the consumer. Even in an
environment with only one message consumer instance, the retrieval of a message is tied to the specific connection that
it came from. Even if the connection to RabbitMQ is reestablished, it cannot be used to acknowledge the message from the
previous connection.

Thus, the idempotency system in this template and the other worker had two main objectives:

* The same message from a source should not be processed twice.
* The same message from a source should *especially* not be processed twice simultaneously.
* If a message is received twice, then that suggests that the previous retrieval of the message lost custody and is
  unable to acknowledge. The retrieval with custody should use the response status of the retrieval that lost custody
  once it becomes available rather than re-process the message.

### Feature Architecture

* Idempotency operations are centralized in the Idempotency Execution Service (`IIdempotencyExecutionService`).
* The Idempotency Execution Service is used extensively by the executor worker threads. However, the idempotency service
  is also written to return permissive stand-ins in the event that idempotency is not enabled in configuration.
* If idempotency support is enabled, then a monitor worker thread shall occasionally check the status of messages that
  were detected as being re-received in parallel to an original receive.
    * This parallel running could happen on this instance of the job worker process or another instance, as judgment is
      made by a distributed lock based in a Redis instance.
    * The monitor thread shall periodically try to re-acquire an exclusive lock on the message ID and to use the cached
      result to immediately acknowledge the message based on past processing.
    * If the monitor thread is able to acquire a lock but fails to retrieve a cached result, then the message is
      re-flagged in the job repository as being a candidate for execution.

### Idempotency IDs

The Idempotency ID of a message is its unique identifier that allows the idempotency system to function. The application
will not crash if receives a job with a null idempotency ID, but it won't be able to act as an idempotent consumer.

* For message brokers like SQS or Azure Service Bus, the Idempotency ID value is set off of the messages ID from the
  system.
* For more stream-like job sources such as Kinesis or Kafka, the Idempotency ID value is based on an indication of a
  record's position in the stream.

For many job sources and configurations, this identifier is automatically generated. However, there are some sources and
configurations where it is not set.

#### Concerning Idempotency ID Uniqueness

Many message sources can automatically provide Idempotency IDs that are reliably unique. However, some services allow
them to be specified by the publisher submitting the message.

The Idempotency IDs are considered to be reliably unique based on of the configuration variable
`JOBS__IDEMPOTENCY__IDEMPOTENCY_IDS_CAN_REPEAT=false`. If the IDs are said to not repeat, then a successful
acknowledgement of a message shall mean that the cached result for that message will be cleared or not entered into the
cache at all. This is done in hope of saving cache resources.

#### RabbitMQ Message IDs

Of the current roster of job sources, RabbitMQ has no option to automatically generate a message ID for the application
to take as an idempotency key. If you are using RabbitMQ and wish to make use of idempotency, then you will need to make
sure that your message publishers are providing a message ID.

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

#### Redis Streams Message IDs

In practice, Redis Streams seems to also require manual setting of a message ID.

Important distinction for this template: the Redis stream entry ID is exposed as the job's `MessageId`, but the
idempotency key is taken from a `message_id` **field** on the stream entry. If you are using Redis Streams and wish to
make use of this template's idempotency features, then your publishers should set that field. Auto-generating or
manually specifying the Redis stream entry ID alone is not enough for the idempotency system.

Example in C# (StackExchange.Redis):

```csharp
var db = multiplexer.GetDatabase();
var fields = new NameValueEntry[]
{
    new("body", """{"SleepDurationSeconds":12}"""),
    // Supply a specific Redis stream entry ID
    new("message_id", Guid.NewGuid().ToString()) // Idempotency ID for this template
};

var specificEntryId = await db.StreamAddAsync("jobs", fields);
```

In Python (`redis-py`):

```python
#!/usr/bin/env python

import json
import uuid

import redis

client = redis.Redis(host="localhost", port=6379, decode_responses=True)
values = {
    "body": json.dumps({"SleepDurationSeconds": 12}),
    # Supply a specific Redis stream entry ID
    "message_id": str(uuid.uuid4()),  # Idempotency ID for this template
}

specific_entry_id = client.xadd("jobs", values)
```

Documentation purports that one can provide an asterisk to request that Redis auto-generate an ID for a message, but
this has not been my experience in practice.

### Redis Connection Instability Tolerance

Though being a proper idempotent consumer is the overall goal, this general template prioritizes overall stability over
strict idempotency. Non-critical exceptions encountered while interacting with Redis at the low-level are captured by a
safety layer.

If the application fails to interact with Redis, then the safety layers will enter a "disgrace" state, in which the
lower-level Redis services will not be attempted until the "disgrace" period has passed.

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

## Cached Idempotency vs Database

This general template uses Redis to cache results and drive its idempotency. However, if Redis does not meet your needs
for message permanence then you will need to implement a service to access another data store.

## Secret Managers

This general template has support for using a secret manager service. The services within the template interact with the
secret manager through the `ISecretManagerService` or `ISecretManagerCacheService` interfaces.
`ISecretManagerCacheService` maintains an in-memory cache of secrets in order to avoid overwhelming the secret manager
server by accident.

At the moment, there are three available implementations of `ISecretManagerService`:

* [AWS SSM Parameter Store](https://docs.aws.amazon.com/systems-manager/latest/userguide/systems-manager-parameter-store.html)
* [Azure Key Vault](https://azure.microsoft.com/en-us/products/key-vault)
* [Docker Secrets](https://docs.docker.com/reference/compose-file/secrets/)
  ([Secondary Link](https://docs.docker.com/engine/swarm/secrets/))

The Core library of this general template indirectly makes use of `ISecretManagerService`, requiring it to be configured
in dependency injection by default. This general template assumes that the chosen secret manager implementation is SSM.
The exception to this is if the chosen job source is either Azure Queue Storage or Azure Service Bus job sources, which
configures Azure Key Vault as the secret manager. The Azure-based job sources use Key Vault with the assumption that
mixing major cloud platforms would be unusual. The template chooses a secret manager provider in the
`Extensions/ServiceCollectionExtensions.cs` file of the root `RedShirt.Example.JobWorker` project.

Please keep this in mind when adapting this template for your specific application.

The following job source implementations (read: pretty much all of them) rely on a Secret Manager as part of their
operations:

* NATS
* RabbitMQ
* ActiveMQ
* Azure Queue Storage
* Azure Service Bus
* AWS Kinesis (indirectly, for its use of Redis in short-term iterator storage)
* Redis Streams

### Docker Secrets

While the other secret managers are more straightforward with their plans, Docker Secrets has some caveats and
assumptions that should be documented.

In general, I would encourage the use of a secret manager other than Docker Secrets. Compared to other options it lacks
flexibility. However, it may be exactly what is needed for a small-scale environment.

Other notes:

* Unlike other secret managers, a container instance's secrets cannot be rotated without restarting the
* Secrets are assumed to be files in `/run/secrets`
    * This directory can be overridden by setting a new path in `COMMON__SECRETS__DOCKER__DIRECTORY`
* Secret keys are assumed to be roughly equal to the underlying file specified in the Docker stack configuration. If the
  file does not meet these guidelines and because the compose file specifies another target path within the container,
  then the secret manager has nothing with which to resolve a key to a file containing a value. After checking the exact
  name of the key under the secret directory, the secret manager will attempt the path with a few file extensions
  (though realistically, only the flat key match will probably be useful).
    * For example, with no overriding directory the key `foo-password` will be searched for under the following absolute
      paths:
        * `/run/secrets/foo-password`
        * `/run/secrets/foo-password.txt`
        * `/run/secrets/foo-password.json`
    * In general, it's advised not to meddle with secret targets within the container at all if you plan to use them
      with this template's Docker secret manager.
* The implementation has not been tested under Docker Swarm, and as such hasn't been tested with external secrets.

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

## Notes on Implementing Kafka

Kafka is more of an append-only event log rather than a traditional message queue.

I go into more detail on each of these points below, but the cliff notes for implementing Kafka are:

* This template's version of Kafka assumes no authentication, which is currently left as an excercise for the reader.
* It is strongly advised to use Batch mode polling when using Kafka as a job source.
* It is strongly advised to enable idempotency handling for Kafka. Basic enabling of idempotency handling hinges off of
  the `JOBS__IDEMPOTENCY__ENABLED` environment variable, with other options described in the configuration section of
  this document and demonstrated in `test/local/docker-compose.yaml`.

### Kafka Authentication

This general template was tested against a local Kafka container with no authentication set up. In addition to that,
there are currently 5 different SASL mechanisms to choose from when implementing authentication. Implementing
authentication for Kafka when adapting this template is currently left as an exercise for the reader.

Kafka clients are constructed in the `RedShirt.Example.JobWorker.JobManagement.Kafka` project, in
`Factories/KafkaConsumerFactory.cs`.

### Kinesis Comparisons and Batch Mode Recommendation

A Kafka topic is very similar to a Kinesis stream. I am going to be comparing Kafka to Kinesis very heavily in this
section because Kinesis is a much more established job source implementation that I have more experience with.

The Kinesis comparison carries down to a basic implementation level of the technology. A Kafka topic is divided into
partitions, just as a Kinesis stream is divided into shards. Processing jobs from either of these sources involves some
layer of the process managing shard/partition ownership

However, a major difference between the available interfaces for Kafka and Kinesis and their implementations in this
template is how ownership of a partition/shard works:

* In Kinesis, the job source's application code lists and iterates through shards in an attempt to find one that does
  not have a distributed lock. The Kinesis job source then performs a `GetRecords` operation on that shard.
* In Kafka, our options are more limited.
    * Kafka does have an option to list individual partitions, but this is considered more of an admin action.
    * Instead, the client declares a Kafka consumer which simply calls `consumerObject.Consume(TimeSpan)`, with a
      TimeSpan for timeouts.
    * Along the same lines: with no ability to iterate through partitions, ownership of a partition is out of the
      client's hands.
        * The Kakfa server/cluster calculates ownership of a partition within a consumer group when the number of
          partitions changes or the number of connected clients changes. This means that a Kafka client can lose access
          to a partition while still working exectly as intended.

Commiting a message in Kafka (done by its offset) implies that every message before it in the partition has also been
processed. This template sorts the jobs retrieved by a job source and messages could be run in parallel worker threads
with different finish times. Under these conditions, it cannot be guaranteed that the batch of messages being commited
during acknowledgement is the next one on the partition's to-do list.

Because of this, it is strongly recommended to run message polling in Batch mode as opposed to Loader mode. With Batch
mode, the job source is polled as soon as the previous batch has finished. In Loader mode, the job loader handler could
have to wait for several seconds (based on the value of the `JOBS__MAX_IDLE_WAIT_SECONDS` environment variable) before
polling again.

### Kafka Idempotency Handling

As described in the above section on Kinesis comparisons, Kafka clients in a consumer group do not control over what
partitions they have authority to commit to. Ownership of a partition is reconsidered when the number of clients in a
consumer group or the number of partitions in a topic changes. A client can lose commit rights to a topic partition
through no fault of its own.

Because of the above point, it is *strongly* encouraged to enable idempotency support for your application if you are
using Kafka with multiple consumers. Basic enabling of idempotency handling hinges off of the
`JOBS__IDEMPOTENCY__ENABLED` environment variable, with other options described in the configuration section of this
document and demonstrated in `test/local/docker-compose.yaml`.

# Testing

Unit tests are written using XUnit/Moq.

For local development testing, see the `test/local/` folder.
