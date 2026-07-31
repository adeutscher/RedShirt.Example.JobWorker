using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Utility;

internal interface IRedisStreamBodyRetriever
{
    string GetMessageBody(NameValueEntry[] values);
    string? GetIdempotencyId(NameValueEntry[] values);
}

internal class RedisStreamBodyRetriever : IRedisStreamBodyRetriever
{
    private const string BodyFieldName = "body";
    private const string IdempotencyIdFieldName = "message_id";

    public string GetMessageBody(NameValueEntry[] values)
    {
        return GetRequiredValue(values, BodyFieldName);
    }

    public string? GetIdempotencyId(NameValueEntry[] values)
    {
        return TryGetValue(values, IdempotencyIdFieldName);
    }

    private static string GetRequiredValue(NameValueEntry[] values, string name)
    {
        return TryGetValue(values, name) ??
               throw new ArgumentException($"Redis stream entry missing required '{name}' field.");
    }

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
}
