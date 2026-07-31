using Google.Protobuf;
using Google.Cloud.PubSub.V1;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Models;

public class GooglePubSubJobModelTests
{
    [Fact]
    public void ShouldExposeMessageIdAndIdempotencyId()
    {
        var received = new ReceivedMessage
        {
            AckId = "ack-1",
            Message = new PubsubMessage
            {
                MessageId = "message-123",
                Data = ByteString.CopyFromUtf8("{}")
            }
        };

        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns(received);

        var model = new GooglePubSubJobModel
        {
            Message = container.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        Assert.Equal("message-123", model.MessageId);
        Assert.Equal("message-123", model.IdempotencyId);
    }

    [Fact]
    public void ShouldFallbackToUnknownWhenMessageMissing()
    {
        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns((ReceivedMessage?) null);

        var model = new GooglePubSubJobModel
        {
            Message = container.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        Assert.Equal("UNKNOWN", model.MessageId);
        Assert.Equal("UNKNOWN", model.IdempotencyId);
    }
}
