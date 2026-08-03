using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Models;

public class GooglePubSubJobModelTests
{
    private static (Mock<IPubSubMessageContainer> Container, GooglePubSubJobModel Job) CreateJob(
        string body = "body",
        string? messageId = null,
        DateTime? createdAtUtc = null)
    {
        var received = new ReceivedMessage
        {
            AckId = "ack-1",
            Message = new PubsubMessage
            {
                MessageId = messageId ?? string.Empty,
                Data = ByteString.CopyFromUtf8(body)
            }
        };

        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns(received);

        var job = new GooglePubSubJobModel
        {
            Message = container.Object,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        return (container, job);
    }

    [Fact]
    public void Body_DelegatesToPubSubMessage()
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
        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns((ReceivedMessage?) null);

        var job = new GooglePubSubJobModel
        {
            Message = container.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Null(job.Body);
    }

    [Fact]
    public void IdempotencyId_MatchesMessageId()
    {
        var (_, job) = CreateJob(messageId: "pubsub-message-456");

        Assert.Equal(job.MessageId, job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIRawJobModel()
    {
        var (_, job) = CreateJob();

        Assert.IsAssignableFrom<IRawJobModel>(job);
    }

    [Fact]
    public void MessageId_DelegatesToPubSubMessage()
    {
        var (_, job) = CreateJob(messageId: "pubsub-message-123");

        Assert.Equal("pubsub-message-123", job.MessageId);
        Assert.Equal("pubsub-message-123", job.IdempotencyId);
    }

    [Fact]
    public void MessageId_ReturnsUnknownWhenInnerMessageIsNull()
    {
        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns((ReceivedMessage?) null);

        var job = new GooglePubSubJobModel
        {
            Message = container.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Equal("UNKNOWN", job.IdempotencyId);
    }

    [Fact]
    public void MessageId_ReturnsUnknownWhenMessageIdIsEmpty()
    {
        var (_, job) = CreateJob(messageId: string.Empty);

        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Equal("UNKNOWN", job.IdempotencyId);
    }
}
