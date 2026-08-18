using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Services.Utility;

namespace RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;

/// <summary>
///     Retries Azure client operations that fail with expected transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerAzureException" />.
/// </summary>
public interface IAzureRetryWrapperService
{
    /// <summary>
    ///     Executes <paramref name="func" /> with retry for expected transient Azure failures.
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
    /// <exception cref="WorkerAzureException">
    ///     Thrown when <paramref name="func" /> ultimately fails. <see cref="WorkerAzureException.CouldBeTransient" />
    ///     reflects the arbiter report for the final exception.
    /// </exception>
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);
}

/// <summary>
///     Polly v8-based retry wrapper for Azure SDK calls.
///     Retries when <see cref="IAzureExceptionArbiterService" /> reports an expected transient failure,
///     using exponential backoff via <see cref="ISleepService" />.
/// </summary>
/// <param name="exceptionArbiterService">Classifies Azure-related exceptions as expected/transient.</param>
/// <param name="logger">Logs retry attempts.</param>
/// <param name="sleepService">Provides cancellable backoff delays between retry attempts.</param>
internal sealed class AzureRetryWrapperService(
    IAzureExceptionArbiterService exceptionArbiterService,
    ILogger<AzureRetryWrapperService> logger,
    ISleepService sleepService)
    : IAzureRetryWrapperService
{
    private const int AzureRetryCount = 3;

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
                MaxRetryAttempts = AzureRetryCount,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is not { } exception)
                    {
                        return PredicateResult.False();
                    }

                    // Cancellation is honoured via ResilienceContext rather than a classic Polly Context bag.
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
                        "Retrying Azure operation after attempt {AttemptNumber}",
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
    ///     If wrapping was appropriate, then will be <see cref="WorkerAzureException" /> wrapped around the
    ///     <paramref name="exception" />.
    ///     If wrapping was not appropriate, then will be <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if the exception was wrapped, else <c>false</c></returns>
    private bool TryGetWrappedException(Exception exception, out Exception? wrappedException)
    {
        wrappedException = null;
        var report = exceptionArbiterService.GetReport(exception);

        if (!report.IsExpected)
        {
            /*
             * Unexpected / unrecognized.
             * Unexpected failures stay raw so they raise attention and get classified.
             */
            return false;
        }

        wrappedException = new WorkerAzureException(exception)
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
}