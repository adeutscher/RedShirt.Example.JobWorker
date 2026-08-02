using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Services.Safety;

/// <summary>
///     Safety wrapper around job source acknowledgement and failure invocation that shall catch and suppress non-critical
///     instances of <see cref="WorkerJobSourceException" /> and prevent them from bubbling up.
/// </summary>
internal interface ISafeJobAcknowledgementService
{
    Task<SafeAcknowledgementResult> AcknowledgeSafelyAsync(IRawJobModel job, CoreJobResult result,
        Exception? exception = null,
        SafeAcknowledgementResult? previousAttempt = null, CancellationToken cancellationToken = default);
}

internal sealed class SafeJobAcknowledgementService(
    IJobSource jobSource,
    IJobFailureHandler jobFailureHandler,
    ISleepService sleepService,
    ILogger<SafeJobAcknowledgementService> logger) : ISafeJobAcknowledgementService
{
    /// <summary>
    ///     Lazily built Polly v8 <see cref="ResiliencePipeline" /> shared across invocations.
    ///     For the moment, deliberately choosing not to catch globally catch unplanned exceptions.
    ///     Unplanned exceptions should absolutely bring down the house, as they indicate a fundamental error with the job
    ///     source implementation or possibly an unaccounted-for permission issue in the profile/credentials used with the
    ///     underlying message source.
    /// </summary>
    private ResiliencePipeline? _retryPipeline;

    /// <summary>
    ///     Get cached retry pipeline, declaring it if none is currently cached.
    /// </summary>
    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Globals.AcknowledgementRetryCount,
                ShouldHandle = new PredicateBuilder()
                    .Handle<WorkerJobSourceException>(e => e is
                        {IsCritical: false, IsHandled: false, CouldBeTransient: true}),
                // Do not use Polly-based delays between attempts
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    // Delay via ISleepService so tests can mock sleeps.
                    // AttemptNumber is 0-based; +1 preserves the prior Polly v7 1-based backoff (2^1, 2^2, …).
                    // Cancellation is intentionally omitted to match the previous shared-policy behaviour.
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber + 1)));
                }
            })
            .Build();
    }

    public async Task<SafeAcknowledgementResult> AcknowledgeSafelyAsync(IRawJobModel job, CoreJobResult result,
        Exception? exception = null, SafeAcknowledgementResult? previousAttempt = null,
        CancellationToken cancellationToken = default)
    {
        // Get status from previous attempt
        var loggedFailureSuccessfully = previousAttempt?.LoggedFailureSuccessfully;
        // Assume false: If a previous acknowledgement had succeeded, then we wouldn't be back here
        var acknowledgedSuccessfully = false;
        try
        {
            // Attempt to maintain idempotency with failure handling, should only run once per result
            if (!result.IsSuccessful() && loggedFailureSuccessfully != true)
            {
#pragma warning disable S1854
                // Mark an attempt before the retry policy has a chance to throw an exception
                // Despite Sonar's opinion, not a useless assign.
                loggedFailureSuccessfully = false;
#pragma warning restore S1854
                await GetRetryPipeline().ExecuteAsync(
                    async token =>
                        await jobFailureHandler.HandleFailureAsync(job, result.ToFailureType(), exception, token),
                    cancellationToken);
                loggedFailureSuccessfully = true;
            }

            await GetRetryPipeline().ExecuteAsync(
                async token => await jobSource.AcknowledgeAsync(job, result, token),
                cancellationToken);
            acknowledgedSuccessfully = true;
        }
        catch (WorkerJobSourceException e) when (!e.IsCritical)
        {
            logger.LogError(e, "Job acknowledge failed: {EMessage}", e.Message);
        }

        return new SafeAcknowledgementResult
        {
            LoggedFailureSuccessfully = loggedFailureSuccessfully,
            AcknowledgedSuccessfully = acknowledgedSuccessfully
        };
    }
}