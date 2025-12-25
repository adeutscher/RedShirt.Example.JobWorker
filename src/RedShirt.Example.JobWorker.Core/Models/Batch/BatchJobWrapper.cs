namespace RedShirt.Example.JobWorker.Core.Models.Batch;

internal class BatchJobWrapper : ISortableJobWrapper
{
    public required IJobModel JobModel { get; init; }
}