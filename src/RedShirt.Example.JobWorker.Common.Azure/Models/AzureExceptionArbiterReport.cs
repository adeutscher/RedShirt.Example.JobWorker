namespace RedShirt.Example.JobWorker.Common.Azure.Models;

internal sealed class AzureExceptionArbiterReport
{
    public required bool IsExpected { get; init; }
    public required bool CouldBeTransient { get; init; }
    public required bool CouldBeExternallySolvable { get; init; }
}