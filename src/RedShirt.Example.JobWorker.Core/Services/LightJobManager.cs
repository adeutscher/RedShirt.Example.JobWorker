using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Models;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     Simpler version of JobManager that simply does a foreach loop through jobs rather than any multithreading.
///     Not recommended for use with job source implementations that require heartbeats, as this implementation doesn't use
///     heartbeats at all.
/// </summary>
/// <param name="logger"></param>
/// <param name="safeJobRunner"></param>
/// <param name="jobSource"></param>
internal class LightJobManager(ILogger<LightJobManager> logger, ISafeJobRunner safeJobRunner, IJobSource jobSource)
    : IJobManager
{
    private uint _totalBatches;
    private ulong _totalJobs;

    public async Task RunAsync(JobSourceResponse response, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();

        var successfullyCompletedJobsCount = response.Items.Count;

        foreach (var job in response.Items)
        {
            var result = await safeJobRunner.RunSafelyAsync(job, cancellationToken);
            if (!result)
            {
                successfullyCompletedJobsCount--;
            }

            try
            {
                await jobSource.AcknowledgeCompletionAsync(job, result, cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Job acknowledge failed");
            }
        }

        timer.Stop();
        logger.LogDebug("Successfully finished {JobsSuccessful}/{JobsTotal} jobs in {ElapsedMilliseconds} ms",
            successfullyCompletedJobsCount, response.Items.Count, timer.ElapsedMilliseconds);

        _totalJobs += (uint) response.Items.Count;
        logger.LogTrace("Total Jobs: {TotalJobs} ({TotalBatches} batches)", _totalJobs, ++_totalBatches);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Nothing to do here in this implementation
        return Task.CompletedTask;
    }
}