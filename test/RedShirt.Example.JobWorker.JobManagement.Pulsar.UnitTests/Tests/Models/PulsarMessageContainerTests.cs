using Pulsar.Client.Common;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Models;

public class PulsarMessageContainerTests
{
    private static MessageId CreateMessageId(long ledgerId = 10, long entryId = 99, int partition = 7,
        string topicName = "events")
    {
        return new MessageId(ledgerId, entryId, MessageIdType.Single, partition, topicName, null);
    }

    [Theory]
    [InlineData(0, 0, 0, "t", "t:0:0:0")]
    [InlineData(1, 2, 3, "orders", "orders:3:1:2")]
    [InlineData(100, 200, -1, "persistent://public/default/jobs", "persistent://public/default/jobs:-1:100:200")]
    public void MessageId_FormatsTopicPartitionLedgerEntry(long ledgerId, long entryId, int partition,
        string topic, string expectedMessageId)
    {
        var container = new PulsarMessageContainer
        {
            PulsarMessageId = CreateMessageId(ledgerId, entryId, partition, topic),
            Topic = topic
        };

        Assert.Equal(partition, container.Partition);
        Assert.Equal(entryId, container.EntryId);
        Assert.Equal(expectedMessageId, container.MessageId);
        Assert.False(container.MessageIdIsDefault);
    }

    [Fact]
    public void MessageId_UsesContainerTopic_NotMessageIdTopicName()
    {
        // MessageId.TopicName may differ from the container Topic (e.g. fallback topic from the consumer).
        var container = new PulsarMessageContainer
        {
            PulsarMessageId = CreateMessageId(3, 4, 1, "broker-topic"),
            Topic = "configured-topic"
        };

        Assert.Equal("configured-topic", container.Topic);
        Assert.Equal("configured-topic:1:3:4", container.MessageId);
        Assert.Equal("broker-topic", container.PulsarMessageId!.TopicName);
    }

    [Fact]
    public void Properties_MapFromMessageId()
    {
        var messageId = CreateMessageId();
        var container = new PulsarMessageContainer
        {
            PulsarMessageId = messageId,
            Key = "user-1",
            Value = "{\"ok\":true}",
            Topic = "events"
        };

        Assert.Same(messageId, container.PulsarMessageId);
        Assert.Equal("user-1", container.Key);
        Assert.Equal("{\"ok\":true}", container.Value);
        Assert.Equal("events", container.Topic);
        Assert.Equal(7, container.Partition);
        Assert.Equal(99, container.EntryId);
        Assert.Equal("events:7:10:99", container.MessageId);
        Assert.False(container.MessageIdIsDefault);
    }

    [Fact]
    public void Properties_WhenKeyAndValueAreNull_StillExposeTopicAndMessageId()
    {
        var container = new PulsarMessageContainer
        {
            PulsarMessageId = CreateMessageId(1, 2, 0, "t"),
            Key = null,
            Value = null,
            Topic = "t"
        };

        Assert.Null(container.Key);
        Assert.Null(container.Value);
        Assert.Equal("t", container.Topic);
        Assert.Equal(0, container.Partition);
        Assert.Equal(2, container.EntryId);
        Assert.Equal("t:0:1:2", container.MessageId);
        Assert.False(container.MessageIdIsDefault);
    }

    [Fact]
    public void Properties_WhenMessageIdIsNull_StillPreserveKeyValueAndTopic()
    {
        var container = new PulsarMessageContainer
        {
            PulsarMessageId = null,
            Key = "k",
            Value = "v",
            Topic = "orphan-topic"
        };

        Assert.Equal("k", container.Key);
        Assert.Equal("v", container.Value);
        Assert.Equal("orphan-topic", container.Topic);
        Assert.Equal(-1, container.Partition);
        Assert.Equal(-1, container.EntryId);
        Assert.Equal("UNKNOWN", container.MessageId);
        Assert.True(container.MessageIdIsDefault);
    }

    [Fact]
    public void Properties_WhenMessageIdIsNull_UseSafeDefaults()
    {
        var container = new PulsarMessageContainer
        {
            PulsarMessageId = null,
            Topic = string.Empty
        };

        Assert.Null(container.PulsarMessageId);
        Assert.Null(container.Key);
        Assert.Null(container.Value);
        Assert.Equal(string.Empty, container.Topic);
        Assert.Equal(-1, container.Partition);
        Assert.Equal(-1, container.EntryId);
        Assert.Equal("UNKNOWN", container.MessageId);
        Assert.True(container.MessageIdIsDefault);
    }
}