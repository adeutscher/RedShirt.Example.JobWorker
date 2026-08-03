namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

internal class KinesisExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool IsCritical { get; init; }
    public required bool CouldBeTransient { get; init; }
}