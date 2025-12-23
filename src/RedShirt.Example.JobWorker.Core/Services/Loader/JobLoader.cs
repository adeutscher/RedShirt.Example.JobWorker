using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.Loader;

/// <summary>
///     The loader is responsible for loading messages from the job source into the in-memory job repository.
///     It should maintain the desired backlog size of jobs in the repository.
/// </summary>
internal interface IJobLoader
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

internal class JobLoader(
    // Confirming that it is intentional to use the base IExecutionEndArbiter
    IExecutionEndArbiter executionEndArbiter,
    IJobRepository jobRepository,
    IJobSource jobSource,
    ILogger<JobLoader> logger,
    IOptions<LoopOptionsConfigurationModel> loopOptions,
    IOptions<JobSourceConfigurationModel> jobSourceOptions) : IJobLoader
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var breakLoop = false;

        while (!breakLoop && executionEndArbiter.ShouldKeepRunning())
        {
            try
            {
                await Policy.Handle<ReasonToWaitException>(_ => executionEndArbiter.ShouldKeepRunning())
                    .WaitAndRetryForeverAsync(retryAttempt =>
                            // Exponential back-off, to the cap of a configurable amount
                            TimeSpan.FromSeconds(Math.Min(loopOptions.Value.EffectiveMaxIdleWaitSeconds,
                                Math.Pow(2, retryAttempt))),
                        (_, span) =>
                        {
                            // Leaving phrasing vaguer than Batch implementation as there could be different reasons to wait
                            logger.LogTrace("Waiting before pulling more jobs, retrying in {Span} s",
                                span.TotalSeconds);
                        })
                    .ExecuteAsync(async () =>
                    {
                        var backlogMaxCount = jobRepository.GetBacklogMaxCount();
                        int sizeToGet;

                        if (backlogMaxCount == 0)
                        {
                            // No configured backlog, so wait until the next worker needs something to do.
                            var totalWatchedJobs = await jobRepository.GetWatchedJobsCountAsync(cancellationToken);
                            if (totalWatchedJobs > 0)
                            {
                                await jobRepository.WaitForJobDemandAsync(cancellationToken);
                            }

                            /*
                             * Using EffectiveBatchSize rather than the number of free workers is considered working
                             * as intended for now. It is equivalent to the current logic of the default Batch mode.
                             */
                            sizeToGet = jobSourceOptions.Value.EffectiveBatchSize;
                        }
                        else
                        {
                            var inactiveJobCount = await jobRepository.GetInactiveJobCountAsync(cancellationToken);
                            sizeToGet = backlogMaxCount - inactiveJobCount;
                            if (sizeToGet <= 0)
                            {
                                // Throwing an exception in order to leverage Polly's handling for incremental backoff.
                                throw new BacklogFullException();
                            }
                        }

                        var stopwatch = Stopwatch.StartNew();
                        var jobResponse = await jobSource.GetJobsAsync(
                            Math.Min(sizeToGet, jobSourceOptions.Value.EffectiveBatchSize),
                            cancellationToken);
                        stopwatch.Stop();
                        logger.LogTrace("Fetched {JobResponseItemsCount} jobs in {Elapsed}", jobResponse.Items.Count,
                            stopwatch);
                        if (jobResponse.Items.Count == 0)
                        {
                            // Throwing an exception in order to leverage Polly's handling for incremental backoff.
                            throw new NoJobException();
                        }

                        await jobRepository.LoadAsync(jobResponse, cancellationToken);
                    });
            }
            catch (ReasonToWaitException)
            {
                // only thrown to here in the specific case of a SIGTERM.
                breakLoop = true;
            }
        }
    }
}