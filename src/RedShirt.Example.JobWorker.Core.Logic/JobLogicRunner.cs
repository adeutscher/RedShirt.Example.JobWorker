using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Logic;

internal sealed class JobLogicRunner(ISleepService sleepService, ILogger<JobLogicRunner> logger) : IJobLogicRunner
{
    public async Task<JobResult> RunAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Sleeping for {DurationSeconds} seconds", job.Data.SleepDurationSeconds);
        await sleepService.DelayAsync(TimeSpan.FromSeconds(job.Data.SleepDurationSeconds), cancellationToken);
        return JobResult.Success;
    }
}