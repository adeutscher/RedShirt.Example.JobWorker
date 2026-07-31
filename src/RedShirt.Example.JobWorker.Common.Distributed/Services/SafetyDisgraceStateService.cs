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
    bool IsInDisgracePeriod();
}

internal class SafetyDisgraceStateService(IOptions<SafetyDisgraceStateService.ConfigurationModel> options)
    : ISafetyDisgraceStateService
{
    private readonly Lock _disgraceLock = new();
    private DateTimeOffset? _disgraceUntil;

    public bool IsInDisgracePeriod()
    {
        lock (_disgraceLock)
        {
            return _disgraceUntil is { } until && DateTimeOffset.UtcNow < until;
        }
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