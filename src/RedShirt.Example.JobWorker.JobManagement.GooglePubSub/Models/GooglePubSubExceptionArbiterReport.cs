namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

internal class GooglePubSubExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool CouldBeTransient { get; init; }
    public required bool IsCritical { get; init; }
}
