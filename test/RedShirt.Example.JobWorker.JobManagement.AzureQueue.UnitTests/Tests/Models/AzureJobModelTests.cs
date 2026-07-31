using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Models;

public class AzureJobModelTests
{
    [Fact]
    public void IdempotencyId_MatchesMessageId()
    {
        const string messageId = "queue-message-456";
        var message = new Mock<IQueueMessageModel>();
        message.SetupGet(m => m.MessageId).Returns(messageId);

        var job = new AzureJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        Assert.Equal(messageId, job.IdempotencyId);
        Assert.Equal(job.MessageId, job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIJobModel()
    {
        var job = new AzureJobModel
        {
            Message = new Mock<IQueueMessageModel>().Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        Assert.IsAssignableFrom<IJobModel>(job);
    }

    [Fact]
    public void MessageId_DelegatesToQueueMessage()
    {
        const string messageId = "queue-message-123";
        var message = new Mock<IQueueMessageModel>();
        message.SetupGet(m => m.MessageId).Returns(messageId);

        var job = new AzureJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        Assert.Equal(messageId, job.MessageId);
        message.VerifyGet(m => m.MessageId, Times.Once);
    }

    [Fact]
    public void Properties_RoundTripAssignedValues()
    {
        var message = new Mock<IQueueMessageModel>();
        message.SetupGet(m => m.MessageId).Returns("round-trip-id");
        var data = new Mock<IJobDataModel>();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);

        var job = new AzureJobModel
        {
            Message = message.Object,
            CreatedAtUtc = createdAt,
            Data = data.Object
        };

        Assert.Same(message.Object, job.Message);
        Assert.Equal("round-trip-id", job.MessageId);
        Assert.Equal("round-trip-id", job.IdempotencyId);
        Assert.Equal(createdAt, job.CreatedAtUtc);
        Assert.Same(data.Object, job.Data);
    }
}