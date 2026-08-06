using Microsoft.Extensions.Options;

namespace RedShirt.Example.JobWorker.Core.Services.Health;

public interface ICoreHealthStateReaderService
{
    bool IsHealthy();
}

public interface ICoreHealthStateUpdateService
{
    void NoteIncident();
}

/// <summary>
///     Tracks recent worker incidents for health/z-pages readiness decisions.
/// </summary>
public sealed class CoreHealthStateService(
    IOptions<CoreHealthStateService.ConfigurationModel> options)
    : ICoreHealthStateUpdateService, ICoreHealthStateReaderService
{
    private readonly Lock _gate = new();
    private DateTime? _lastIncident;

    /// <summary>
    ///     Check to see if the service should be considered healthy.
    ///     A service shall be considered healthy if health checking is not enabled or if the time since the last incident
    ///     has been greater than <see cref="ConfigurationModel.EffectiveRecentIncidentThreshold" />.
    /// </summary>
    public bool IsHealthy()
    {
        if (!options.Value.Enabled)
        {
            return true;
        }

        DateTime? lastIncident;
        lock (_gate)
        {
            lastIncident = _lastIncident;
        }

        if (lastIncident is null)
        {
            return true;
        }

        return DateTime.UtcNow - lastIncident.Value > options.Value.EffectiveRecentIncidentThreshold;
    }

    public void NoteIncident()
    {
        lock (_gate)
        {
            _lastIncident = DateTime.UtcNow;
        }
    }

    public sealed class ConfigurationModel
    {
        public const int DefaultRecentIncidentThresholdSeconds = 60;

        /// <summary>
        ///     When <see langword="false" />, <see cref="IsHealthy" /> always returns <see langword="true" />.
        /// </summary>
        public bool Enabled { get; init; } = true;

        public required int? RecentIncidentThresholdSeconds { get; init; }

        public TimeSpan EffectiveRecentIncidentThreshold =>
            TimeSpan.FromSeconds(Math.Max(1,
                RecentIncidentThresholdSeconds ?? DefaultRecentIncidentThresholdSeconds));
    }
}