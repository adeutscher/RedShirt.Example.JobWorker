namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

internal class AzureServiceBusExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool IsExpected { get; init; }
    public required bool CouldBeExternallySolvable { get; init; }
    public required bool CouldBeTransient { get; init; }
}
