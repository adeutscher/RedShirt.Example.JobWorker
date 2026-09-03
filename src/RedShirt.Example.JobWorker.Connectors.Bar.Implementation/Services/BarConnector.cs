using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Models;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Services;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Factories;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services.Resilience;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services;

/// <summary>
///     Bar connector implementation: maps Core requests onto the Bar HTTP client under the retry wrapper.
///     This connector is a stand-in for an API template client; refer to <c>docs/bar-connector.md</c>
///     for last-mile instructions when adapting to your target API.
/// </summary>
internal sealed class BarConnector(
    IBarApiClientFactory barApiClientFactory,
    IBarRetryWrapperService retryWrapperService,
    ISleepService sleepService,
    ILogger<BarConnector> logger,
    IOptions<BarConnector.ConfigurationModel> options) : IBarConnector
{
    private const int DefaultReasonToWaitFallbackSeconds = 15;

    private ResiliencePipeline? _reasonToWaitPipeline;

    /// <summary>
    ///     Recycled Polly pipeline that respects <see cref="BarReasonToWaitException" /> indefinitely.
    ///     Cancellation is the Core job worker configuration's problem; this connector keeps trying respectfully
    ///     when the dependency signals rate limiting or another reason to wait.
    /// </summary>
    private ResiliencePipeline GetReasonToWaitPipeline()
    {
        return _reasonToWaitPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // Functionally infinite retries
                MaxRetryAttempts = int.MaxValue,
                ShouldHandle = args =>
                {
                    if (args.Context.CancellationToken.IsCancellationRequested)
                    {
                        return PredicateResult.False();
                    }

                    return args.Outcome.Exception is BarReasonToWaitException
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                // Do not delay via polly, use ISleepService.
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    if (args.Outcome.Exception is not BarReasonToWaitException reasonToWait)
                    {
                        return;
                    }

                    var baseDelay = reasonToWait.RetryAfter ?? options.Value.EffectiveReasonToWaitFallback;
                    // Polly v8 AttemptNumber is zero-based (0 on the first retry). Add linear slack on top of
                    // RetryAfter/fallback, assuming that the API may need a little extra time to recognize that
                    // the rate-limiting window has passed (0s, then 1s, then 2s, etc).
                    var attemptBuffer = TimeSpan.FromSeconds(args.AttemptNumber);
                    var delay = baseDelay + attemptBuffer;
                    logger.LogWarning(reasonToWait,
                        "Bar indicated a reason to wait with a {Type}; delaying {Delay} (base {BaseDelay}, attempt buffer {AttemptBuffer}) before retry (attempt {AttemptNumber})",
                        reasonToWait.GetType().Name, delay, baseDelay, attemptBuffer, args.AttemptNumber + 1);
                    await sleepService.DelayAsync(delay, args.Context.CancellationToken);
                }
            })
            .Build();
    }

    private Task<T> ExecuteRespectingReasonToWaitAsync<T>(
        Func<CancellationToken, Task<T>> func,
        CancellationToken cancellationToken)
    {
        return GetReasonToWaitPipeline().ExecuteAsync(
            async token => await func(token),
            cancellationToken).AsTask();
    }

    private Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return ExecuteRespectingReasonToWaitAsync(
            token => retryWrapperService.RunAsync(operation, token),
            cancellationToken);
    }

    public Task<CreateBarConnectorResponse> CreateAsync(CreateBarConnectorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteWithResilienceAsync(async innerToken =>
        {
            var client = barApiClientFactory.CreateBarApiClient();
            return await client.CreateBarAsync(request, innerToken);
        }, cancellationToken);
    }

    public Task<GetBarConnectorResponse> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return ExecuteWithResilienceAsync(innerToken =>
        {
            var client = barApiClientFactory.CreateBarApiClient();
            return client.GetBarByIdAsync(id, innerToken);
        }, cancellationToken);
    }

    internal sealed class ConfigurationModel
    {
        /// <summary>
        ///     Fallback wait duration when a <see cref="BarReasonToWaitException" /> does not specify
        ///     <see cref="BarReasonToWaitException.RetryAfter" />.
        ///     When null, <see cref="DefaultReasonToWaitFallbackSeconds" /> is used.
        /// </summary>
        public required int? ReasonToWaitFallbackSeconds { get; init; }

        public TimeSpan EffectiveReasonToWaitFallback =>
            TimeSpan.FromSeconds(Math.Max(1,
                ReasonToWaitFallbackSeconds ?? DefaultReasonToWaitFallbackSeconds));
    }
}