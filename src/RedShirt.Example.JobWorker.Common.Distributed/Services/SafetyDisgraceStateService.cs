using Microsoft.Extensions.Options;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

/// <summary>
///     Common tracker between safe cache/lock implementations that tracks when the last time the cache failed.
///     As a performance measure, if the implementations using this service are invoked while in the disgrace period, then
///     they shall output a stand-in.
/// </summary>
internal interface ISafetyDisgraceStateService
{
    void EnterDisgracePeriod();

    /// <summary>
    ///     When in a disgrace period, the UTC time after which attempts may resume; otherwise the current UTC time.
    /// </summary>
    DateTime GetNextAttemptTime();

    /// <summary>
    ///     Returns whether operations are currently in a disgrace period.
    ///     When <c>true</c>, <paramref name="nextAttemptTime" /> is the UTC time after which attempts may resume;
    ///     otherwise it is the current UTC time.
    /// </summary>
    bool IsInDisgracePeriod(out DateTime nextAttemptTime);
}

internal sealed class SafetyDisgraceStateService(IOptions<SafetyDisgraceStateService.ConfigurationModel> options)
    : ISafetyDisgraceStateService
{
    private readonly Lock _disgraceLock = new();
    private DateTimeOffset? _disgraceUntil;

    public bool IsInDisgracePeriod(out DateTime nextAttemptTime)
    {
        lock (_disgraceLock)
        {
            if (_disgraceUntil is { } until && DateTimeOffset.UtcNow < until)
            {
                nextAttemptTime = until.UtcDateTime;
                return true;
            }

            // Ready to go now
            nextAttemptTime = DateTime.UtcNow;
            // Not in disgrace period
            return false;
        }
    }

    public DateTime GetNextAttemptTime()
    {
        IsInDisgracePeriod(out var nextAttemptTime);
        return nextAttemptTime;
    }

    public void EnterDisgracePeriod()
    {
        lock (_disgraceLock)
        {
            _disgraceUntil = DateTimeOffset.UtcNow.AddSeconds(options.Value.DisgracePeriodSeconds);
        }
    }

    public sealed class ConfigurationModel
    {
        public required int DisgracePeriodSeconds { get; init; }
    }
}