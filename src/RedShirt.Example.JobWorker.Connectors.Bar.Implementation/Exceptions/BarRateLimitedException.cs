namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;

/// <summary>
///     Suggests a rate limit response from the underlying Bar service (typically HTTP 429).
/// </summary>
internal sealed class BarRateLimitedException : BarReasonToWaitException
{
    public BarRateLimitedException(TimeSpan? retryAfter)
        : base("Bar API rate limit exceeded.")
    {
        RetryAfter = retryAfter;
        IsHandled = false;
        CouldBeTransient = true;
        CouldBeExternallySolvable = true;
    }
}
