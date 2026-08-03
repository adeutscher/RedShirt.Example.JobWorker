namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;

internal sealed class RedisStreamsExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool IsCritical { get; init; }
    public required bool CouldBeTransient { get; init; }
}