using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;

/// <summary>
///     Retries SSM operations that fail with non-critical transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerSecretManagerException" /> with
///     <see cref="WorkerSecretManagerException.IsHandled" /> set.
/// </summary>
internal interface ISsmRetryWrapperService
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);
}

internal class SsmRetryWrapperService(
    ISsmExceptionArbiterService exceptionArbiterService,
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

                    var report = exceptionArbiterService.GetJudgement(exception);
                    return report is {IsCritical: false, CouldBeTransient: true}
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber)),
                        args.Context.CancellationToken);
                }
            })
            .Build();
    }

    private Exception WrapIfNeeded(Exception exception)
    {
        var report = exceptionArbiterService.GetJudgement(exception);

        if (report.AlreadyHandled)
        {
            return exception;
        }

        if (report.IsCritical)
        {
            return exception;
        }

        return new WorkerSecretManagerException(exception, false, report.CouldBeTransient, true);
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