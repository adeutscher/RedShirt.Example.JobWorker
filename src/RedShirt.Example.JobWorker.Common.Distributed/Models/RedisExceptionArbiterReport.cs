namespace RedShirt.Example.JobWorker.Common.Azure.Models;

public class RedisExceptionArbiterReport
{
    public required bool CouldBeTransient { get; init; }
}