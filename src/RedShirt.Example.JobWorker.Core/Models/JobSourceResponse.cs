namespace RedShirt.Example.JobWorker.Core.Models;

public interface IJobSourceResponse
{
    List<IRawJobModel> Items { get; }
}

public sealed class JobSourceResponse : IJobSourceResponse
{
    public required List<IRawJobModel> Items { get; init; }
}