using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services.Resilience;

/// <summary>
///     Retries Kafka client operations that fail with expected transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerJobSourceException" /> with
///     <see cref="WorkerJobSourceException.IsHandled" /> set so Core does not retry again.
/// </summary>
internal interface IKafkaRetryWrapperService
{
    /// <summary>
    ///     Executes <paramref name="func" /> with retry for expected transient Kafka failures.
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
    ///     Executes <paramref name="func" /> with retry for expected transient Kafka failures.
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
///     Polly v8-based retry wrapper for Kafka client calls.
///     Retries when <see cref="IKafkaExceptionArbiterService" /> reports an expected transient failure,
///     using exponential backoff via <see cref="ISleepService" />.
/// </summary>
/// <param name="exceptionArbiterService">Classifies Kafka-related exceptions as expected/transient.</param>
/// <param name="logger">Logs retry attempts.</param>
/// <param name="sleepService">Provides cancellable backoff delays between retry attempts.</param>
internal class KafkaRetryWrapperService(
    IKafkaExceptionArbiterService exceptionArbiterService,
    ILogger<KafkaRetryWrapperService> logger,
    ISleepService sleepService)
    : IKafkaRetryWrapperService
{
    private const int KafkaRetryCount = 3;

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
                MaxRetryAttempts = KafkaRetryCount,
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
                        "Retrying Kafka operation after attempt {AttemptNumber}",
                        args.AttemptNumber);
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

        // ReSharper disable once DuplicatedSequentialIfBodies
        if (report.AlreadyHandled && exception is WorkerJobSourceException)
        {
            return exception;
        }

        if (!report.IsExpected)
        {
            /*
             * Unexpected / unrecognized.
             * Unexpected failures stay raw so they raise attention and get classified.
             */
            return exception;
        }

        return new WorkerJobSourceException(exception)
        {
            CouldBeTransient = report.CouldBeTransient,
            IsHandled = true,
            CouldBeExternallySolvable = report.CouldBeExternallySolvable
        };
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