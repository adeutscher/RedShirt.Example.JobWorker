using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

/// <summary>
///     Retries Google Pub/Sub client operations that fail with non-critical transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerJobSourceException" /> with
///     <see cref="WorkerJobSourceException.IsHandled" /> set so Core does not retry again.
/// </summary>
internal interface IGooglePubSubRetryWrapperService
{
    /// <summary>
    ///     Executes <paramref name="func" /> with retry for non-critical transient Pub/Sub failures.
    /// </summary>
    /// <typeparam name="T">The result type produced by <paramref name="func" />.</typeparam>
    /// <param name="func">
    ///     The operation to execute. Receives the same <see cref="CancellationToken" /> used for retries and backoff delays.
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation, retry attempts, and backoff delays.
    /// </param>
    /// <returns>The successful result of <paramref name="func" />.</returns>
    /// <exception cref="OperationCanceledException">
    ///     Propagated when <paramref name="cancellationToken" /> is cancelled.
    /// </exception>
    /// <exception cref="WorkerJobSourceException">
    ///     Thrown when <paramref name="func" /> ultimately fails. <see cref="WorkerJobSourceException.CouldBeTransient" />
    ///     reflects the arbiter judgement for the final exception;
    ///     <see cref="WorkerJobSourceException.IsHandled" /> is <c>true</c>.
    /// </exception>
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes <paramref name="func" /> with retry for non-critical transient Pub/Sub failures.
    /// </summary>
    /// <param name="func">
    ///     The operation to execute. Receives the same <see cref="CancellationToken" /> used for retries and backoff delays.
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation, retry attempts, and backoff delays.
    /// </param>
    /// <exception cref="OperationCanceledException">
    ///     Propagated when <paramref name="cancellationToken" /> is cancelled.
    /// </exception>
    /// <exception cref="WorkerJobSourceException">
    ///     Thrown when <paramref name="func" /> ultimately fails. <see cref="WorkerJobSourceException.CouldBeTransient" />
    ///     reflects the arbiter judgement for the final exception;
    ///     <see cref="WorkerJobSourceException.IsHandled" /> is <c>true</c>.
    /// </exception>
    Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default);
}

/// <summary>
///     Polly v8-based retry wrapper for Google Pub/Sub client calls.
///     Retries when <see cref="IGooglePubSubExceptionArbiterService" /> reports a non-critical transient failure,
///     using exponential backoff via <see cref="ISleepService" />.
/// </summary>
/// <param name="exceptionArbiterService">Classifies Pub/Sub-related exceptions as critical/transient.</param>
/// <param name="sleepService">Provides cancellable backoff delays between retry attempts.</param>
internal class GooglePubSubRetryWrapperService(
    IGooglePubSubExceptionArbiterService exceptionArbiterService,
    ISleepService sleepService)
    : IGooglePubSubRetryWrapperService
{
    private const int GooglePubSubRetryCount = 3;

    /// <summary>
    ///     Lazily built Polly v8 <see cref="ResiliencePipeline" /> shared across invocations.
    /// </summary>
    private ResiliencePipeline? _retryPipeline;

    /// <summary>
    ///     Creates (once) the retry pipeline: arbiter-driven <c>ShouldHandle</c>, zero Polly delay,
    ///     and exponential backoff performed in <c>OnRetry</c> through <see cref="ISleepService" />.
    /// </summary>
    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = GooglePubSubRetryCount,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is not { } exception)
                    {
                        return PredicateResult.False();
                    }

                    // Cancellation is honoured via ResilienceContext.
                    if (args.Context.CancellationToken.IsCancellationRequested)
                    {
                        return PredicateResult.False();
                    }

                    var report = exceptionArbiterService.GetReport(exception);
                    return report is {IsCritical: false, CouldBeTransient: true}
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                // Do not use Polly-based delays between attempts
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    // Delay is performed via ISleepService in OnRetry so tests can mock sleeps.
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
