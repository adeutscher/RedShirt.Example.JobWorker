namespace RedShirt.Example.JobWorker.Common.Distributed.Models;

internal class RedisExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool CouldBeTransient { get; init; }
    public required bool IsCritical { get; init; }
}