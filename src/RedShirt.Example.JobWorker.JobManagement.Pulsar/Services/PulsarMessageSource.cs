using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;

internal interface IPulsarMessageSource
{
    Task<IPulsarMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

internal class PulsarMessageSource(
    IPulsarConsumerSource consumerSource,
    IPulsarRetryWrapperService retryWrapperService,
    IOptions<PulsarMessageSource.ConfigurationModel> options) : IPulsarMessageSource
{
    /// <summary>
    ///     Timeout for initial consume under short-polling. Because of the limited options exposed for the Pulsar client's
    ///     IConsumer, our implementation of short-polling is long-polling with a tight constraint.
    ///     Pulsar connection has been observed to take a bit longer on the first consume.
    ///     For the moment, choosing not to single out the first-ever zero-wait consume for the entire JobWorker process.
    /// </summary>
    internal const int InitialShortPollConsumeTimeoutMilliseconds = 750;

    /// <summary>
    ///     Timeout for follow-up consumes under short-polling. . Because of the limited options exposed for the Pulsar
    ///     client's IConsumer, our implementation of short-polling is long-polling with a tight constraint.
    /// </summary>
    internal const int FollowUpShortPollConsumeTimeoutMilliseconds = 500;

    public async Task<IPulsarMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var consumer = await retryWrapperService.RunAsync(
            consumerSource.GetConsumerAsync,
            cancellationToken);
        var messages = new List<IPulsarMessageContainer>();
        var consumeTimeout = options.Value.EffectiveWaitTimeSeconds > 0
            ? TimeSpan.FromSeconds(options.Value.EffectiveWaitTimeSeconds)
            : TimeSpan.FromMilliseconds(InitialShortPollConsumeTimeoutMilliseconds);

        while (messages.Count < batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var message = await retryWrapperService.RunAsync(
                ct => consumer.ConsumeAsync(consumeTimeout, ct),
                cancellationToken);

            if (message is null)
            {
                break;
            }

            // Follow-up attempts should only have a short timeout to avoid stacking waits
            consumeTimeout = TimeSpan.FromMilliseconds(FollowUpShortPollConsumeTimeoutMilliseconds);

            messages.Add(message);
        }

        return new PulsarMessageSourceResponse
        {
            Messages = messages
        };
    }

    public sealed class ConfigurationModel
    {
        /// <summary>
        ///     Seconds to wait for the next message on <c>ConsumeAsync</c>. Defaults to 1.
        ///     Clamped via <see cref="EffectiveWaitTimeSeconds" />. Zero uses
        ///     <see cref="PulsarMessageSource.InitialShortPollConsumeTimeoutMilliseconds" />
        ///     for the first consume.
        /// </summary>
        public required int WaitTimeSeconds { get; init; } = 1;

        public int EffectiveWaitTimeSeconds => Math.Max(0, WaitTimeSeconds);
    }
}