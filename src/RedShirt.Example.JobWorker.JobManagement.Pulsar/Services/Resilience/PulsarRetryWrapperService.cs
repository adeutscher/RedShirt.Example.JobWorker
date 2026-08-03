using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

/// <summary>
///     Retries Pulsar client operations that fail with non-critical transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerJobSourceException" /> with
///     <see cref="WorkerJobSourceException.IsHandled" /> set so Core does not retry again.
/// </summary>
internal interface IPulsarRetryWrapperService
{
    /// <summary>
    ///     Executes <paramref name="func" /> with retry for non-critical transient Pulsar failures.
    /// </summary>
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes <paramref name="func" /> with retry for non-critical transient Pulsar failures.
    /// </summary>
    Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default);
}

/// <summary>
///     Polly v8-based retry wrapper for Pulsar client calls.
///     Retries when <see cref="IPulsarExceptionArbiterService" /> reports a non-critical transient failure,
///     using exponential backoff via <see cref="ISleepService" />.
/// </summary>
internal class PulsarRetryWrapperService(
    IPulsarExceptionArbiterService exceptionArbiterService,
    ISleepService sleepService)
    : IPulsarRetryWrapperService
{
    private const int PulsarRetryCount = 3;

    private ResiliencePipeline? _retryPipeline;

    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = PulsarRetryCount,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is not { } exception)
                    {
                        return PredicateResult.False();
                    }

                    if (args.Context.CancellationToken.IsCancellationRequested)
                    {
                        return PredicateResult.False();
                    }

                    var report = exceptionArbiterService.GetReport(exception);
                    return report is {IsCritical: false, CouldBeTransient: true}
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber)),
                        args.Context.CancellationToken);
                }
            })
            .Build();
    }

    private Exception WrapIfNeeded(Exception exception)
    {
        var report = exceptionArbiterService.GetReport(exception);

        if (report.AlreadyHandled)
        {
            return exception;
        }

        if (report.IsCritical)
        {
            /*
             * Critical / unrecognized. Throw raw exception.
             * We absolutely want to raise a massive alert and get a developer's attention
             *  so that the problem either becomes classified or the upstream cause is addressed.
             */
            return exception;
        }

        return new WorkerJobSourceException(exception, report.IsCritical, report.CouldBeTransient, true);
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetRetryPipeline().ExecuteAsync(
                async token => await func(token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw WrapIfNeeded(exception);
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
    {
        try
        {
            await GetRetryPipeline().ExecuteAsync(
                async token => await func(token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw WrapIfNeeded(exception);
        }
    }
}
