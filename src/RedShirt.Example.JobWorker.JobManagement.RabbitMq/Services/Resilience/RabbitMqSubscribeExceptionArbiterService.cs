using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Constants;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

/// <summary>
///     Classifies connection-shutdown / callback exceptions for the RabbitMQ subscribe job source:
///     reconnect, halt-on-failure stop, or accounted-for transient noise.
/// </summary>
internal interface IRabbitMqSubscribeExceptionArbiter
{
    /// <summary>
    ///     Whether <paramref name="exception" /> (or any inner exception) looks like expected
    ///     callback noise that should neither reconnect nor halt.
    /// </summary>
    bool IsAccountedForAndLikelyTransientError(Exception exception);

    /// <summary>
    ///     Whether <paramref name="exception" /> (or any inner exception) is a reason to reset the
    ///     channel and run the subscribe reconnect loop.
    /// </summary>
    bool IsReasonToReconnect(Exception exception);

    /// <summary>
    ///     Whether <paramref name="exception" /> (or any inner exception) is a permanent auth failure
    ///     that should stop the worker when halt-on-failure is enabled.
    /// </summary>
    /// <remarks>
    ///     Callers still funnel these through the reconnect loop so halt-on-failure / first-offense
    ///     transient policy can apply.
    /// </remarks>
    bool IsReasonToStopIfHaltOnFailure(Exception exception);
}

/// <summary>
///     Default <see cref="IRabbitMqSubscribeExceptionArbiter" /> implementation.
/// </summary>
internal class RabbitMqSubscribeExceptionArbiterService : IRabbitMqSubscribeExceptionArbiter
{
    /// <inheritdoc />
    public bool IsAccountedForAndLikelyTransientError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                // Soft channel errors leave the connection healthy; reconnect is not warranted.
                case OperationInterruptedException
                {
                    ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ChannelCodeMin
                    and <= RabbitMqExceptionCodeConstants.ChannelCodeMax
                }:
                // Local channel-limit issues — not a transport drop.
                case ChannelAllocationException:
                    return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsReasonToStopIfHaltOnFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case AuthenticationFailureException:
                case PossibleAuthenticationFailureException:
                    return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsReasonToReconnect(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case AlreadyClosedException:
                case BrokerUnreachableException:
                case ConnectFailureException:
                case SocketException:
                case IOException:
                case OperationInterruptedException
                {
                    ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ConnectionCodeRangeAMin
                    and <= RabbitMqExceptionCodeConstants.ConnectionCodeRangeAMax
                }:
                case OperationInterruptedException
                {
                    ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ConnectionCodeRangeBMin
                    and <= RabbitMqExceptionCodeConstants.ConnectionCodeRangeBMax
                }:
                    return true;
            }
        }

        return false;
    }
}
