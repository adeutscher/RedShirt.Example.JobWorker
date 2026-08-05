namespace RedShirt.Example.JobWorker.Common.Distributed.Models;

internal sealed class RedisExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool CouldBeTransient { get; init; }
    public required bool IsExpected { get; init; }
    public required bool CouldBeExternallySolvable { get; init; }
}