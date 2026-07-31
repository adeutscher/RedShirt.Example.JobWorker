namespace RedShirt.Example.JobWorker.Common.Azure.Models;

public class AzureExceptionArbiterReport
{
    public required bool IsExpected { get; init; }
    public required bool IsTransient { get; init; }
}