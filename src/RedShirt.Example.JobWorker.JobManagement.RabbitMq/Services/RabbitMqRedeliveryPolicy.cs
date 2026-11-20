using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

/// <summary>
///     Decides whether a failed message goes back on the queue, so that a poison message cannot loop forever.
/// </summary>
internal interface IRabbitMqRedeliveryPolicy
{
    bool ShouldRequeue(RabbitMqRawJobModel message, CoreJobResult result);
}

internal class RabbitMqRedeliveryPolicy(
    IOptions<RabbitMqQueueConfigurationModel> options,
    ILogger<RabbitMqRedeliveryPolicy> logger) : IRabbitMqRedeliveryPolicy
{
    public bool ShouldRequeue(RabbitMqRawJobModel message, CoreJobResult result)
    {
        if (!result.IsRecoverableFailure())
        {
            // Empty / Parsing / InvalidData: never requeue.
            return false;
        }

        var maximumDeliveries = options.Value.EffectiveMaximumDeliveries;

        if (message.PreviousDeliveryCount is { } previousDeliveries)
        {
            // Quorum queue, or a first delivery: the count is known.
            if (previousDeliveries + 1 < maximumDeliveries)
            {
                return true;
            }

            logger.LogWarning(
                "RabbitMQ message {MessageId} failed on delivery {DeliveryAttempt} of {MaximumDeliveries}. Rejecting without requeue.",
                message.MessageId, previousDeliveries + 1, maximumDeliveries);
            return false;
        }

        /*
         * Redelivered without a delivery count: a classic queue, which does not track how many times a message was
         * delivered. The message has failed at least twice now, so it is not requeued again.
         * Use a quorum queue to allow more attempts.
         */
        logger.LogWarning(
            "RabbitMQ message {MessageId} failed again after a redelivery, and the queue does not track a delivery count. Rejecting without requeue.",
            message.MessageId);
        return false;
    }
}