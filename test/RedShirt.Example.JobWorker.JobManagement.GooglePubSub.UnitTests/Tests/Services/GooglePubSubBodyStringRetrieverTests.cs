using Google.Protobuf;
using Google.Cloud.PubSub.V1;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class GooglePubSubBodyStringRetrieverTests
{
    [Fact]
    public void ShouldReturnUtf8Body()
    {
        var received = new ReceivedMessage
        {
            AckId = "ack-1",
            Message = new PubsubMessage
            {
                MessageId = "message-123",
                Data = ByteString.CopyFromUtf8("{\"SleepDurationSeconds\":12}")
            }
        };

        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns(received);

        var body = new GooglePubSubBodyStringRetriever().GetBody(container.Object);

        Assert.Equal("{\"SleepDurationSeconds\":12}", body);
    }
}
