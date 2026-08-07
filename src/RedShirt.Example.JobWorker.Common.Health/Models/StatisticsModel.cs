namespace RedShirt.Example.JobWorker.Common.Health.Models;

/// <summary>
///     Aggregate worker statistics suitable for health/z-pages reporting.
/// </summary>
public sealed class StatisticsModel
{
    public required JobStatisticsModel Lifetime { get; init; }

    /// <summary>
    ///     Job statistics aggregated over <see cref="RecentWindow" /> (sliding, bucketed).
    /// </summary>
    public required JobStatisticsModel Recent { get; init; }

    /// <summary>
    ///     Wall-clock window used when computing <see cref="Recent" />.
    /// </summary>
    public required TimeSpan RecentWindow { get; init; }

    public required TimeSpan Uptime { get; init; }
}

/// <summary>
///     Job outcome counts and successful-run timings for a reporting scope.
/// </summary>
public sealed class JobStatisticsModel
{
    public required SuccessfulTimingsModel SuccessfulTimings { get; init; }

    public required LifetimeTotalsModel Totals { get; init; }
}

/// <summary>
///     Duration statistics for jobs that completed successfully.
/// </summary>
public sealed class SuccessfulTimingsModel
{
    public required TimeSpan Average { get; init; }

    public required TimeSpan Max { get; init; }

    public required TimeSpan Min { get; init; }
}

/// <summary>
///     Counts of jobs by outcome for a reporting scope (lifetime or recent window).
/// </summary>
public sealed class LifetimeTotalsModel
{
    public required long Received { get; init; }

    public required long Successful { get; init; }

    public required long Cancelled { get; init; }

    public required long Failed { get; init; }

    public required long InvalidData { get; init; }
}
