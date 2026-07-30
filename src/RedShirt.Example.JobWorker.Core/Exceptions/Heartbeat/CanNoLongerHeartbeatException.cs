namespace RedShirt.Example.JobWorker.Core.Exceptions;

/// <summary>
///     Indicates that a message has been in flight for so long that the visibility time cannot be extended further for
///     that job source implementation.
///     If this happens during otherwise expected behaviour, then it means either:
///     * You may want to reconsider your choice of job source implementation
///     * You may want to consider breaking up the job logic into different programs so that it doesn't risk hitting a
///     limit
/// </summary>
public class CanNoLongerHeartbeatException : Exception;