using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.Loader;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.MessagePolling;

#pragma warning disable S107
internal class LoaderModeJobLoader(
    IJobLoaderStateService jobLoaderStateService,
    // Confirming that it is intentional to use the base IExecutionEndArbiter
    IExecutionEndArbiter executionEndArbiter,
    IJobRepository jobRepository,
    IJobSource jobSource,
    ISleepService sleepService,
    ILogger<LoaderModeJobLoader> logger,
    IOptions<LoopOptionsConfigurationModel> loopOptions,
    IOptions<JobSourceConfigurationModel> jobSourceOptions) : IJobLoader
#pragma warning restore S107
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var breakLoop = false;

        jobLoaderStateService.ReportLoaderStart();

        try
        {
            // Declare policy once rather than constantly recreate
            var policy = Policy.Handle<ReasonToWaitException>(_ => executionEndArbiter.ShouldKeepRunning())
                .RetryForeverAsync(async (_, retryAttempt) =>
                {
                    // Exponential back-off, to the cap of a configurable amount
                    var span = TimeSpan.FromSeconds(Math.Min(loopOptions.Value.EffectiveMaxIdleWaitSeconds,
                        Math.Pow(2, retryAttempt)));
                    // Leaving phrasing vaguer than Batch implementation as there could be different reasons to wait
                    logger.LogTrace("Waiting before pulling more jobs, retrying in {Span} s",
                        span.TotalSeconds);
                    await sleepService.DelayAsync(span, cancellationToken);
                });

            while (!breakLoop && executionEndArbiter.ShouldKeepRunning())
            {
                try
                {
                    await policy
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
                                    while (!await jobRepository.WaitForJobDemandAsync(TimeSpan.FromSeconds(5),
                                               cancellationToken))
                                    {
                                        if (!executionEndArbiter.ShouldKeepRunning())
                                        {
                                            throw new AbortJobLoaderException();
                                        }
                                    }
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
                            logger.LogTrace("Fetched {JobResponseItemsCount} jobs in {Elapsed}",
                                jobResponse.Items.Count,
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
        catch (AbortJobLoaderException)
        {
            // Using AbortJobLoaderException as an exit-override signal
            // Valid behaviour, pass
        }
        finally
        {
            // Using finally makes the use of loader stop exception-safe
            jobLoaderStateService.ReportLoaderStop();
        }
    }
}