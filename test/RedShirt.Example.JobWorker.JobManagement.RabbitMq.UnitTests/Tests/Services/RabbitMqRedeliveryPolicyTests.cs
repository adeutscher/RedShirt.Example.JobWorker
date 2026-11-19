using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class RabbitMqRedeliveryPolicyTests
{
    private static RabbitMqRawJobModel CreateMessage(int? previousDeliveryCount)
    {
        return new RabbitMqRawJobModel
        {
            MessageId = "m",
            DeliveryTag = 1,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body",
            PreviousDeliveryCount = previousDeliveryCount
        };
    }

    [Theory]
    [InlineData(null, RabbitMqQueueConfigurationModel.DefaultMaximumDeliveries)]
    [InlineData(0, RabbitMqQueueConfigurationModel.DefaultMaximumDeliveries)]
    [InlineData(-3, RabbitMqQueueConfigurationModel.DefaultMaximumDeliveries)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    public void EffectiveMaximumDeliveries_DefaultsWhenUnsetOrNotPositive(int? configured, int expected)
    {
        Assert.Equal(expected,
            new RabbitMqQueueConfigurationModel {MaximumDeliveries = configured}.EffectiveMaximumDeliveries);
    }

    [Theory]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    [InlineData(CoreJobResult.InvalidData)]
    public void ShouldRequeue_NonRecoverable_Never(CoreJobResult result)
    {
        Assert.False(RabbitMqRedeliveryPolicyTestHelpers.Create().ShouldRequeue(CreateMessage(0), result));
    }

    [Theory]
    // Five deliveries allowed: requeue after deliveries 1 to 4, reject on delivery 5.
    [InlineData(0, 5, true)]
    [InlineData(3, 5, true)]
    [InlineData(4, 5, false)]
    [InlineData(19, 5, false)]
    // One delivery allowed: never requeue.
    [InlineData(0, 1, false)]
    public void ShouldRequeue_KnownDeliveryCount_RequeuesUntilLimit(int previousDeliveries, int maximumDeliveries,
        bool expected)
    {
        var policy = RabbitMqRedeliveryPolicyTestHelpers.Create(maximumDeliveries);

        Assert.Equal(expected, policy.ShouldRequeue(CreateMessage(previousDeliveries), CoreJobResult.Failure));
        Assert.Equal(expected, policy.ShouldRequeue(CreateMessage(previousDeliveries), CoreJobResult.Cancelled));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(100)]
    public void ShouldRequeue_UnknownDeliveryCount_DoesNotRequeueAgain(int? maximumDeliveries)
    {
        // A redelivered message on a classic queue: requeued at most once, whatever the limit.
        var policy = RabbitMqRedeliveryPolicyTestHelpers.Create(maximumDeliveries);

        Assert.False(policy.ShouldRequeue(CreateMessage(null), CoreJobResult.Failure));
    }
}
