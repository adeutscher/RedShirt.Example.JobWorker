using Apache.NMS;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Models;

public class ActiveMqRawJobModelTests
{
    [Fact]
    public void Body_FromBytesMessage()
    {
        var bytesMessage = new Mock<IBytesMessage>();
        bytesMessage.Setup(m => m.BodyLength).Returns(12);
        bytesMessage.Setup(m => m.Reset());
        bytesMessage.Setup(m => m.ReadBytes(It.IsAny<byte[]>())).Returns((byte[] output) =>
        {
            const string msg = "Hello World!";
            var prep = Encoding.UTF8.GetBytes(msg);
            prep.CopyTo(output);
            return msg.Length;
        });

        var job = new ActiveMqRawJobModel
        {
            Message = bytesMessage.Object,
            MessageId = "bytes-msg",
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal("Hello World!", job.Body);
        // Cached: second access should not re-read.
        Assert.Equal("Hello World!", job.Body);
        bytesMessage.Verify(m => m.Reset(), Times.Once);
        bytesMessage.Verify(m => m.ReadBytes(It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void Body_FromTextMessage()
    {
        var textMessage = new Mock<ITextMessage>();
        textMessage.Setup(m => m.Text).Returns("Hello World!");

        var job = new ActiveMqRawJobModel
        {
            Message = textMessage.Object,
            MessageId = "text-msg",
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal("Hello World!", job.Body);
    }

    [Fact]
    public void Body_ThrowsForNullMessage()
    {
        var job = new ActiveMqRawJobModel
        {
            Message = null!,
            MessageId = "null-msg",
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Throws<CouldNotRetrieveMessageBodyException>(() => _ = job.Body);
    }

    [Fact]
    public void Body_ThrowsForUnsupportedMessageType()
    {
        var message = new Mock<IMessage>();

        var job = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = "unsupported-msg",
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Throws<CouldNotRetrieveMessageBodyException>(() => _ = job.Body);
    }

    [Fact]
    public void IdempotencyId_MatchesMessageId()
    {
        var messageId = Guid.NewGuid().ToString();
        var message = new Mock<ITextMessage>();
        message.Setup(m => m.Text).Returns("body");

        var job = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal(messageId, job.IdempotencyId);
        Assert.Equal(job.MessageId, job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIRawJobModel()
    {
        var message = new Mock<ITextMessage>();
        message.Setup(m => m.Text).Returns("body");

        var job = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.IsAssignableFrom<IRawJobModel>(job);
    }

    [Theory]
    [InlineData("msg-1")]
    [InlineData("ID:activemq-host-12345-67890-1:1:1:1:1")]
    [InlineData("")]
    public void MessageId_RoundTrips(string messageId)
    {
        var message = new Mock<ITextMessage>();
        message.Setup(m => m.Text).Returns("body");

        var job = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal(messageId, job.MessageId);
        Assert.Equal(messageId, job.IdempotencyId);
    }

    [Fact]
    public void Properties_RoundTripAssignedValues()
    {
        var message = new Mock<ITextMessage>(MockBehavior.Strict);
        message.Setup(m => m.Text).Returns("assigned-body");
        var messageId = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);

        var job = new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = messageId,
            CreatedAtUtc = createdAt
        };

        Assert.Same(message.Object, job.Message);
        Assert.Equal(messageId, job.MessageId);
        Assert.Equal(messageId, job.IdempotencyId);
        Assert.Equal(createdAt, job.CreatedAtUtc);
        Assert.Equal("assigned-body", job.Body);
    }
}