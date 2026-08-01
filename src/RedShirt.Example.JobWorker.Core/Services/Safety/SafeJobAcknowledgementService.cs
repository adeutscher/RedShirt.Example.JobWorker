using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Exceptions;
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
    Task<SafeAcknowledgementResult> AcknowledgeSafelyAsync(IRawJobModel job, bool success, Exception? exception = null,
        SafeAcknowledgementResult? previousAttempt = null, CancellationToken cancellationToken = default);
}

internal class SafeJobAcknowledgementService(
    IJobSource jobSource,
    IJobFailureHandler jobFailureHandler,
    ISleepService sleepService,
    ILogger<SafeJobAcknowledgementService> logger) : ISafeJobAcknowledgementService
{
    /// <summary>
    ///     Retry policy for acknowledgements
    ///     For the moment, deliberately choosing not to catch globally catch unplanned exceptions.
    ///     Unplanned exceptions should absolutely bring down the house, as they indicate a fundamental error with the job
    ///     source implementation or possibly an unaccounted-for permission issue in the profile/credentials used with the
    ///     underlying message source.
    /// </summary>
    private readonly AsyncRetryPolicy _acknowledgementRetryPolicy = Policy
        .Handle<WorkerJobSourceException>(e => e is {IsCritical: false, IsHandled: false, CouldBeTransient: true})
        .RetryAsync(Globals.AcknowledgementRetryCount,
            // Unfortunately, cannot have a common policy declaration AND our cancellationToken.
            // I chose to have the common policy declaration.
            (_, instanceCount) => sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, instanceCount))));

    public async Task<SafeAcknowledgementResult> AcknowledgeSafelyAsync(IRawJobModel job, bool success,
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
            if (!success && loggedFailureSuccessfully != true)
            {
#pragma warning disable S1854
                // Mark an attempt before the retry policy has a chance to throw an exception
                // Despite Sonar's opinion, not a useless assign.
                loggedFailureSuccessfully = false;
#pragma warning restore S1854
                await _acknowledgementRetryPolicy
                    .ExecuteAsync(() => jobFailureHandler.HandleFailureAsync(job, exception, cancellationToken));
                loggedFailureSuccessfully = true;
            }

            await _acknowledgementRetryPolicy
                .ExecuteAsync(() => jobSource.AcknowledgeCompletionAsync(job, success, cancellationToken));
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