namespace RedShirt.Example.JobWorker.Core.Models;

/// <summary>
///     Contains job data.
/// </summary>
public interface IJobDataModel
{
    int SleepDurationSeconds { get; }
}