using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.Services;

public interface IAzureRetryWrapper
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);
}

public class AzureRetryWrapper(IAzureExceptionArbiterService exceptionArbiterService, ISleepService sleepService)
    : IAzureRetryWrapper
{
    private const int AzureRetryCount = 3;

    private ResiliencePipeline? _retryPipeline;

    private bool JudgeIfExceptionCanBeHandled(Exception exception, ResilienceContext context)
    {
        // Cancellation is honoured via ResilienceContext rather than a classic Polly Context bag.
        if (context.CancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var judgement = exceptionArbiterService.GetJudgement(exception);
        return judgement is {IsExpected: true, IsTransient: true};
    }

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

                    return JudgeIfExceptionCanBeHandled(exception, args.Context)
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

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetRetryPipeline().ExecuteAsync(
                async token => await func(token),
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw new AzureExceptionWrapper(exception,
                exceptionArbiterService.GetJudgement(exception).IsTransient);
        }
    }
}