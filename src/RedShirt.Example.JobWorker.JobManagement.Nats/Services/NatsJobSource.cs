using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal class NatsJobSource(
    INatsJetStreamContextFactory natsJetStreamContextFactory,
    IFetchNoWaitGetter fetchNoWaitGetter,
    IBodyRetriever bodyRetriever,
    ISourceMessageConverter converter,
    ISourceMessageSorter sorter,
    ILogger<NatsJobSource> logger,
    IOptions<NatsJobSource.ConfigurationModel> options) : IJobSource
{
    private readonly Lazy<Task<INatsJSContext>> _lazyContext =
        new(() => natsJetStreamContextFactory.CreateNatsJetStreamContextAsync());

    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is JobModel jobModel)
        {
            await jobModel.Message.AckAsync(cancellationToken: cancellationToken);
        }
    }

    public async Task<JobSourceResponse> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from NATS Stream: {StreamName}",
            options.Value.BatchSize, options.Value.StreamName);

        var js = await _lazyContext.Value;

        var consumer = await js.CreateOrUpdateConsumerAsync(options.Value.StreamName,
            new ConsumerConfig {Name = "c1", DurableName = "c1"}, cancellationToken);
        var fetchNoWaitOpts = new NatsJSFetchOpts
        {
            MaxMsgs = options.Value.EffectiveBatchSize,
            IdleHeartbeat = TimeSpan.FromSeconds(5)
        };

        var getJobsResponseItems = new List<IJobModel>();

        var result = fetchNoWaitGetter.FetchNoWaitAsync(consumer, fetchNoWaitOpts, cancellationToken);

        await foreach (var msg in result)
        {
            IJobDataModel? convertedMessage = null;
            string? body = null;
            try
            {
                body = bodyRetriever.GetMessageBody(msg);
                convertedMessage = converter.Convert(body);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing NATS message: {MessageBody}", body);
            }

            if (convertedMessage is null)
            {
                /*
                 * What exactly to do with bad messages is a bit up in the air at the moment.
                 * Deleting them from the queue is 'good enough' for now in this general template.
                 */

                // Delete the message so that it cannot keep gumming up the queue
                await msg.AckAsync(cancellationToken: cancellationToken);

                // Proceed to the next message
                continue;
            }

            // Got a message, add it to return set.
            getJobsResponseItems.Add(new JobModel
            {
                Message = msg,
                MessageId = msg.Subject,
                Data = convertedMessage
            });
        }

        /*
         * This implementation does not retry to fill the gap if parsing errors occurred.
         * This is considered acceptable as everything is assumed to parse correctly with
         * error-handling being an edge case.
         */

        return new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = 0,
            Items = sorter.GetSortedListOfJobs(getJobsResponseItems)
        };
    }

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public sealed class ConfigurationModel
    {
        public required string StreamName { get; init; }
        public required int BatchSize { get; init; }

        public int EffectiveBatchSize => Math.Max(BatchSize, 1);
    }
}