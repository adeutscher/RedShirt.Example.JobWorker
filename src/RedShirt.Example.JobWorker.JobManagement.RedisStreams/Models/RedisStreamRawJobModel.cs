using RedShirt.Example.JobWorker.Core.Models;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;

internal class RedisStreamRawJobModel : IRawJobModel
{
    private const string BodyFieldName = "body";
    private const string IdempotencyIdFieldName = "message_id";

    private static string? TryGetValue(NameValueEntry[] values, string name)
    {
        foreach (var item in values)
        {
            if (item.Name == name)
            {
                return item.Value.ToString();
            }
        }

        return null;
    }

    internal required StreamEntry Message { get; init; }
    public required string MessageId { get; init; }

    public string? IdempotencyId => TryGetValue(Message.Values, IdempotencyIdFieldName);

    public string? Body =>
        TryGetValue(Message.Values, BodyFieldName) ??
        throw new ArgumentException($"Redis stream entry missing required '{BodyFieldName}' field.");

    public required DateTime CreatedAtUtc { get; init; }
}