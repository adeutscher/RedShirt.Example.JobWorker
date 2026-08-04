using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Models;

public class PulsarJobModelTests
{
    private static (Mock<IPulsarMessageContainer> Container, PulsarJobModel Job) CreateJob(
        string messageId = "t:0:1:2",
        bool messageIdIsDefault = false,
        string? body = "body",
        DateTime? createdAtUtc = null)
    {
        var container = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        container.SetupGet(c => c.MessageId).Returns(messageId);
        container.SetupGet(c => c.MessageIdIsDefault).Returns(messageIdIsDefault);

        var job = new PulsarJobModel
        {
            Message = container.Object,
            Body = body,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        return (container, job);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"ok\":true}")]
    public void Body_RoundTripsConfiguredValue(string? body)
    {
        var (_, job) = CreateJob(body: body);

        Assert.Equal(body, job.Body);
    }

    [Fact]
    public void CreatedAtUtc_RoundTripsConfiguredValue()
    {
        var createdAt = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        var (_, job) = CreateJob(createdAtUtc: createdAt);

        Assert.Equal(createdAt, job.CreatedAtUtc);
    }

    [Fact]
    public void IdempotencyId_IsNull_WhenMessageIdIsDefault()
    {
        var (_, job) = CreateJob("UNKNOWN", true);

        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Null(job.IdempotencyId);
    }

    [Fact]
    public void IdempotencyId_MatchesMessageId_WhenMessageIdIsNotDefault()
    {
        var (_, job) = CreateJob("events:1:2:3");

        Assert.Equal(job.MessageId, job.IdempotencyId);
        Assert.Equal("events:1:2:3", job.IdempotencyId);
    }

    [Fact]
    public void ImplementsIRawJobModel()
    {
        var (_, job) = CreateJob();

        Assert.IsAssignableFrom<IRawJobModel>(job);
    }

    [Fact]
    public void MessageId_DelegatesToMessage()
    {
        var (_, job) = CreateJob("events:7:10:99");

        Assert.Equal("events:7:10:99", job.MessageId);
    }

    [Fact]
    public void Message_ExposesConfiguredContainer()
    {
        var (container, job) = CreateJob();

        Assert.Same(container.Object, job.Message);
    }
}