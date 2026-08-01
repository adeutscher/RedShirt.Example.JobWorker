namespace RedShirt.Example.JobWorker.Core.Models;

/// <summary>
///     Temporary envelope for loading converted job models into IJobRepository
/// </summary>
public interface IJobEnvelope
{
    /// <summary>
    ///     Converted job model.
    /// </summary>
    IJobModel JobModel { get; init; }

    /// <summary>
    ///     Raw job model. It is expected that the implementation of IJobSource could require information in custom fields on
    ///     the specific implementation of IRawJobDataModel
    /// </summary>
    IRawJobModel RawJobModel { get; init; }
}

public class JobEnvelope : IJobEnvelope
{
    public required IJobModel JobModel { get; init; }
    public required IRawJobModel RawJobModel { get; init; }
}