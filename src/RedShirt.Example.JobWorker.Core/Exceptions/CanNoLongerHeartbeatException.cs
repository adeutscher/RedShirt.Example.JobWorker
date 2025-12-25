namespace RedShirt.Example.JobWorker.Core.Exceptions;

/// <summary>
///     Indicates that a message has been in flight for so long that the visibility time cannot be extended further.
/// </summary>
public class CanNoLongerHeartbeatException : Exception;