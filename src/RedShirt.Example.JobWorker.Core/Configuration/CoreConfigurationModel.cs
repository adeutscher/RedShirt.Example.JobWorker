namespace RedShirt.Example.JobWorker.Core.Configuration;

internal class CoreConfigurationModel
{
    public required bool HaltOnFailure { get; init; }

    /// <summary>
    ///     When true, transient exceptions are escalated and treated as unexpected errors.
    /// </summary>
    public bool TreatTransientAsFailure { get; init; }
}