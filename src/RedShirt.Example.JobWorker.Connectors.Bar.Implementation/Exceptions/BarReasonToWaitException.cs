using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;

/// <summary>
///     The Bar dependency indicated that the caller should wait before retrying.
///     The JobWorker Bar connector respects these exceptions indefinitely; see <c>docs/bar-connector.md</c>.
/// </summary>
internal abstract class BarReasonToWaitException : BarException
{
    /// <summary>
    ///     Optional delay suggested by the dependency (for example from an HTTP <c>Retry-After</c> header).
    ///     When null, the connector uses its configured fallback wait duration.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    protected BarReasonToWaitException(string message) : base(message)
    {
    }
}
