using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Models;

public class AzureQueueStorageRawJobModelTests
{
    private static Mock<IQueueMessageModel> CreateMessage(string messageId = "queue-message-id",
        string body = "body")
    {
        var message = new Mock<IQueueMessageModel>();
        message.SetupGet(m => m.MessageId).Returns(messageId);
        message.SetupGet(m => m.Body).Returns(body);
        return message;
    }

    [Fact]
    public void Body_DelegatesToQueueMessage()
    {
        var message = CreateMessage("round-trip-id", "round-trip-body");
        var createdAt = DateTime.UtcNow.AddMinutes(-5);

        var job = new AzureQueueStorageRawJobModel
        {
            Message = message.Object,
            CreatedAtUtc = createdAt
        };

        Assert.Same(message.Object, job.Message);
        Assert.Equal("round-trip-id", job.MessageId);
        Assert.Equal("round-trip-id", job.IdempotencyId);
        Assert.Equal(createdAt, job.CreatedAtUtc);
        Assert.Equal("round-trip-body", job.Body);
    }

    [Fact]
    public void IdempotencyId_MatchesMessageId()
    {
        const string messageId = "queue-message-456";
        var message = CreateMessage(messageId);

        var job = new AzureQueueStorageRawJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal(messageId, job.IdempotencyId);
        Assert.Equal(job.MessageId, job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIRawJobModel()
    {
        var job = new AzureQueueStorageRawJobModel
        {
            Message = CreateMessage().Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.IsAssignableFrom<IRawJobModel>(job);
    }

    [Fact]
    public void MessageId_DelegatesToQueueMessage()
    {
        const string messageId = "queue-message-123";
        var message = CreateMessage(messageId);

        var job = new AzureQueueStorageRawJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal(messageId, job.MessageId);
        message.VerifyGet(m => m.MessageId, Times.Once);
    }
}