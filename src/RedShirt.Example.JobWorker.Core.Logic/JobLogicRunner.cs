using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services.Abstractions;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Services;

namespace RedShirt.Example.JobWorker.Core.Logic;

internal sealed class JobLogicRunner(
    IBarConnector barConnector,
    ISleepService sleepService,
    ILogger<JobLogicRunner> logger) : IJobLogicRunner
{
    public async Task<IJobLogicRunnerResponse> RunAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Sleeping for {DurationSeconds} seconds", job.Data.SleepDurationSeconds);
        await sleepService.DelayAsync(TimeSpan.FromSeconds(job.Data.SleepDurationSeconds), cancellationToken);

        var barId = Math.Max(1, job.Data.SleepDurationSeconds);
        var barRecord = await barConnector.GetByIdAsync(barId, cancellationToken);
        logger.LogInformation("Bar record {BarId} resolved to {BarName}", barRecord.Id, barRecord.Name);

        return new JobLogicRunnerResponse
        {
            Result = JobResult.Success
        };
    }
}