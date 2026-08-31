using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

/// <summary>
///     Retries NATS / JetStream client operations that fail with expected transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerJobSourceException" /> with
///     <see cref="WorkerJobSourceException.IsHandled" /> set so Core does not retry again.
/// </summary>
internal interface INatsRetryWrapperService
{
    /// <summary>
    ///     Executes <paramref name="func" /> with retry for expected transient NATS failures.
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
    ///     Executes <paramref name="func" /> with retry for expected transient NATS failures.
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

    Task RunAsync<TState>(Func<TState, CancellationToken, Task> func, TState state,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Polly v8-based retry wrapper for NATS / JetStream client calls.
///     Retries when <see cref="INatsExceptionArbiterService" /> reports an expected transient failure,
///     using exponential backoff via <see cref="ISleepService" />.
/// </summary>
/// <param name="exceptionArbiterService">Classifies NATS-related exceptions as expected/transient.</param>
/// <param name="logger">Logs retry attempts.</param>
/// <param name="sleepService">Provides cancellable backoff delays between retry attempts.</param>
internal class NatsRetryWrapperService(
    INatsExceptionArbiterService exceptionArbiterService,
    ILogger<NatsRetryWrapperService> logger,
    ISleepService sleepService)
    : INatsRetryWrapperService
{
    private const int NatsRetryCount = 3;

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
                MaxRetryAttempts = NatsRetryCount,
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
                    return report is {IsExpected: true, CouldBeTransient: true}
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                // Do not use Polly-based delays between attempts
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Retrying NATS operation after attempt {AttemptNumber}",
                        args.AttemptNumber);
                    // Delay is performed via ISleepService in OnRetry so tests can mock sleeps.
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber)),
                        args.Context.CancellationToken);
                }
            })
            .Build();
    }

    /// <summary>
    ///     Try to get the wrapped exception.
    /// </summary>
    /// <param name="exception">Exception to be judged.</param>
    /// <param name="wrappedException">
    ///     If wrapping was appropriate, then will be <see cref="WorkerJobSourceException" /> wrapped around the
    ///     <paramref name="exception" />.
    ///     If wrapping was not appropriate, then will be <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if the exception was wrapped, else <c>false</c></returns>
    private bool TryGetWrappedException(Exception exception, out Exception? wrappedException)
    {
        wrappedException = null;
        var report = exceptionArbiterService.GetReport(exception);

        // ReSharper disable once DuplicatedSequentialIfBodies
        if (report.AlreadyHandled && exception is WorkerJobSourceException)
        {
            return false;
        }

        if (!report.IsExpected)
        {
            /*
             * Unexpected / unrecognized.
             * Unexpected failures stay raw so they raise attention and get classified.
             */
            return false;
        }

        wrappedException = new WorkerJobSourceException(exception)
        {
            CouldBeTransient = report.CouldBeTransient,
            IsHandled = true,
            CouldBeExternallySolvable = report.CouldBeExternallySolvable
        };
        return true;
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
            if (TryGetWrappedException(exception, out var wrappedException) && wrappedException is not null)
            {
                throw wrappedException;
            }

            // Do a flat throw to preserve stack trace
            throw;
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
            if (TryGetWrappedException(exception, out var wrappedException) && wrappedException is not null)
            {
                throw wrappedException;
            }

            // Do a flat throw to preserve stack trace
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RunAsync<TState>(Func<TState, CancellationToken, Task> func, TState state,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await GetRetryPipeline().ExecuteAsync(
                async token => await func(state, token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (TryGetWrappedException(exception, out var wrappedException) && wrappedException is not null)
            {
                throw wrappedException;
            }

            throw;
        }
    }
}