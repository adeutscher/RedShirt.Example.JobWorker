using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Common.Services;

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
        /*
         * The default template does not sort the list at all,
         * but this class was made to be a central, ideal place to do it.
         */
        
        // When working with multiple worker threads,
        // it's ideal wants to sort the larger jobs to process first  
        return input;
    }
}