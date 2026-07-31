namespace RedShirt.Example.JobWorker.Common.Azure.Models;

internal class AzureExceptionArbiterReport
{
    public required bool IsCritical { get; init; }
    public required bool CouldBeTransient { get; init; }
}