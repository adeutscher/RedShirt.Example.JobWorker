using Apache.NMS;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Models;

public class JobModelTests
{
    [Fact]
    public void IdempotencyId_MatchesMessageId()
    {
        var messageId = Guid.NewGuid().ToString();

        var job = new JobModel
        {
            Message = new Mock<IMessage>().Object,
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        Assert.Equal(messageId, job.IdempotencyId);
        Assert.Equal(job.MessageId, job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIJobModel()
    {
        var job = new JobModel
        {
            Message = new Mock<IMessage>().Object,
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        Assert.IsAssignableFrom<IJobModel>(job);
    }

    [Theory]
    [InlineData("msg-1")]
    [InlineData("ID:activemq-host-12345-67890-1:1:1:1:1")]
    [InlineData("")]
    public void MessageId_RoundTrips(string messageId)
    {
        var job = new JobModel
        {
            Message = new Mock<IMessage>().Object,
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        Assert.Equal(messageId, job.MessageId);
        Assert.Equal(messageId, job.IdempotencyId);
    }

    [Fact]
    public void Properties_RoundTripAssignedValues()
    {
        var message = new Mock<IMessage>(MockBehavior.Strict);
        var data = new Mock<IJobDataModel>(MockBehavior.Strict);
        var messageId = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);

        var job = new JobModel
        {
            Message = message.Object,
            MessageId = messageId,
            CreatedAtUtc = createdAt,
            Data = data.Object
        };

        Assert.Same(message.Object, job.Message);
        Assert.Equal(messageId, job.MessageId);
        Assert.Equal(messageId, job.IdempotencyId);
        Assert.Equal(createdAt, job.CreatedAtUtc);
        Assert.Same(data.Object, job.Data);
    }
}