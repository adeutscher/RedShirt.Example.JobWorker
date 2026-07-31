using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.MessagePolling;

/// <summary>
///     Fetches jobs and passes them along to the Job Manager.
///     If no jobs are retrieved, then back off before trying again
/// </summary>
/// <param name="executionEndArbiter"></param>
/// <param name="jobRepository"></param>
/// <param name="jobSource"></param>
/// <param name="logger"></param>
/// <param name="loopOptions"></param>
#pragma warning disable S107
internal class BatchModeJobLoader(
    IExecutionEndArbiter executionEndArbiter,
    IJobLoaderStateService jobLoaderStateService,
    IJobRepository jobRepository,
    IJobSource jobSource,
    ILogger<BatchModeJobLoader> logger,
    IOptions<JobSourceConfigurationModel> jobSourceOptions,
    IOptions<LoopOptionsConfigurationModel> loopOptions,
    ISleepService sleepService) : IJobLoader
#pragma warning restore S107
{
    public async Task<HandlerResponseEnum> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            jobLoaderStateService.ReportLoaderStart();
            while (executionEndArbiter.ShouldKeepRunning())
            {
                await Policy
                    .Handle<NoJobException>(_ => executionEndArbiter.ShouldKeepRunning())
                    .RetryForeverAsync(async (_, retryAttempt) =>
                    {
                        // Exponential back-off, to the cap of a configurable amount
                        var span = TimeSpan.FromSeconds(Math.Min(loopOptions.Value.EffectiveMaxIdleWaitSeconds,
                            Math.Pow(2, retryAttempt)));
                        logger.LogTrace("Received no jobs from source, retrying in {Span} s", span.TotalSeconds);
                        await sleepService.DelayAsync(span, cancellationToken);
                    })
                    .ExecuteAsync(async () =>
                    {
                        JobSourceResponse jobResponse;
                        var stopwatch = Stopwatch.StartNew();

                        try
                        {
                            jobResponse = await jobSource.GetJobsAsync(jobSourceOptions.Value.EffectiveBatchSize,
                                cancellationToken);
                        }
                        catch (WorkerJobSourceException e) when (e is {IsCritical: false, CouldBeTransient: true})
                        {
                            logger.LogWarning(e, "Error getting jobs from source");
                            // Treat an anticipated transient error as a delay reason
                            throw new NoJobException();
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

                        await jobRepository.LoadAsync(jobResponse, cancellationToken);

                        await jobRepository.WaitForEmptyRepositoryAsync(cancellationToken);
                    });
            }
        }
        catch (NoJobException)
        {
            // pass, only thrown to here in the specific case of a SIGTERM.
        }
        finally
        {
            jobLoaderStateService.ReportLoaderStop();
        }

        return HandlerResponseEnum.Finished;
    }
}