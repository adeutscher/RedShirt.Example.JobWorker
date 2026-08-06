using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Common.Enums;

namespace RedShirt.Example.JobWorker.Core.Models;

internal sealed class IdempotencyCacheResult
{
    public required CoreJobResult JobResult { get; init; }
    public required SafeAcknowledgementResult AcknowledgementResult { get; init; }
}