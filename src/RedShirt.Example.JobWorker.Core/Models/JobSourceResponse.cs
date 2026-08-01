namespace RedShirt.Example.JobWorker.Core.Models;

public sealed class JobSourceResponse
{
    public required List<IRawJobModel> Items { get; init; }
}