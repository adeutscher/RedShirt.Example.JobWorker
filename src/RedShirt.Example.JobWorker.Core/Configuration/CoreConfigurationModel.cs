namespace RedShirt.Example.JobWorker.Core.Configuration;

internal class CoreConfigurationModel
{
    public required bool HaltOnFailure { get; init; }

    /// <summary>
    ///     When true, transient exceptions are escalated and treated as unexpected errors.
    ///     Largely intended for debugging some cases without having to temporarily break exception handling in code.
    /// </summary>
    public bool TreatTransientExceptionAsFailure { get; init; }
}