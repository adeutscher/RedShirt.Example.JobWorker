namespace RedShirt.Example.JobWorker.Common.Aws.Models;

internal sealed class AwsExceptionArbiterReport
{
    public required bool IsExpected { get; init; }
    public required bool CouldBeTransient { get; init; }
    public required bool CouldBeExternallySolvable { get; init; }
}