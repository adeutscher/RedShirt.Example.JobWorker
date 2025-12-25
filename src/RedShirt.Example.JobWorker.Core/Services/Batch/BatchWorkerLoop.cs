using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models.Batch;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Batch.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services.Batch;

internal interface IBatchWorkerLoop
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Fetches jobs and passes them along to the Job Manager.
///     If no jobs are retrieved, then back off before trying again
/// </summary>
/// <param name="executionEndArbiter"></param>
/// <param name="jobManager"></param>
/// <param name="jobSource"></param>
/// <param name="logger"></param>
/// <param name="loopOptions"></param>
internal class BatchWorkerLoop(
    IExecutionEndArbiter executionEndArbiter,
    IJobManager jobManager,
    IJobSource jobSource,
    ISourceMessageSorter sorter,
    ILogger<BatchWorkerLoop> logger,
    IOptions<JobSourceConfigurationModel> jobSourceOptions,
    IOptions<LoopOptionsConfigurationModel> loopOptions) : IBatchWorkerLoop
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (executionEndArbiter.ShouldKeepRunning())
            {
                await Policy.Handle<NoJobException>(_ => executionEndArbiter.ShouldKeepRunning())
                    .WaitAndRetryForeverAsync(retryAttempt =>
                            // Exponential back-off, to the cap of a configurable amount
                            TimeSpan.FromSeconds(Math.Min(loopOptions.Value.EffectiveMaxIdleWaitSeconds,
                                Math.Pow(2, retryAttempt))),
                        (_, span) =>
                        {
                            logger.LogTrace("Received no jobs from source, retrying in {Span} s", span.TotalSeconds);
                        })
                    .ExecuteAsync(async () =>
                    {
                        var jobResponse = await jobSource.GetJobsAsync(jobSourceOptions.Value.EffectiveBatchSize,
                            cancellationToken);
                        if (jobResponse.Items.Count == 0)
                        {
                            // Throwing an exception in order to leverage Polly's handling for incremental backoff.
                            throw new NoJobException();
                        }

                        var sortedItems = sorter
                            .GetSortedListOfJobs(jobResponse.Items.Select(i => new BatchJobWrapper
                            {
                                JobModel = i
                            }).ToList()).Select(j => j.JobModel).ToList();
                        await jobManager.RunAsync(sortedItems, cancellationToken);
                    });
            }
        }
        catch (NoJobException)
        {
            // pass, only thrown to here in the specific case of a SIGTERM.
        }
    }
}