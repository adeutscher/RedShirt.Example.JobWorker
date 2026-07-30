using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Exceptions.JobSource;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     Safety wrapper around job source acknowledgement that shall catch instances of TransientAcknowledgementException
///     and prevent them from bubbling up.
/// </summary>
internal interface ISafeJobAcknowledgementService
{
    Task<bool> AcknowledgeSafelyAsync(IJobRepositoryEntry job, bool success, CancellationToken cancellationToken);
}

internal class SafeJobAcknowledgementService(
    IJobSource jobSource,
    ISleepService sleepService,
    ILogger<SafeJobAcknowledgementService> logger) : ISafeJobAcknowledgementService
{
    /// <summary>
    ///     Retry policy for acknowledgements
    ///     For the moment, deliberately choosing not to catch globally catch unplanned exceptions.
    ///     Unplanned exceptions should absolutely bring down the house, as they indicate a fundamental error with the job
    ///     source implementation or possibly an unaccounted-for permission issue in the underlying message source.
    /// </summary>
    private readonly AsyncRetryPolicy<bool> _acknowledgementRetryPolicy = Policy<bool>
        .Handle<JobSourceAcknowledgementException>(e => e.IsTransient)
        .RetryAsync(Globals.AcknowledgementRetryCount,
            // Unfortunately, cannot have a common policy declaration AND our cancellationToken.
            // I chose to have the common policy declaration.
            (_, instanceCount) => sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, instanceCount))));

    public async Task<bool> AcknowledgeSafelyAsync(IJobRepositoryEntry job, bool success,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _acknowledgementRetryPolicy
                .ExecuteAsync(() => jobSource.AcknowledgeCompletionAsync(job.JobModel, success, cancellationToken));
        }
        catch (JobSourceHeartbeatException e)
        {
            logger.LogError(e, "Job acknowledge failed: {EMessage}", e.Message);
        }

        return false;
    }
}