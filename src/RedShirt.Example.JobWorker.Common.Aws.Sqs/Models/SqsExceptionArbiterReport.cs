namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.Models;

internal class SqsExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool IsCritical { get; init; }
    public required bool CouldBeTransient { get; init; }
}