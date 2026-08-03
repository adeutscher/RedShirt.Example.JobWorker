using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Utility;

public class PubSubMessageAttributeRetrieverTests
{
    [Fact]
    public void TryGetDeliveryAttempt_WhenMessageMissing_ReturnsNull()
    {
        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns((ReceivedMessage?) null);

        Assert.Null(PubSubMessageAttributeRetriever.TryGetDeliveryAttempt(container.Object));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    public void TryGetDeliveryAttempt_WhenPopulated_ReturnsValue(int deliveryAttempt, int expected)
    {
        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns(new ReceivedMessage
        {
            AckId = "ack",
            DeliveryAttempt = deliveryAttempt,
            Message = new PubsubMessage {MessageId = "m", Data = ByteString.CopyFromUtf8("{}")}
        });

        Assert.Equal(expected, PubSubMessageAttributeRetriever.TryGetDeliveryAttempt(container.Object));
    }

    [Theory]
    [InlineData(0)]
    public void TryGetDeliveryAttempt_WhenUnset_ReturnsNull(int deliveryAttempt)
    {
        var container = new Mock<IPubSubMessageContainer>();
        container.SetupGet(c => c.Message).Returns(new ReceivedMessage
        {
            AckId = "ack",
            DeliveryAttempt = deliveryAttempt,
            Message = new PubsubMessage {MessageId = "m", Data = ByteString.CopyFromUtf8("{}")}
        });

        Assert.Null(PubSubMessageAttributeRetriever.TryGetDeliveryAttempt(container.Object));
    }
}