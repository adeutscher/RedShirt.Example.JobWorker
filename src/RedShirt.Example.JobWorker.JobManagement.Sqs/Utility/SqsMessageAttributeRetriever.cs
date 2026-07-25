using Amazon.SQS.Model;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Utility;

internal static class SqsMessageAttributeRetriever
{
    public static DateTime? TryGetApproximateFirstReceiveUtc(Message message)
    {
        if (message.Attributes is null
            || !message.Attributes.TryGetValue(SqsConstants.AttributeApproximateFirstReceiveTimestamp, out var raw)
            || !long.TryParse(raw, out var epochMs))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
    }

    public static int? TryGetApproximateReceiveCount(Message message)
    {
        if (message.Attributes is null
            || !message.Attributes.TryGetValue(SqsConstants.AttributeApproximateReceiveCount, out var raw)
            || !int.TryParse(raw, out var receiveCount))
        {
            return null;
        }

        return receiveCount;
    }
}