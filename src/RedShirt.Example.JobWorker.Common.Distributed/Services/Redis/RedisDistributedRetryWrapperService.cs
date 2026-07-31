using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;

/// <summary>
///     Retries Distributed client operations that fail with expected transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerDistributedException" />.
/// </summary>
internal interface IDistributedRetryWrapperService
{
    /// <summary>
    ///     Executes <paramref name="func" /> with retry for expected transient Distributed failures.
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
    /// <exception cref="WorkerDistributedException">
    ///     Thrown when <paramref name="func" /> ultimately fails. <see cref="WorkerDistributedException.IsTransient" />
    ///     reflects the arbiter judgement for the final exception.
    /// </exception>
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes <paramref name="func" /> with retry for expected transient Distributed failures.
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
    /// <exception cref="WorkerDistributedException">
    ///     Thrown when <paramref name="func" /> ultimately fails. <see cref="WorkerDistributedException.IsTransient" />
    ///     reflects the arbiter judgement for the final exception.
    /// </exception>
    Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default);
}

/// <summary>
///     Polly v8-based retry wrapper for Redis / distributed-cache calls.
///     Retries when <see cref="IRedisDistributedExceptionArbiterService" /> reports a possibly transient failure,
///     using exponential backoff via <see cref="IDistributedSleepService" />.
/// </summary>
/// <param name="exceptionArbiterService">Classifies Redis-related exceptions as possibly transient.</param>
/// <param name="sleepService">Provides cancellable backoff delays between retry attempts.</param>
internal class RedisDistributedRetryWrapperService(
    IRedisDistributedExceptionArbiterService exceptionArbiterService,
    IDistributedSleepService sleepService)
    : IDistributedRetryWrapperService
{
    private const int RedisRetryCount = 3;

    /// <summary>
    ///     Lazily built Polly v8 <see cref="ResiliencePipeline" /> shared across invocations.
    /// </summary>
    private ResiliencePipeline? _retryPipeline;

    /// <summary>
    ///     Creates (once) the retry pipeline: arbiter-driven <c>ShouldHandle</c>, zero Polly delay,
    ///     and exponential backoff performed in <c>OnRetry</c> through <see cref="IDistributedSleepService" />.
    /// </summary>
    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = RedisRetryCount,
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

                    return exceptionArbiterService.GetReport(exception).CouldBeTransient
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                // Do not use Polly-based delays between attempts
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    // Delay is performed via IDistributedSleepService in OnRetry so tests can mock sleeps.
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber)),
                        args.Context.CancellationToken);
                }
            })
            .Build();
    }

    private Exception WrapIfNeeded(Exception exception)
    {
        var report = exceptionArbiterService.GetReport(exception);

        if (report.IsCritical)
        {
            /*
             * Critical / unrecognized. Throw raw exception.
             * We absolutely want to raise a massive alert and get a developer's attention
             *  so that the problem either becomes classified or the upstream cause is addressed.
             */
            throw exception;
        }

        if (report.AlreadyHandled)
        {
            return exception;
        }

        return new WorkerDistributedException(exception, false, report.CouldBeTransient);
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