using Microsoft.Extensions.Logging;
using Polly;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.Batch;

/// <summary>
///     Simpler version of JobManager that simply does a foreach loop through jobs rather than any multithreading.
///     Not recommended for use with job source implementations that require heartbeats managed to be managed by Core,
///     as this implementation doesn't use heartbeats at all.
/// </summary>
/// <param name="logger"></param>
/// <param name="safeJobRunner"></param>
/// <param name="jobSource"></param>
internal class LightJobManager(ILogger<LightJobManager> logger, ISafeJobRunner safeJobRunner, IJobSource jobSource)
    : IJobManager
{
    private uint _totalBatches;
    private ulong _totalJobs;

    public async Task RunAsync(List<IJobModel> items, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();

        var successfullyCompletedJobsCount = items.Count;

        foreach (var job in items)
        {
            var result = await safeJobRunner.RunSafelyAsync(job, cancellationToken);
            if (!result)
            {
                successfullyCompletedJobsCount--;
            }

            try
            {
                await Policy.Handle<Exception>()
                    .RetryAsync(Globals.AcknowledgementRetryCount,
                        async (e, instanceCount) =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, instanceCount)), cancellationToken);
                        }
                    )
                    .ExecuteAsync(() => jobSource.AcknowledgeCompletionAsync(job, result, cancellationToken));
            }
            catch (Exception e)
            {
                logger.LogError(e, "Job acknowledge failed");
            }
        }

        timer.Stop();
        logger.LogDebug("Successfully finished {JobsSuccessful}/{JobsTotal} jobs in {ElapsedMilliseconds} ms",
            successfullyCompletedJobsCount, items.Count, timer.ElapsedMilliseconds);

        _totalJobs += (uint) items.Count;
        logger.LogTrace("Total Jobs: {TotalJobs} ({TotalBatches} batches)", _totalJobs, ++_totalBatches);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Nothing to do here in this implementation
        return Task.CompletedTask;
    }
}