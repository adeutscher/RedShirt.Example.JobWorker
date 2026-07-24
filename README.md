# RedShirt.Example.JobWorker

General template for polling/processing messages out of a variety of message stores.

Repo features:

* Initialisation script for quick namespace adjustment.
* Configuration is based on environment variables.
* Message polling with:
    * [Amazon SQS](https://aws.amazon.com/sqs/)
    * [Amazon Kinesis](https://aws.amazon.com/kinesis/data-streams/)
    * [Apache ActiveMQ Artemis](https://artemis.apache.org/components/artemis/)
    * [Azure Queue Storage](https://learn.microsoft.com/en-us/azure/storage/queues/storage-queues-introduction)
    * [Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview)
    * [NATS](https://nats.io/)
    * [RabbitMQ](https://www.rabbitmq.com/)

# Configuration

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file.

## Loader Mode

"Loader" mode is a new implementation of the handling of jobs pulled from the job source. Compared to the original
"Batch" mode, it features:

* Automatic backlog population for fewer delays between job executions.
* Better separation of concerns for maintainability.

"Loader" mode is currently marked as experimental until it can be tested more in deployed environments,
while the original "Batch" mode is the default. To enable "Loader" mode, either:

* Set the `JOBS__LOADER__ENABLED` environment variable to `1`.
* Adjust the logic in `RedShirt.Example.JobWorker.Core` project's `Extensions/ServiceCollectionExtensions.cs` (as part
  of initializing this
  template)

### Important Note: Loader Mode + Kinesis

Important note: Loader mode is currently fundamentally incompatible with using Kinesis as a job source. The Kinesis job source is based around the idea of completing a batch of jobs before incrementing the tracker on the stream shard that the messages originate from. This is fundamentally incompatible with the Loader mode's philosophy of keeping an in-memory buffer of events. This is considered a safety measure because in the event of a sudden and catastrophic event (e.g. host-driven container death, hardware failure resulting in container death, or an infiltration team comprised entirely of well-trained roseate spoonbills resulting in container death) the Kinesis messages do not have any underlying queue technology that could reclaim the messages. The messages would just be lost.

If you choose to apply this template by combining Loader mode and Kinesis, please be aware of this warning.

All that being said, if you did choose to override this warning then one could modify the `HighLevelStreamSource` implementation of `IJobSource` to move the tracker and release the acquired lock immediately before returning a batch of messages and prune out the contents of the implementation of the `AcknowledgeCompletionAsync` method. Because of the message-safety concerns outlined above, this is just a hypothetical exercise for the reader.

# Initialisation

Recommended steps when using this as a template:

1. To change the namespace of this solution en-masse for your purposes, use the `init-repo.sh` script:

    ```bash
    bash init-repo.sh New.Namespace.Here
    ```

2. In the `RedShirt.Example.JobWorker.Core` project, update `IJobDataModel` interface and `JobDataModel` implementation
   to reflect the need of your project.
3. In the `RedShirt.Example.JobWorker.Core` project, update `SourceMessageConverter` and `SourceMessageSorter` to
   fit your needs for your project.
4. Update the `RedShirt.Example.JobWorker.Core.Logic` project to handle `IJobDataModel` jobs as needed by your project.
5. Select a message source type and remove the projects for the sources that you are not using.

# Testing

For local testing, see the `test/local/` folder.
