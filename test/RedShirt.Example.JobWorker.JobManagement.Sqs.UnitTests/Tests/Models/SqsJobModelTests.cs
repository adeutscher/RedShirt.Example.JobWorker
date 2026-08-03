using Amazon.SQS.Model;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.UnitTests.Tests.Models;

public class SqsJobModelTests
{
    [Fact]
    public void IdempotencyId_MatchesMessageId()
    {
        var messageId = Guid.NewGuid().ToString();

        var job = new SqsJobModel
        {
            RawMessage = new Message(),
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString()
        };

        Assert.Equal(messageId, job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIRawJobModel()
    {
        var job = new SqsJobModel
        {
            RawMessage = new Message(),
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString()
        };

        Assert.IsAssignableFrom<IRawJobModel>(job);
    }

    [Fact]
    public void Properties_RoundTripAssignedValues()
    {
        var message = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString()
        };
        var body = Guid.NewGuid().ToString();
        var messageId = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);

        var job = new SqsJobModel
        {
            RawMessage = message,
            MessageId = messageId,
            CreatedAtUtc = createdAt,
            Body = body
        };

        Assert.Same(message, job.RawMessage);
        Assert.Equal(messageId, job.MessageId);
        Assert.Equal(messageId, job.IdempotencyId);
        Assert.Equal(createdAt, job.CreatedAtUtc);
        Assert.Equal(body, job.Body);
    }

    [Fact]
    public void RawMessage_CanBeReassigned()
    {
        var original = new Message {ReceiptHandle = "original"};
        var replacement = new Message {ReceiptHandle = "replacement"};

        var job = new SqsJobModel
        {
            RawMessage = original,
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString()
        };

        job.RawMessage = replacement;

        Assert.Same(replacement, job.RawMessage);
        Assert.Equal("replacement", job.RawMessage.ReceiptHandle);
    }
}