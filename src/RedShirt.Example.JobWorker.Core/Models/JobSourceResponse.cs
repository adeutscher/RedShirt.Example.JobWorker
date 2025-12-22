namespace RedShirt.Example.JobWorker.Core.Models;

public sealed class JobSourceResponse
{
    public required List<IJobModel> Items { get; init; }
}