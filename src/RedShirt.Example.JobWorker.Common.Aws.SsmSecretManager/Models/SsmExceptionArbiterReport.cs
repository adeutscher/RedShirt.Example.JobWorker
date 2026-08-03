namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Models;

internal class SsmExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool IsCritical { get; init; }
    public required bool CouldBeTransient { get; init; }
}