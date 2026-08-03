namespace RedShirt.Example.JobWorker.Common.Aws.Models;

internal class AwsExceptionArbiterReport
{
    public required bool IsCritical { get; init; }
    public required bool CouldBeTransient { get; init; }
}