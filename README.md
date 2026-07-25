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
* Documentation for local testing

# Configuration

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file.

## Loader Mode

"Loader" mode is a new implementation of the handling of jobs pulled from the job source. Compared to the original
"Batch" mode, it features:

* Automatic backlog population for fewer delays between job executions and reducing the number of idle worker threads in
  a multi-threading scenario.
* Better separation of concerns for maintainability.

"Loader" mode is currently marked as experimental until it can be tested more in deployed environments,
while the original "Batch" mode is the default. To enable "Loader" mode, either:

* Set the `JOBS__LOADER__ENABLED` environment variable to `1`.
* Adjust the logic in `RedShirt.Example.JobWorker.Core` project's `Extensions/ServiceCollectionExtensions.cs` (as part
  of initializing this template)

### Important Note: Loader Mode + Kinesis

Important note: Before combining Loader mode with the Kinesis job source, please consider the below message about some
behaviours of the job source implementation that one should be aware of.

A Kinesis stream is fundamentally composed of multiple shards, and the shards contain the job record messages. A default
Kinesis stream will have 4 shards. The Kinesis job source implementation in this worker template operates by placing a
distributed lock on a shard until the worker has fully processed all of the job record messages that it pulled from that
shard. The distributed lock prevents a parallel instance of the job worker from pulling the same records and duplicating
work (subject to any idempotency system the template implementation's work processor has in place).

Loader mode is designed to asynchronously poll for messages. This means that a single job worker instance using Loader
mode could potentially place a claim on all available shards on a Kinesis stream. This would limit potential for scaling
the stream consumer. The Kinesis job source might fundamentally be a better fit for the "Batch" mode message sourcing for which it was originally designed. This noteworthiness could be a case for "Batch" mode to be refactored to have more distinction between class roles rather than entirely replaced in the future.

If you choose to apply this template by combining Loader mode and Kinesis, please be aware of this warning.

# Initialisation

Below are the recommended steps for using this as a template:

1. To change the namespace of this solution en-masse for your purposes, use the `init-repo.sh` script:

    ```bash
    bash init-repo.sh New.Namespace.Here
    ```

2. In the `Core` project, update `IJobDataModel` interface and `JobDataModel` implementation
   to reflect the need of your project.
3. In the `Core` project, update `SourceMessageConverter` and `SourceMessageSorter` to
   fit your needs of your project.
4. Update the `Core.Logic` project's implementation of the `IJobLogicRunner` interface to handle `IJobDataModel` jobs as
   needed by your project.
5. Select a message source type and prune the implementation projects for the sources that you are not using.

## Notes on Implementing Kinesis

A Kinesis stream is fundamentally composed of multiple shards, and the shards contain the job record messages. A default
Kinesis stream will have 4 shards.

Fundamentally, the Kinesis as a source of records is different from the other messaging technologies covered within this
template in a number of ways:

* Kinesis is a stream rather than a true message broker.
* As a stream, individual messages have no built-in mechanism to be reclaimed by the queue in the event of the container
  being stopped by a sudden and catastrophic problem outside of its control (e.g. hardware failure). This is why the job
  worker will only proceed to move the tracker for a shard past the current batch of messages after all messages have
  been attempted. It is an intentional safety mechanism.
* The stream stores messages individually, but the iterator string used to progress in a short-term context only
  operates in batches.
* The stream stores messages in sequential order, but this this template supports prioritizing messages received in an
  arbitrary order as defined by the chosen implementation of `ISourceMessageSorter`.

### Kinesis AI Audit Notes

The solutions to the above considerations for Kinesis are part of why an AI audit of the key Kinesis using the Composer
model takes serious issue with many points of the Kinesis job source's design. These objections are included but not
limited to:

* Worrying about "leaking" locks by keeping them for later storage rather than encapsulating all remaining operations in
  the method in a try-finally statement.
* Assuming that messages will not be acknowledged if the job failed to process, or that some message Ids will never be
  acknowledged for some other reason.
    * This one in particular might be solveable with a different method name that better implies that it is always
      called, but this is not a priority.
* Worrying about the "all-or-nothing" nature of processing a batch of messages from a shard. Composer is under the
  impression that progress can be incremented per message. It is wrong.

While Composer really doesn't like the Kinesis job source in particular, these issues are in fact fundamental to the
operation of Kinesis within this framework.

# Testing

Unit tests are written using XUnit/Moq.

For local development testing, see the `test/local/` folder.
