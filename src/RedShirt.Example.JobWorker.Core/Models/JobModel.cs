using RedShirt.Example.JobWorker.Common.Models;

namespace RedShirt.Example.JobWorker.Core.Models;

public sealed class JobModel : IJobModel
{
    public required string MessageId { get; init; }
    public required string? IdempotencyId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}
