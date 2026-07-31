namespace RedShirt.Example.JobWorker.Common.Distributed.Models;

public class RedisExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool CouldBeTransient { get; init; }
}
