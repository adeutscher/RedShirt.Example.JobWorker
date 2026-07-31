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
    private const string CancellationTokenKey = "ct";

    private AsyncRetryPolicy? _retryPolicy;

    private bool JudgeIfExceptionCanBeHandled(Exception exception)
    {
        var judgement = exceptionArbiterService.GetJudgement(exception);
        return judgement is { IsExpected: true, IsTransient: true };
    }

    private static CancellationToken GetCancellationToken(Context context)
    {
        if (context.TryGetValue(CancellationTokenKey, out var value) && value is CancellationToken cancellationToken)
        {
            return cancellationToken;
        }

        return CancellationToken.None;
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func,
        CancellationToken cancellationToken = default)
    {
        _retryPolicy ??= Policy.Handle<Exception>(JudgeIfExceptionCanBeHandled)
            .RetryAsync(AzureRetryCount,
                (_, instanceCount, context) => sleepService.DelayAsync(
                    TimeSpan.FromSeconds(Math.Pow(2, instanceCount)),
                    GetCancellationToken(context)));

        var context = new Context
        {
            [CancellationTokenKey] = cancellationToken
        };

        var policyResult = await _retryPolicy.ExecuteAndCaptureAsync(
            (_, ct) => func(ct),
            context, cancellationToken);

        if (policyResult.Outcome == OutcomeType.Failure)
        {
            throw new AzureExceptionWrapper(policyResult.FinalException,
                exceptionArbiterService.GetJudgement(policyResult.FinalException).IsTransient);
        }

        return policyResult.Result;
    }
}
