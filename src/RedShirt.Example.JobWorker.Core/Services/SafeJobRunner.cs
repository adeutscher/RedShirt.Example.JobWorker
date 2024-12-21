using Microsoft.Extensions.Logging;
using Polly;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services;

internal interface ISafeJobRunner
{
    Task<bool> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default);
}

internal class SafeJobRunner(IJobLogicRunner jobLogicRunner, ILogger<SafeJobRunner> logger) : ISafeJobRunner
{
    public async Task<bool> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        try
        {
            await Policy.Handle<JobRetryException>()
                .RetryAsync(3)
                .ExecuteAsync(() => jobLogicRunner.RunAsync(job.Data, cancellationToken));
            return true;
        }
        catch (Exception e)
        {
            logger.LogError("Error running job: {EMessage}", e.Message);
            return false;
        }
    }
}