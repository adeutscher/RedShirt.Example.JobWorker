using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal interface INatsMessageSource
{
    Task<NatsMessageSourceResponse> FetchMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class NatsMessageSource(
    INatsConnectionRetryWrapper connectionRetryWrapper,
    INatsRetryWrapperService retryWrapperService,
    INatsExceptionArbiterService exceptionArbiterService,
    IOptions<NatsMessageSource.ConfigurationModel> options,
    IOptions<NatsStreamTimeoutConfigurationModel> timeoutOptions) : INatsMessageSource
{
    private bool _nextConnectionAttemptShouldForceNewConnection;

    /// <summary>
    ///     Idle heartbeat for pull requests: a fraction of ack-wait / visibility timeout, clamped to a
    ///     sensible client range so long waits still get keepalive traffic.
    /// </summary>
    private TimeSpan IdleHeartbeat => TimeSpan.FromSeconds(Math.Clamp(
        timeoutOptions.Value.EffectiveVisibilityTimeoutSeconds / 4.0, 5, 30));

    private async Task<List<INatsJSMsg<NatsMemoryOwner<byte>>>> FetchBatchWithNoWaitAsync(int batchSize,
        INatsJSConsumer consumer, CancellationToken cancellationToken)
    {
        var items = new List<INatsJSMsg<NatsMemoryOwner<byte>>>();

        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = batchSize,
            IdleHeartbeat = IdleHeartbeat
        };

        var result = consumer.FetchNoWaitAsync<NatsMemoryOwner<byte>>(fetchOpts, cancellationToken: cancellationToken);
        await foreach (var msg in result)
        {
            items.Add(msg);
        }

        return items;
    }

    /// <summary>
    ///     Silly little wrapper to prevent try-catches crowding in <see cref="FetchMessagesAsync" /> on the different
    ///     reasons to call <see cref="INatsConnectionRetryWrapper.GetConsumerAndDoActionWithRetryAsync" />.
    /// </summary>
    /// <param name="operationCallback">Callback to be run.</param>
    /// <param name="suppressProblem">
    ///     Setting to <c>true</c> suggests a reason to suppress a thrown problem. My thinking behind
    ///     this was to suppress errors in the event that we have already received at least one message, making reconnecting
    ///     the next invocation's problem.
    /// </param>
    /// <param name="cancellationToken"></param>
    private async Task DoOperationAsync(Func<INatsJSConsumer, CancellationToken, Task> operationCallback,
        bool suppressProblem,
        CancellationToken cancellationToken)
    {
        try
        {
            await connectionRetryWrapper.GetConsumerAndDoActionWithRetryAsync(operationCallback,
                _nextConnectionAttemptShouldForceNewConnection,
                cancellationToken: cancellationToken);
            // Have run cleanly without an explosion
            _nextConnectionAttemptShouldForceNewConnection = false;
        }
        catch (Exception e)
        {
            _nextConnectionAttemptShouldForceNewConnection = e.IsPotentialCredentialProblem()
                                                             || exceptionArbiterService.GetReport(e).CouldBeTransient;

            if (!suppressProblem)
            {
                throw;
            }
            // If we are suppressing the problem, then do not throw.
            // My goal with this is to preserve a hold on messages that have already been fetched by this FetchMessagesAsync call.
        }
    }

    public async Task<NatsMessageSourceResponse> FetchMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        NatsMessageSourceResponse? response = null;

        if (options.Value.EffectiveWaitTimeSeconds <= 0)
        {
            await DoOperationAsync(async (consumer, ct) =>
            {
                response = new NatsMessageSourceResponse
                {
                    Messages = await retryWrapperService.RunAsync(
                        token => FetchBatchWithNoWaitAsync(batchSize, consumer, token), ct)
                };
            }, false, cancellationToken);
            return response!;
        }

        var items = new List<INatsJSMsg<NatsMemoryOwner<byte>>>();
        INatsJSMsg<NatsMemoryOwner<byte>>? firstResult = null;

        await DoOperationAsync(async (consumer, ct) =>
        {
            firstResult = await retryWrapperService.RunAsync<INatsJSMsg<NatsMemoryOwner<byte>>?>(async token =>
                await consumer.NextAsync<NatsMemoryOwner<byte>>(opts: new NatsJSNextOpts
                {
                    IdleHeartbeat = IdleHeartbeat,
                    Expires = TimeSpan.FromSeconds(options.Value.EffectiveWaitTimeSeconds)
                }, cancellationToken: token), ct);
        }, false, cancellationToken);

        if (firstResult is not null)
        {
            items.Add(firstResult);
            if (batchSize >= 1)
            {
                await DoOperationAsync(async (consumer, ct) =>
                {
                    items.AddRange(await retryWrapperService.RunAsync(
                        token => FetchBatchWithNoWaitAsync(batchSize - 1, consumer, token), ct));
                }, true, cancellationToken);
            }
        }

        response = new NatsMessageSourceResponse
        {
            Messages = items
        };

        return response;
    }

    public sealed class ConfigurationModel
    {
        public required int WaitTimeSeconds { get; init; }
        public int EffectiveWaitTimeSeconds => Math.Max(WaitTimeSeconds, 0);
    }
}