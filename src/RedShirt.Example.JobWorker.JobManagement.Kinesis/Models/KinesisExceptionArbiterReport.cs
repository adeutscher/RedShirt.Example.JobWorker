namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

internal class KinesisExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool IsExpected { get; init; }
    public required bool CouldBeExternallySolvable { get; init; }
    public required bool CouldBeTransient { get; init; }
}