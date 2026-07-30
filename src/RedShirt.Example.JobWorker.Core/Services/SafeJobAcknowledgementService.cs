using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services;

internal interface ISafeJobAcknowledgementService
{
    Task<bool> AcknowledgeSafelyAsync(IJobRepositoryEntry job, bool success, CancellationToken cancellationToken);
}

internal class SafeJobAcknowledgementService(
    IJobSource jobSource,
    ISleepService sleepService,
    ILogger<SafeJobAcknowledgementService> logger) : ISafeJobAcknowledgementService
{
    private readonly AsyncRetryPolicy<bool> _acknowledgementRetryPolicy = Policy<bool>.Handle<Exception>()
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
        catch (Exception e)
        {
            logger.LogError(e, "Job acknowledge failed");
        }

        return false;
    }
}