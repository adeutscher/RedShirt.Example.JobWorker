namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

internal sealed class ActiveMqExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool IsExpected { get; init; }
    public required bool CouldBeExternallySolvable { get; init; }
    public required bool CouldBeTransient { get; init; }
}