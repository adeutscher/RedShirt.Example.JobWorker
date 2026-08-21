using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Health;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs.Polling;

/// <summary>
///     Fetches jobs and passes them along to the Job Manager.
///     If no jobs are retrieved, then back off before trying again
/// </summary>
internal sealed class BatchModeJobLoader(
    IJobSource jobSource,
    IJobRepository jobRepository,
    IJobIntakeService jobIntakeService,
    ICoreHealthStateUpdateService healthStateUpdateService,
    ILogger<BatchModeJobLoader> logger,
    ICoreConfigurationService coreConfigurationService,
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
#pragma warning disable S2139
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore S2139
        {
            logger.LogError(e, "Unexpected error getting jobs from source");
            healthStateUpdateService.NoteIncident();

            if (e is WorkerJobSourceException {CouldBeTransient: true} &&
                !coreConfigurationService.IsTreatingTransientExceptionAsFailure())
            {
                // Treat an anticipated transient error as a delay reason
                throw new NoJobException();
            }

            if (!coreConfigurationService.IsHaltOnFailure())
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

        await jobRepository.WaitForEmptyRepositoryAsync(cancellationToken);
    }
}