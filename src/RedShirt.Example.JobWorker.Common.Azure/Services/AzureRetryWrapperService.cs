using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.Services;

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
    ///     Thrown when <paramref name="func" /> ultimately fails. <see cref="WorkerAzureException.IsTransient" />
    ///     reflects the arbiter judgement for the final exception.
    /// </exception>
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);
}

/// <summary>
///     Polly v8-based retry wrapper for Azure SDK calls.
///     Retries when <see cref="IAzureExceptionArbiterService" /> reports an expected transient failure,
///     using exponential backoff via <see cref="ISleepService" />.
/// </summary>
/// <param name="exceptionArbiterService">Classifies Azure-related exceptions as expected/transient.</param>
/// <param name="sleepService">Provides cancellable backoff delays between retry attempts.</param>
internal class AzureRetryWrapperService(
    IAzureExceptionArbiterService exceptionArbiterService,
    ISleepService sleepService)
    : IAzureRetryWrapperService
{
    private const int AzureRetryCount = 3;

    /// <summary>
    ///     Lazily built Polly v8 <see cref="ResiliencePipeline" /> shared across invocations.
    /// </summary>
    private ResiliencePipeline? _retryPipeline;

    /// <summary>
    ///     Returns whether the exception should be retried based on if the arbiter
    ///     marks the exception as both expected and transient.
    /// </summary>
    private bool JudgeIfExceptionCanBeHandled(Exception exception)
    {
        var judgement = exceptionArbiterService.GetJudgement(exception);
        return judgement is {IsExpected: true, CouldBeTransient: true};
    }

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

                    return JudgeIfExceptionCanBeHandled(exception)
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
            var report = exceptionArbiterService.GetJudgement(exception);

            if (!report.IsExpected)
            {
                // Throw unexpected exception types upwards.
                // Intentionally creating as big of a problem as possible so that it gets developer attention 
                throw;
            }

            throw new WorkerAzureException(exception, report.CouldBeTransient);
        }
    }
}