using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Models;

internal sealed class SafeJobRunResults
{
    public required CoreJobResult Result { get; init; }
    public required Exception? Exception { get; init; }
}