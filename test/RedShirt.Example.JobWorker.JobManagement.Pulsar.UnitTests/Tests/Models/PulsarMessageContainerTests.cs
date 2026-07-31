using Pulsar.Client.Common;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Models;

public class PulsarMessageContainerTests
{
    [Fact]
    public void Properties_MapFromMessageId()
    {
        var messageId = new MessageId(10, 99, MessageIdType.Single, 7, "events", null);
        var container = new PulsarMessageContainer
        {
            PulsarMessageId = messageId,
            Key = "user-1",
            Value = "{\"ok\":true}",
            Topic = "events"
        };

        Assert.Equal("user-1", container.Key);
        Assert.Equal("{\"ok\":true}", container.Value);
        Assert.Equal("events", container.Topic);
        Assert.Equal(7, container.Partition);
        Assert.Equal(99, container.EntryId);
        Assert.Equal("events:7:10:99", container.MessageId);
        Assert.False(container.MessageIdIsDefault);
    }

    [Fact]
    public void Properties_WhenMessageIdIsNull_UseSafeDefaults()
    {
        var container = new PulsarMessageContainer
        {
            PulsarMessageId = null,
            Topic = string.Empty
        };

        Assert.Null(container.Key);
        Assert.Null(container.Value);
        Assert.Equal(string.Empty, container.Topic);
        Assert.Equal(-1, container.Partition);
        Assert.Equal(-1, container.EntryId);
        Assert.Equal("UNKNOWN", container.MessageId);
        Assert.True(container.MessageIdIsDefault);
    }
}
