using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

internal static class PubSubMessageAttributeRetriever
{
    /// <summary>
    ///     Returns the Pub/Sub delivery attempt when it is populated (typically only when a dead-letter policy
    ///     is configured on the subscription). Returns <c>null</c> when the field is unset / zero.
    /// </summary>
    public static int? TryGetDeliveryAttempt(IPubSubMessageContainer message)
    {
        var deliveryAttempt = message.Message?.DeliveryAttempt ?? 0;
        return deliveryAttempt > 0 ? deliveryAttempt : null;
    }
}
