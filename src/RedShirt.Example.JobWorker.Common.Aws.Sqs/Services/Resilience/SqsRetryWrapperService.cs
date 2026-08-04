using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Core.Services.Utility;

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

internal class SqsRetryWrapperService(
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

    private Exception WrapIfNeeded(Exception exception)
    {
        var report = exceptionArbiterService.GetReport(exception);

        // ReSharper disable once DuplicatedSequentialIfBodies
        if (report.AlreadyHandled && exception is WorkerSqsException)
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

        return new WorkerSqsException(exception)
        {
            CouldBeTransient = report.CouldBeTransient,
            IsHandled = true,
            CouldBeExternallySolvable = report.CouldBeExternallySolvable
        };
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