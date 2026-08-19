namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Constants;

/// <summary>
///     Reference: https://github.com/rabbitmq/amqp-0.9.1-spec/blob/main/docs/amqp-0-9-1-reference.md
/// </summary>
public class RabbitMqExceptionCodeConstants
{
    /// <summary>
    ///     Bottom range of channel error codes. These are "soft" errors. They immediately terminate the specific channel where
    ///     the mistake happened, but leave your overall connection and other channels completely healthy.
    /// </summary>
    public const ushort ChannelCodeMin = 400;

    /// <summary>
    ///     Top range of channel error codes. These are "soft" errors. They immediately terminate the specific channel where
    ///     the mistake happened, but leave your overall connection and other channels completely healthy.
    /// </summary>
    public const ushort ChannelCodeMax = 499;

    /// <summary>
    ///     Bottom range of one range of connection error codes. These are "hard" errors. When these codes are thrown, they
    ///     destroy the entire TCP connection. Because all channels live inside a connection, every channel on that connection
    ///     is permanently closed at once
    /// </summary>
    public const ushort ConnectionCodeRangeAMin = 300;

    /// <summary>
    ///     Top range of one range of connection error codes. These are "hard" errors. When these codes are thrown, they
    ///     destroy the entire TCP connection. Because all channels live inside a connection, every channel on that connection
    ///     is permanently closed at once
    /// </summary>
    public const ushort ConnectionCodeRangeAMax = 399;

    /// <summary>
    ///     Bottom range of one range of connection error codes. These are "hard" errors. When these codes are thrown, they
    ///     destroy the entire TCP connection. Because all channels live inside a connection, every channel on that connection
    ///     is permanently closed at once
    /// </summary>
    public const ushort ConnectionCodeRangeBMin = 500;

    /// <summary>
    ///     Top range of one range of connection error codes. These are "hard" errors. When these codes are thrown, they
    ///     destroy the entire TCP connection. Because all channels live inside a connection, every channel on that connection
    ///     is permanently closed at once
    /// </summary>
    public const ushort ConnectionCodeRangeBMax = 599;
}