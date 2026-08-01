using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Models;

public class AzureRawJobModelTests
{
    private static (Mock<IServiceBusMessageContainer> Container, AzureRawJobModel Job) CreateJob(
        string body = "body",
        string? messageId = null,
        DateTime? createdAtUtc = null)
    {
        var receivedMessage = messageId is null
            ? ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body))
            : ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body), messageId);
        var message = new Mock<IServiceBusMessageContainer>();
        message.SetupGet(m => m.Message).Returns(receivedMessage);

        var job = new AzureRawJobModel
        {
            Message = message.Object,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        return (message, job);
    }

    [Fact]
    public void Body_DelegatesToServiceBusMessage()
    {
        const string body = "こんにちは 🌍";
        var (_, job) = CreateJob(body, "round-trip-id", DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(body, job.Body);
        Assert.Equal("round-trip-id", job.MessageId);
    }

    [Fact]
    public void Body_ReturnsEmptyString_WhenMessageBodyIsEmpty()
    {
        var (_, job) = CreateJob(string.Empty);

        Assert.Equal(string.Empty, job.Body);
    }

    [Fact]
    public void Body_ReturnsNull_WhenInnerMessageIsNull()
    {
        var message = new Mock<IServiceBusMessageContainer>();
        message.SetupGet(m => m.Message).Returns((ServiceBusReceivedMessage?) null);

        var job = new AzureRawJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Null(job.Body);
    }

    [Fact]
    public void IdempotencyId_MatchesMessageId()
    {
        var (_, job) = CreateJob(messageId: "service-bus-message-456");

        Assert.Equal(job.MessageId, job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIRawJobModel()
    {
        var (_, job) = CreateJob();

        Assert.IsAssignableFrom<IRawJobModel>(job);
    }

    [Fact]
    public void MessageId_DelegatesToServiceBusMessage()
    {
        var (_, job) = CreateJob(messageId: "service-bus-message-123");

        Assert.Equal("service-bus-message-123", job.MessageId);
        Assert.Equal("service-bus-message-123", job.IdempotencyId);
    }

    [Fact]
    public void MessageId_ReturnsUnknownWhenInnerMessageIsNull()
    {
        var message = new Mock<IServiceBusMessageContainer>();
        message.SetupGet(m => m.Message).Returns((ServiceBusReceivedMessage?) null);

        var job = new AzureRawJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Equal("UNKNOWN", job.IdempotencyId);
    }

    [Fact]
    public void MessageId_ReturnsUnknownWhenServiceBusMessageIdIsNull()
    {
        var (_, job) = CreateJob();

        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Equal("UNKNOWN", job.IdempotencyId);
    }
}