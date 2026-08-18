using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Services.Utility;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;

/// <summary>
///     Retries SQS operations that fail with expected transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerSqsException" /> with
///     <see cref="WorkerSqsException.IsHandled" /> set.
/// </summary>
public interface ISqsRetryWrapperService
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);

    Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default);
}

internal sealed class SqsRetryWrapperService(
    ISqsExceptionArbiterService exceptionArbiterService,
    ILogger<SqsRetryWrapperService> logger,
    ISleepService sleepService)
    : ISqsRetryWrapperService
{
    private const int SqsRetryCount = 3;

    private ResiliencePipeline? _retryPipeline;

    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = SqsRetryCount,
                ShouldHandle = args =>
                {
                    // ReSharper disable once DuplicatedSequentialIfBodies
                    if (args.Outcome.Exception is not { } exception)
                    {
                        return PredicateResult.False();
                    }

                    if (args.Context.CancellationToken.IsCancellationRequested)
                    {
                        return PredicateResult.False();
                    }

                    var report = exceptionArbiterService.GetReport(exception);
                    return report is {IsExpected: true, CouldBeTransient: true}
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Retrying SQS operation after attempt {AttemptNumber}",
                        args.AttemptNumber);
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
    ///     If wrapping was appropriate, then will be <see cref="WorkerSqsException" />
    ///     wrapped around
    ///     <param name="exception"></param>
    /// </param>
    /// <returns><c>true</c> if the exception was wrapped, else <c>false</c></returns>
    private bool TryGetWrappedException(Exception exception, out Exception? wrappedException)
    {
        wrappedException = null;
        var report = exceptionArbiterService.GetReport(exception);

        // ReSharper disable once DuplicatedSequentialIfBodies
        if (report.AlreadyHandled && exception is WorkerSqsException)
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

        wrappedException = new WorkerSqsException(exception)
        {
            CouldBeTransient = report.CouldBeTransient,
            IsHandled = true,
            CouldBeExternallySolvable = report.CouldBeExternallySolvable
        };
        return true;
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
            if (TryGetWrappedException(exception, out var wrappedException) && wrappedException is not null)
            {
                throw wrappedException;
            }

            // Do a flat throw to preserve stack trace
            throw;
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
            if (TryGetWrappedException(exception, out var wrappedException) && wrappedException is not null)
            {
                throw wrappedException;
            }

            // Do a flat throw to preserve stack trace
            throw;
        }
    }
}