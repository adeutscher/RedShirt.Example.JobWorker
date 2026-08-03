namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Models;

internal class SqsJobSourceExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool IsCritical { get; init; }
    public required bool CouldBeTransient { get; init; }
}