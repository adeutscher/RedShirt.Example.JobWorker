using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.MessagePolling;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.MessagePolling;

#pragma warning disable S107
internal sealed class LoaderModeJobLoader(
    IJobSource jobSource,
    IExecutionEndArbiter executionEndArbiter,
    IJobRepository jobRepository,
    IJobIntakeService jobIntakeService,
    ICoreHealthStateUpdateService healthStateUpdateService,
    ILogger<LoaderModeJobLoader> logger,
    IOptions<CoreConfigurationModel> coreOptions,
    IOptions<JobSourceConfigurationModel> jobSourceOptions) : IJobLoader
#pragma warning restore S107
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
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
                        throw new AbortJobLoaderLoopException();
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

        IJobSourceResponse jobResponse;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            jobResponse = await jobSource.GetJobsAsync(
                Math.Min(sizeToGet, jobSourceOptions.Value.EffectiveBatchSize),
                cancellationToken);
        }
#pragma warning disable S2139
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore S2139
        {
            logger.LogError(e, "Unexpected error getting jobs from source");
            healthStateUpdateService.NoteIncident();

            if (e is WorkerJobSourceException {CouldBeTransient: true})
            {
                // Treat an anticipated transient error as a delay reason
                throw new NoJobException();
            }

            if (!coreOptions.Value.HaltOnFailure)
            {
                // Soft-fail: treat like an empty poll so the loader loop can back off and retry.
                throw new NoJobException();
            }

            // Throw upwards to trigger halt
            throw;
        }

        stopwatch.Stop();
        logger.LogTrace("Fetched {JobResponseItemsCount} jobs in {Elapsed}",
            jobResponse.Items.Count,
            stopwatch);
        if (jobResponse.Items.Count == 0)
        {
            // Throwing an exception in order to leverage Polly's handling for incremental backoff.
            throw new NoJobException();
        }

        await jobIntakeService.SubmitAsync(jobResponse, cancellationToken);
    }
}