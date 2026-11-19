using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Utility;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Utility;

public class RabbitMqDeliveryCountReaderTests
{
    private static IReadOnlyBasicProperties CreateProperties(object? headerValue, bool includeHeader = true)
    {
        var headers = new Dictionary<string, object?>();
        if (includeHeader)
        {
            headers[RabbitMqDeliveryCountReader.DeliveryCountHeader] = headerValue;
        }

        var properties = new Mock<IReadOnlyBasicProperties>(MockBehavior.Strict);
        properties.SetupGet(p => p.Headers).Returns(headers);
        return properties.Object;
    }

    public static TheoryData<object> NumericHeaderValues => new()
    {
        3L,
        3,
        (short) 3,
        (byte) 3,
        3UL
    };

    [Theory]
    [MemberData(nameof(NumericHeaderValues))]
    public void NumericHeader_IsReadWhetherOrNotRedelivered(object headerValue)
    {
        Assert.Equal(3, RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(headerValue), true));
        Assert.Equal(3, RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(headerValue), false));
    }

    [Fact]
    public void HugeHeader_IsClampedToIntMaxValue()
    {
        Assert.Equal(int.MaxValue,
            RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(long.MaxValue), true));
    }

    public static TheoryData<object> UnusableHeaderValues => new()
    {
        -1L,
        "3",
        Encoding.UTF8.GetBytes("3"),
        ulong.MaxValue
    };

    [Theory]
    [MemberData(nameof(UnusableHeaderValues))]
    public void UnusableHeader_FallsBackToRedeliveredFlag(object headerValue)
    {
        Assert.Null(RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(headerValue), true));
        Assert.Equal(0, RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(headerValue), false));
    }

    [Fact]
    public void NullHeader_FallsBackToRedeliveredFlag()
    {
        Assert.Null(RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(null), true));
        Assert.Equal(0, RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(null), false));
    }

    [Fact]
    public void MissingHeader_FirstDelivery_IsZero()
    {
        Assert.Equal(0,
            RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(null, false), false));
        Assert.Equal(0, RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(null, false));
    }

    [Fact]
    public void MissingHeader_Redelivered_IsUnknown()
    {
        Assert.Null(RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(CreateProperties(null, false), true));
        Assert.Null(RabbitMqDeliveryCountReader.TryGetPreviousDeliveryCount(null, true));
    }
}
