using RabbitMQ.Client;
using System.Globalization;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Utility;

internal static class RabbitMqDeliveryCountReader
{
    /// <summary>
    ///     Header that quorum queues set on redelivered messages, counting earlier unsuccessful deliveries.
    ///     Classic queues do not set it.
    /// </summary>
    internal const string DeliveryCountHeader = "x-delivery-count";

    private static bool TryConvert(object? value, out int count)
    {
        count = 0;
        if (value is not IConvertible convertible || value is string)
        {
            return false;
        }

        try
        {
            var raw = convertible.ToInt64(CultureInfo.InvariantCulture);
            if (raw < 0)
            {
                return false;
            }

            count = (int) Math.Min(raw, int.MaxValue);
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Number of earlier unsuccessful deliveries, or null when the queue does not track it.
    ///     A missing header on a message that is not redelivered means no earlier deliveries.
    /// </summary>
    public static int? TryGetPreviousDeliveryCount(IReadOnlyBasicProperties? properties, bool redelivered)
    {
        if (properties?.Headers is not null
            && properties.Headers.TryGetValue(DeliveryCountHeader, out var value)
            && TryConvert(value, out var count))
        {
            return count;
        }

        return redelivered ? null : 0;
    }
}