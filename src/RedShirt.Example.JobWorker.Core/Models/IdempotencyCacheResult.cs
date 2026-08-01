namespace RedShirt.Example.JobWorker.Core.Models;

internal sealed class IdempotencyCacheResult
{
    public required bool JobSuccess { get; init; }
    public required SafeAcknowledgementResult AcknowledgementResult { get; init; }
}