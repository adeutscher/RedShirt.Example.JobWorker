namespace RedShirt.Example.JobWorker.Common.Models;

/// <summary>
///     Contains job data.
/// </summary>
public interface IJobDataModel
{
    int SleepDurationSeconds { get; }
}