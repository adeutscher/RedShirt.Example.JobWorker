using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.MessagePolling;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs.Polling;

#pragma warning disable S107
internal sealed class LoaderModeJobLoader : IJobLoader, IDisposable
#pragma warning restore S107
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ICoreConfigurationService _coreConfigurationService;
    private readonly Lock _generalLock = new();
    private readonly ICoreHealthStateUpdateService _healthStateUpdateService;
    private readonly IJobIntakeService _jobIntakeService;
    private readonly IJobRepository _jobRepository;
    private readonly IJobSource _jobSource;
    private readonly ILogger<LoaderModeJobLoader> _logger;

    private bool _disposed;

    private void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (_generalLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    private void OnExecutionEndArbiterStop(Exception? exception)
    {
        lock (_generalLock)
        {
            _cancellationTokenSource.Cancel();
        }
    }

    private async Task DoOperationWithLinkedToken(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Construct a linked CTS to tie it to _cancellationTokenSource.
         *
         * Wrapping this like so instead of returning a token because otherwise
         * the cancellation token source will be prematurely disposed.
         */

        CancellationTokenSource? linkedCts = null;
        try
        {
            CancellationToken linkedToken;
            lock (_generalLock)
            {
                if (_disposed)
                {
                    throw new OperationCanceledException();
                }

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationTokenSource.Token);
                linkedToken = linkedCts.Token;
            }

            await operation(linkedToken);
        }
        finally
        {
            linkedCts?.Dispose();
        }
    }

    private async Task WaitForDemandAsync(CancellationToken cancellationToken)
    {
        // Wait until the next worker needs something to do when jobs are already in-flight.
        try
        {
            await DoOperationWithLinkedToken(_jobRepository.WaitForJobDemandAsync, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AbortJobLoaderLoopException();
        }
    }

    private async Task<IJobSourceResponse> GetJobsAsync(int sizeToGet, CancellationToken cancellationToken)
    {
        IJobSourceResponse jobResponse;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            jobResponse = await _jobSource.GetJobsAsync(
                Math.Min(sizeToGet, _coreConfigurationService.FetchCount),
                cancellationToken);
        }
#pragma warning disable S2139
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore S2139
        {
            _logger.LogError(e, "Unexpected error getting jobs from source");
            _healthStateUpdateService.NoteIncident();

            if (e is WorkerJobSourceException {CouldBeTransient: true} &&
                !_coreConfigurationService.IsTreatingTransientExceptionAsFailure)
            {
                // Treat an anticipated transient error as a delay reason
                throw new NoJobException();
            }

            if (!_coreConfigurationService.IsHaltOnFailure)
            {
                // Soft-fail: treat like an empty poll so the loader loop can back off and retry.
                throw new NoJobException();
            }

            // Throw upwards to trigger halt
            throw;
        }

        stopwatch.Stop();
        _logger.LogTrace("Fetched {JobResponseItemsCount} jobs in {Elapsed}",
            jobResponse.Items.Count,
            stopwatch);
        return jobResponse;
    }

#pragma warning disable S107
    public LoaderModeJobLoader(
        IJobSource jobSource,
        IExecutionEndArbiter executionEndArbiter,
        IJobRepository jobRepository,
        IJobIntakeService jobIntakeService,
        ICoreHealthStateUpdateService healthStateUpdateService,
        ICoreConfigurationService coreConfigurationService,
        ILogger<LoaderModeJobLoader> logger)
#pragma warning restore S107
    {
        _jobSource = jobSource;
        _jobRepository = jobRepository;
        _jobIntakeService = jobIntakeService;
        _healthStateUpdateService = healthStateUpdateService;
        _coreConfigurationService = coreConfigurationService;
        _logger = logger;

        executionEndArbiter.AddOnStopCallback(OnExecutionEndArbiterStop);
    }

    public void Dispose()
    {
        Dispose(true);
        // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
        GC.SuppressFinalize(this);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await WaitForDemandAsync(cancellationToken);

        var watchedJobCount = await _jobRepository.GetWatchedJobsCountAsync(cancellationToken);
        var sizeToGet = _coreConfigurationService.FetchCount - watchedJobCount;

        if (sizeToGet <= 0)
        {
            // Throwing an exception in order to leverage Polly's handling for incremental backoff.
            throw new BacklogFullException();
        }

        var jobResponse = await GetJobsAsync(sizeToGet, cancellationToken);

        if (jobResponse.Items.Count == 0)
        {
            // Throwing an exception in order to leverage Polly's handling for incremental backoff.
            throw new NoJobException();
        }

        await _jobIntakeService.SubmitAsync(jobResponse, cancellationToken);
    }
}