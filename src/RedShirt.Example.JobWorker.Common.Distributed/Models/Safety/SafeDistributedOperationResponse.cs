using RedShirt.Example.JobWorker.Common.Distributed.Enums;

namespace RedShirt.Example.JobWorker.Common.Distributed.Models.Safety;

/// <summary>
///     Represents an attempt to safely perform a distributed operation.
///     Used as a base class by operations that also return a payload.
/// </summary>
public class SafeDistributedOperationResponse
{
    public required SafeDistributedOperationResult Result { get; init; }
    public required DateTime NextAttemptTime { get; init; }
}