namespace RedShirt.Example.JobWorker.Core.Models;

public class IdempotencyCacheResult
{
    public required bool JobSuccess { get; init; }
    public required SafeAcknowledgementResult AcknowledgementResult { get; init; }
}