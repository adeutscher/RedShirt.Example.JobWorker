using RedShirt.Example.JobWorker.Core.Models;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;

internal sealed class RedisStreamJobModel : IJobModel
{
    internal required StreamEntry Message { get; init; }
    public required string MessageId { get; init; }
    public string? IdempotencyId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}
