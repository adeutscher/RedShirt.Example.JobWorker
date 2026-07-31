using Confluent.Kafka;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Models;

public class KafkaMessageContainerTests
{
    [Fact]
    public void Properties_MapFromConsumeResult()
    {
        var container = new KafkaMessageContainer
        {
            Result = new ConsumeResult<string, string>
            {
                Topic = "events",
                Partition = new Partition(7),
                Offset = new Offset(99),
                Message = new Message<string, string>
                {
                    Key = "user-1",
                    Value = "{\"ok\":true}"
                }
            }
        };

        Assert.Equal("user-1", container.Key);
        Assert.Equal("{\"ok\":true}", container.Value);
        Assert.Equal("events", container.Topic);
        Assert.Equal(7, container.Partition);
        Assert.Equal(99, container.Offset);
        Assert.Equal("events:7:99", container.MessageId);
        Assert.False(container.MessageIdIsDefault);
    }

    [Fact]
    public void Properties_WhenMessageIsNull_ExposeNullKeyAndValue()
    {
        var container = new KafkaMessageContainer
        {
            Result = new ConsumeResult<string, string>
            {
                Topic = "t",
                Partition = new Partition(0),
                Offset = new Offset(1),
                Message = null
            }
        };

        Assert.Null(container.Key);
        Assert.Null(container.Value);
        Assert.Equal("t", container.Topic);
        Assert.Equal(0, container.Partition);
        Assert.Equal(1, container.Offset);
        Assert.Equal("t:0:1", container.MessageId);
        Assert.False(container.MessageIdIsDefault);
    }

    [Fact]
    public void Properties_WhenResultIsNull_UseSafeDefaults()
    {
        var container = new KafkaMessageContainer
        {
            Result = null
        };

        Assert.Null(container.Key);
        Assert.Null(container.Value);
        Assert.Equal(string.Empty, container.Topic);
        Assert.Equal(-1, container.Partition);
        Assert.Equal(-1, container.Offset);
        Assert.Equal("UNKNOWN", container.MessageId);
        Assert.True(container.MessageIdIsDefault);
    }
}