using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Logic;

internal class JobLogicRunner(ISleepService sleepService, ILogger<JobLogicRunner> logger) : IJobLogicRunner
{
    public Task RunAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Sleeping for {DurationSeconds} seconds", job.Data.SleepDurationSeconds);
        return sleepService.DelayAsync(TimeSpan.FromSeconds(job.Data.SleepDurationSeconds), cancellationToken);
    }
}