using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Models;

public class AzureJobModelTests
{
    [Fact]
    public void IdempotencyId_MatchesMessageId()
    {
        const string messageId = "service-bus-message-456";
        var receivedMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("body"),
            messageId);
        var message = new Mock<IServiceBusMessageContainer>();
        message.SetupGet(m => m.Message).Returns(receivedMessage);

        var job = new AzureJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        Assert.Equal(job.MessageId, job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIRawJobModel()
    {
        var job = new AzureJobModel
        {
            Message = new Mock<IServiceBusMessageContainer>().Object,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        Assert.IsAssignableFrom<IRawJobModel>(job);
    }

    [Fact]
    public void MessageId_DelegatesToServiceBusMessage()
    {
        const string messageId = "service-bus-message-123";
        var receivedMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("body"),
            messageId);
        var message = new Mock<IServiceBusMessageContainer>();
        message.SetupGet(m => m.Message).Returns(receivedMessage);

        var job = new AzureJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        Assert.Equal(messageId, job.MessageId);
        Assert.Equal(messageId, job.IdempotencyId);
    }

    [Fact]
    public void MessageId_ReturnsUnknownWhenInnerMessageIsNull()
    {
        var message = new Mock<IServiceBusMessageContainer>();
        message.SetupGet(m => m.Message).Returns((ServiceBusReceivedMessage?) null);

        var job = new AzureJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Equal("UNKNOWN", job.IdempotencyId);
    }

    [Fact]
    public void MessageId_ReturnsUnknownWhenServiceBusMessageIdIsNull()
    {
        var receivedMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("body"));
        var message = new Mock<IServiceBusMessageContainer>();
        message.SetupGet(m => m.Message).Returns(receivedMessage);

        var job = new AzureJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        };

        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Equal("UNKNOWN", job.IdempotencyId);
    }

    [Fact]
    public void Properties_RoundTripAssignedValues()
    {
        var receivedMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("body"),
            "round-trip-id");
        var message = new Mock<IServiceBusMessageContainer>();
        message.SetupGet(m => m.Message).Returns(receivedMessage);
        const string body = "round-trip-body";
        var createdAt = DateTime.UtcNow.AddMinutes(-5);

        var job = new AzureJobModel
        {
            Message = message.Object,
            CreatedAtUtc = createdAt,
            Body = body
        };

        Assert.Same(message.Object, job.Message);
        Assert.Equal("round-trip-id", job.MessageId);
        Assert.Equal("round-trip-id", job.IdempotencyId);
        Assert.Equal(createdAt, job.CreatedAtUtc);
        Assert.Equal(body, job.Body);
    }
}