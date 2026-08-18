namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;

internal class RabbitMqExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool CouldBeTransient { get; init; }
    public required bool IsExpected { get; init; }
    public required bool CouldBeExternallySolvable { get; init; }
}
