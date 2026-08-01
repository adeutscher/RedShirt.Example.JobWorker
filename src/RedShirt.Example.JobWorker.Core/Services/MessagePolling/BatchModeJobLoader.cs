using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.MessagePolling;

/// <summary>
///     Fetches jobs and passes them along to the Job Manager.
///     If no jobs are retrieved, then back off before trying again
/// </summary>
internal class BatchModeJobLoader(
    IJobSource jobSource,
    IJobRepository jobRepository,
    IJobIntakeService jobIntakeService,
    ILogger<BatchModeJobLoader> logger,
    IOptions<JobSourceConfigurationModel> jobSourceOptions) : IJobLoader
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        IJobSourceResponse jobResponse;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            jobResponse = await jobSource.GetJobsAsync(jobSourceOptions.Value.EffectiveBatchSize, cancellationToken);
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

        await jobIntakeService.SubmitAsync(jobResponse, cancellationToken);

        await jobRepository.WaitForEmptyRepositoryAsync(cancellationToken);
    }
}