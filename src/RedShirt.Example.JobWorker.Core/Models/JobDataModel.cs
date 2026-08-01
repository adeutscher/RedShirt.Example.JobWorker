namespace RedShirt.Example.JobWorker.Core.Models;

/// <summary>
///     Contains job data.
/// </summary>
public interface IJobDataModel
{
    int SleepDurationSeconds { get; }
}

internal sealed class JobDataModel : IJobDataModel
{
    public required int SleepDurationSeconds { get; init; }
}