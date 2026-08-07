using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.Services;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;

/// <summary>
///     Retries SSM operations that fail with expected transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerSecretManagerException" /> with
///     <see cref="WorkerSecretManagerException.IsHandled" /> set.
/// </summary>
internal interface ISsmRetryWrapperService
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);
}

internal sealed class SsmRetryWrapperService(
    ISsmExceptionArbiterService exceptionArbiterService,
    ILogger<SsmRetryWrapperService> logger,
    ISleepService sleepService)
    : ISsmRetryWrapperService
{
    private const int SsmRetryCount = 3;

    private ResiliencePipeline? _retryPipeline;

    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = SsmRetryCount,
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
                        "Retrying SSM operation after attempt {AttemptNumber}",
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
        if (report.AlreadyHandled && exception is WorkerSecretManagerException)
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

        return new WorkerSecretManagerException(exception)
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
}