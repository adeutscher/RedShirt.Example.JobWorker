using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Services;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services.Resilience;

/// <summary>
///     Retries SQS job-source operations, then surfaces failures as
///     <see cref="WorkerJobSourceException" /> with <see cref="WorkerJobSourceException.IsHandled" /> set.
/// </summary>
internal interface ISqsJobSourceRetryWrapperService
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);

    Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default);
}

internal class SqsJobSourceRetryWrapperService(
    ISqsJobSourceExceptionArbiterService exceptionArbiterService,
    ILogger<SqsJobSourceRetryWrapperService> logger,
    ISleepService sleepService)
    : ISqsJobSourceRetryWrapperService
{
    private const int SqsJobSourceRetryCount = 3;

    private ResiliencePipeline? _retryPipeline;

    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = SqsJobSourceRetryCount,
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
                    return report is {IsExpected: true, CouldBeTransient: true}
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Retrying SQS job source operation after attempt {AttemptNumber}",
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