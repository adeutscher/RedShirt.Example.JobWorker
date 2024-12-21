using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Services;

/// <summary>
///     Sorts received messages internally to optimize processing per batch.
/// </summary>
public interface ISourceMessageSorter
{
    List<IJobModel> GetSortedListOfJobs(List<IJobModel> input);
}

internal class SourceMessageSorter : ISourceMessageSorter
{
    public List<IJobModel> GetSortedListOfJobs(List<IJobModel> input)
    {
        return input
            .OrderByDescending(i => i.Data.SleepDurationSeconds).ToList();
    }
}