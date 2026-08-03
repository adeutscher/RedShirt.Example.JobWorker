using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Models;

public class RedisStreamRawJobModelTests
{
    [Fact]
    public void Body_ReturnsBodyField()
    {
        var model = new RedisStreamRawJobModel
        {
            Message = new StreamEntry("1-0",
            [
                new NameValueEntry("message_id", "abc"),
                new NameValueEntry("body", """{"x":1}""")
            ]),
            MessageId = "1-0",
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal("""{"x":1}""", model.Body);
        Assert.Equal("abc", model.IdempotencyId);
    }

    [Fact]
    public void Body_MissingBody_ThrowsArgumentException()
    {
        var model = new RedisStreamRawJobModel
        {
            Message = new StreamEntry("1-0", [new NameValueEntry("message_id", "abc")]),
            MessageId = "1-0",
            CreatedAtUtc = DateTime.UtcNow
        };

        var thrown = Assert.Throws<ArgumentException>(() => _ = model.Body);
        Assert.Contains("body", thrown.Message);
    }

    [Fact]
    public void IdempotencyId_MissingField_ReturnsNull()
    {
        var model = new RedisStreamRawJobModel
        {
            Message = new StreamEntry("1-0", [new NameValueEntry("body", "{}")]),
            MessageId = "1-0",
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Null(model.IdempotencyId);
    }
}
