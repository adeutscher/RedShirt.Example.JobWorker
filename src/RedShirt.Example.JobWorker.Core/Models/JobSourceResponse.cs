namespace RedShirt.Example.JobWorker.Core.Models;

public sealed class JobSourceResponse
{
    public required List<IRawJobDataModel> Items { get; init; }
}