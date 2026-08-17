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
    internal const int ZeroWaitInitialConsumeTimeoutMilliseconds = 750;
    internal const int FollowUpConsumeTimeoutMilliseconds = 500;

    public async Task<IPulsarMessageSourceResponse> GetMessagesAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        var consumer = await retryWrapperService.RunAsync(
            consumerSource.GetConsumerAsync,
            cancellationToken);
        var messages = new List<IPulsarMessageContainer>();
        var consumeTimeout = options.Value.EffectiveWaitTimeSeconds > 0
            ? TimeSpan.FromSeconds(options.Value.EffectiveWaitTimeSeconds)
            : TimeSpan.FromMilliseconds(ZeroWaitInitialConsumeTimeoutMilliseconds);

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
            consumeTimeout = TimeSpan.FromMilliseconds(FollowUpConsumeTimeoutMilliseconds);

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
        ///     <see cref="PulsarMessageSource.ZeroWaitInitialConsumeTimeoutMilliseconds" />
        ///     for the first consume.
        /// </summary>
        public required int WaitTimeSeconds { get; init; } = 1;

        public int EffectiveWaitTimeSeconds => Math.Max(0, WaitTimeSeconds);
    }
}