using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services.Abstractions;
using RedShirt.Example.JobWorker.Common.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Logic;

internal sealed class JobLogicRunner(ISleepService sleepService, ILogger<JobLogicRunner> logger) : IJobLogicRunner
{
    public async Task<IJobLogicRunnerResponse> RunAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Sleeping for {DurationSeconds} seconds", job.Data.SleepDurationSeconds);
        await sleepService.DelayAsync(TimeSpan.FromSeconds(job.Data.SleepDurationSeconds), cancellationToken);
        return new JobLogicRunnerResponse
        {
            Result = JobResult.Success
        };
    }
}