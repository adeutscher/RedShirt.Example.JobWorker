using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

internal class RabbitMqJobSource(
    IRabbitMqChannelRetryWrapper channelRetryWrapper,
    IOptions<RabbitMqQueueConfigurationModel> configuration,
    ILogger<RabbitMqJobSource> logger)
    : IJobSource
{
    private bool _nextConnectionAttemptShouldForceNewConnection;

    /// <summary>
    ///     Actually fetch results from RabbitMQ.
    ///     Mostly sequestered off to make for a smaller try-catch statement in <see cref="GetJobsAsync" />.
    /// </summary>
    /// <param name="batchSize"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<InnerResults> GetResultsAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BasicGetResult>();
        while (results.Count < batchSize)
        {
            BasicGetResult? result = null;
            try
            {
                await channelRetryWrapper.GetChannelAndDoActionWithRetryAsync(
                    async (channel, ct) =>
                    {
                        result = await channel.BasicGetAsync(configuration.Value.QueueName, false, ct);
                    }, _nextConnectionAttemptShouldForceNewConnection, cancellationToken: cancellationToken);
                _nextConnectionAttemptShouldForceNewConnection = false;
            }
            catch (WorkerJobSourceException e) when (results.Count > 0 && e.IsPotentialCredentialProblem())
            {
                // If we already have some results, then absorb the exception and deal with what we've got
                _nextConnectionAttemptShouldForceNewConnection = true;
            }

            /*
             * Historical note: Prior to adding Polly support to RabbitMQ, we used to capture AlreadyClosedException instances
             * thrown when the connection had closed and not been auto-recovered.
             *
             * Modern version wraps this in a WorkerJobSourceException that could be transient.
             * As such, letting the exception bubble up.
             */

            if (result is null)
                // Nothing more to grab at the moment.
            {
                break;
            }

            results.Add(result);
        }

        return new InnerResults
        {
            Items = results
        };
    }

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not RabbitMqRawJobModel rabbitMqJobModel)
        {
            // Message did not originate from RabbitMQ, return
            return;
        }

        await channelRetryWrapper.GetChannelAndDoActionWithRetryAsync(async (channel, ct) =>
        {
            if (result.IsSuccessful())
            {
                await channel.BasicAckAsync(rabbitMqJobModel.DeliveryTag, false, ct);
            }
            else
            {
                // If recoverable, then NAck with requeue so the message can be delivered again.
                // Empty / Parsing / InvalidData: NAck without requeue (dead-letter if the queue is configured for it).
                await channel.BasicNackAsync(rabbitMqJobModel.DeliveryTag, false, result.IsRecoverableFailure(),
                    ct);
            }
        }, cancellationToken: cancellationToken);
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Fetching up to {EffectiveBatchSize} messages from RabbitMQ Queue: {QueueName}",
            batchSize, configuration.Value.QueueName);

        InnerResults results;
        try
        {
            results = await GetResultsAsync(batchSize, cancellationToken);
        }
        catch (WorkerJobSourceException e)
        {
            // Store more context for later
            _nextConnectionAttemptShouldForceNewConnection = e.IsPotentialCredentialProblem();
            throw;
        }

        return new JobSourceResponse
        {
            Items = results.Items.Select(r => new RabbitMqRawJobModel
            {
                MessageId = r.BasicProperties.MessageId ?? "UNKNOWN",
                IdempotencyId = r.BasicProperties.MessageId,
                DeliveryTag = r.DeliveryTag,
                CreatedAtUtc = DateTime.UtcNow,
                Body = Encoding.UTF8.GetString(r.Body.ToArray())
                // ReSharper disable once UseCollectionExpression
            }).ToList<IRawJobModel>()
        };
    }

    public int RecommendedHeartbeatIntervalSeconds => 0;

    public bool IsSubscriptionSource => false;

    public Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        /*
         * Not necessary. Heartbeats are managed by the persistence of the IConnection object.
         * See: https://www.rabbitmq.com/client-libraries/dotnet-api-guide
         * Since it is not necessary to do any thinking, then it is also not necessary to check that the provided
         * IRawJobDataModel is even a RabbitMqRawJobModel.
         */
        return Task.CompletedTask;
    }

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    private sealed class InnerResults
    {
        public required List<BasicGetResult> Items { get; init; }
    }
}