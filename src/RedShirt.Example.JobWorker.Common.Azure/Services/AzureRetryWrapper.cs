using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.Services;

public interface IAzureRetryWrapper
{
    Task RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);
}

public class AzureRetryWrapper(IAzureExceptionArbiterService exceptionArbiterService, ISleepService sleepService) : IAzureRetryWrapper
{
    private const int AzureRetryCount = 3;

    private bool JudgeIfExceptionCanBeHandled(Exception exception)
    {
        var judgement = exceptionArbiterService.GetJudgement(exception);
        return judgement is {IsExpected: true, IsTransient: true};
    }

    private AsyncRetryPolicy? _retryPolicy;
    
    public async Task RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default)
    {
        _retryPolicy ??= Policy.Handle<Exception>(JudgeIfExceptionCanBeHandled)
            .RetryAsync(AzureRetryCount,
                // Opting not to support a cancellation token here because 
                // ReSharper disable once MethodSupportsCancellation
                (_, instanceCount, ctx) => sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, instanceCount))));

        var policyResult = await _retryPolicy.ExecuteAndCaptureAsync(func, cancellationToken: cancellationToken);
        if (policyResult.Outcome == OutcomeType.Failure)
        {
            throw new AzureExceptionWrapper(policyResult.FinalException, exceptionArbiterService.GetJudgement(policyResult.FinalException).IsTransient);
        }
    }
}