namespace RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;

/// <summary>
///     The Bar dependency reported that no record exists for the requested id (HTTP 404).
/// </summary>
public sealed class BarRecordNotFoundException(int id) : Exception($"Bar record {id} was not found.")
{
    public int Id => id;
}