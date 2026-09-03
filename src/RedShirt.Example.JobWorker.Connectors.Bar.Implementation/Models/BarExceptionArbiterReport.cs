namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Models;

internal sealed class BarExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }

    public required bool IsExpected { get; init; }

    public required bool CouldBeTransient { get; init; }

    public required bool CouldBeExternallySolvable { get; init; }
}