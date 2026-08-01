using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.SourceMessages;

/// <summary>
///     Sorts received messages internally to optimize processing per batch.
/// </summary>
internal interface ISourceMessageSorter
{
    List<T> GetSortedListOfJobs<T>(List<T> input) where T : ISortableJobWrapper;
}

internal sealed class SourceMessageSorter : ISourceMessageSorter
{
    public List<T> GetSortedListOfJobs<T>(List<T> input) where T : ISortableJobWrapper
    {
        /*
         * When working with multiple worker threads,
         * it's ideal wants to sort the larger jobs to process first
         *
         * That being said, sorting by the hours old a message is to avoid holding onto a low-priority job forever.
         */

        return input
            // Prioritize significantly older messages to prevent them from potentially being stuck in memory forever. 
            .OrderByDescending(i => i.JobModel.HoursOld)
            // Will need to replace or just outright remove this ThenByDescending when adapting the template
            .ThenByDescending(i => i.JobModel.Data.SleepDurationSeconds)
            .ToList();
    }
}