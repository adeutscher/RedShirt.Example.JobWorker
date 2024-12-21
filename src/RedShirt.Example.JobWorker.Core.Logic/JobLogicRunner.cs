using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.Logic;

internal class JobLogicRunner(ILogger<JobLogicRunner> logger) : IJobLogicRunner
{
    public Task RunAsync(IJobDataModel job, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Sleeping for {DurationSeconds} seconds", job.SleepDurationSeconds);
        return Task.Delay(job.SleepDurationSeconds * 1000, cancellationToken);
    }
}