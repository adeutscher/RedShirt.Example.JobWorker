using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services;

internal interface ISafeJobAcknowledgementService
{
    Task<bool> AcknowledgeSafelyAsync(IJobModel jobModel, bool success, CancellationToken cancellationToken);
}

internal class SafeJobAcknowledgementService(
    IJobSource jobSource,
    ISleepService sleepService,
    ILogger<SafeJobAcknowledgementService> logger) : ISafeJobAcknowledgementService
{
    private readonly AsyncRetryPolicy _acknowledgementRetryPolicy = Policy.Handle<Exception>()
        .RetryAsync(Globals.AcknowledgementRetryCount,
            async (e, instanceCount) =>
            {
                // Unfortunately, cannot have a common policy declaration AND our cancellationToken.
                // I chose to have the common policy declaration.
                await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, instanceCount)));
            }
        );

    public async Task<bool> AcknowledgeSafelyAsync(IJobModel jobModel, bool success,
        CancellationToken cancellationToken)
    {
        try
        {
            await _acknowledgementRetryPolicy
                .ExecuteAsync(() => jobSource.AcknowledgeCompletionAsync(jobModel, success, cancellationToken));
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Job acknowledge failed");
        }

        return false;
    }
}