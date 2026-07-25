using Amazon.SQS.Model;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.UnitTests.Tests.Utility;

public class SqsMessageAttributeRetrieverTests
{
    [Fact]
    public void TryGetApproximateFirstReceiveUtc_WhenAttributesNull_ReturnsNull()
    {
        var message = new Message {Attributes = null!};

        var result = SqsMessageAttributeRetriever.TryGetApproximateFirstReceiveUtc(message);

        Assert.Null(result);
    }

    [Fact]
    public void TryGetApproximateFirstReceiveUtc_WhenAttributeMissing_ReturnsNull()
    {
        var message = new Message
        {
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateReceiveCount] = "3"
            }
        };

        var result = SqsMessageAttributeRetriever.TryGetApproximateFirstReceiveUtc(message);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("12.5")]
    public void TryGetApproximateFirstReceiveUtc_WhenAttributeUnparseable_ReturnsNull(string raw)
    {
        var message = new Message
        {
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateFirstReceiveTimestamp] = raw
            }
        };

        var result = SqsMessageAttributeRetriever.TryGetApproximateFirstReceiveUtc(message);

        Assert.Null(result);
    }

    [Fact]
    public void TryGetApproximateFirstReceiveUtc_WhenAttributeValid_ReturnsUtcDateTime()
    {
        var expected = new DateTimeOffset(2024, 6, 15, 12, 30, 45, TimeSpan.Zero);
        var epochMs = expected.ToUnixTimeMilliseconds();

        var message = new Message
        {
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateFirstReceiveTimestamp] = epochMs.ToString()
            }
        };

        var result = SqsMessageAttributeRetriever.TryGetApproximateFirstReceiveUtc(message);

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result.Value.Kind);
        Assert.Equal(expected.UtcDateTime, result.Value);
    }

    [Fact]
    public void TryGetApproximateFirstReceiveUtc_WhenEpochIsZero_ReturnsUnixEpoch()
    {
        var message = new Message
        {
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateFirstReceiveTimestamp] = "0"
            }
        };

        var result = SqsMessageAttributeRetriever.TryGetApproximateFirstReceiveUtc(message);

        Assert.Equal(DateTimeOffset.UnixEpoch.UtcDateTime, result);
    }

    [Fact]
    public void TryGetApproximateReceiveCount_WhenAttributesNull_ReturnsNull()
    {
        var message = new Message {Attributes = null!};

        var result = SqsMessageAttributeRetriever.TryGetApproximateReceiveCount(message);

        Assert.Null(result);
    }

    [Fact]
    public void TryGetApproximateReceiveCount_WhenAttributeMissing_ReturnsNull()
    {
        var message = new Message
        {
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateFirstReceiveTimestamp] = "0"
            }
        };

        var result = SqsMessageAttributeRetriever.TryGetApproximateReceiveCount(message);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("1.5")]
    [InlineData("3e2")]
    public void TryGetApproximateReceiveCount_WhenAttributeUnparseable_ReturnsNull(string raw)
    {
        var message = new Message
        {
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateReceiveCount] = raw
            }
        };

        var result = SqsMessageAttributeRetriever.TryGetApproximateReceiveCount(message);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void TryGetApproximateReceiveCount_WhenAttributeValid_ReturnsCount(int receiveCount)
    {
        var message = new Message
        {
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateReceiveCount] = receiveCount.ToString()
            }
        };

        var result = SqsMessageAttributeRetriever.TryGetApproximateReceiveCount(message);

        Assert.Equal(receiveCount, result);
    }

    [Fact]
    public void TryGetApproximateReceiveCount_WhenEmptyAttributes_ReturnsNull()
    {
        var message = new Message {Attributes = new Dictionary<string, string>()};

        var result = SqsMessageAttributeRetriever.TryGetApproximateReceiveCount(message);

        Assert.Null(result);
    }
}
