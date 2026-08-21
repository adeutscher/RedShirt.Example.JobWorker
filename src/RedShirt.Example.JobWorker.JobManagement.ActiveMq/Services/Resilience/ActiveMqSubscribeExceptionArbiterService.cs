using Apache.NMS;
using Apache.NMS.ActiveMQ;
using System.Net.Sockets;
using ActiveMqIoException = Apache.NMS.ActiveMQ.IOException;
using IOException = System.IO.IOException;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

/// <summary>
///     Classifies <see cref="IConnection.ExceptionListener" /> exceptions for the ActiveMQ subscribe job source:
///     reconnect, halt-on-failure stop, or accounted-for transient noise.
/// </summary>
internal interface IActiveMqSubscribeExceptionArbiter
{
    /// <summary>
    ///     Whether <paramref name="exception" /> (or any inner exception) looks like expected
    ///     <see cref="IConnection.ExceptionListener" /> noise that should neither reconnect nor halt.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Used to suppress "unaccounted-for" warnings for known brief / non-fatal NMS callbacks
    ///         (especially broker Exception frames).
    ///     </para>
    ///     <para>
    ///         Typical cases: <see cref="BrokerException" />; brief broker pressure
    ///         (<see cref="ResourceAllocationException" />, <see cref="TransactionRolledBackException" />);
    ///         local session state (<see cref="IllegalStateException" />).
    ///         Permanent auth/config failures belong in <see cref="IsReasonToStopIfHaltOnFailure" />;
    ///         transport drops belong in <see cref="IsReasonToReconnect" />.
    ///     </para>
    /// </remarks>
    bool IsAccountedForAndLikelyTransientError(Exception exception);

    /// <summary>
    ///     Whether <paramref name="exception" /> (or any inner exception) is a reason to reset the
    ///     consumer and run the subscribe reconnect loop.
    /// </summary>
    /// <remarks>
    ///     Covers transport / connection drops and a closed consumer that still needs a fresh
    ///     subscription. Expected non-reconnect NMS callbacks are classified by
    ///     <see cref="IsAccountedForAndLikelyTransientError" /> or
    ///     <see cref="IsReasonToStopIfHaltOnFailure" /> instead.
    /// </remarks>
    bool IsReasonToReconnect(Exception exception);

    /// <summary>
    ///     Whether <paramref name="exception" /> (or any inner exception) is a permanent auth / config
    ///     failure that should stop the worker when halt-on-failure is enabled.
    /// </summary>
    /// <remarks>
    ///     These are expected NMS signals where reconnecting will not help (bad credentials, missing
    ///     destination, invalid client id/selector). Callers should only stop when
    ///     <c>HaltOnFailure</c> is true.
    /// </remarks>
    bool IsReasonToStopIfHaltOnFailure(Exception exception);
}

/// <summary>
///     Default <see cref="IActiveMqSubscribeExceptionArbiter" /> implementation.
/// </summary>
internal class ActiveMqSubscribeExceptionArbiterService : IActiveMqSubscribeExceptionArbiter
{
    /// <inheritdoc />
    public bool IsAccountedForAndLikelyTransientError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                // Broker Exception command frames on the connection — often non-fatal from the
                // client POV (disconnect races, advisory-style errors).
                case BrokerException:
                // Brief broker-side contention / rollback.
                case ResourceAllocationException:
                case TransactionRolledBackException:
                // Local session state noise on ExceptionListener.
                case IllegalStateException:
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
                case NMSSecurityException:
                case InvalidDestinationException:
                case InvalidClientIDException:
                case InvalidSelectorException:
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
                // Abrupt peer close / wire EOF (often wrapped in NMSException).
                case EndOfStreamException:
                // Socket-level failures (reset, refused, timed out, host unreachable).
                case SocketException:
                // OpenWire inactivity monitor and other ActiveMQ transport IO failures.
                case ActiveMqIoException:
                // Generic stream IO from the transport thread.
                case IOException:
                // Connection lifecycle failures reported by the NMS client.
                case NMSConnectionException:
                case ConnectionClosedException:
                case ConnectionFailedException:
                // Consumer gone — rebuild via reconnect/resubscribe rather than treat as noise.
                case ConsumerClosedException:
                    return true;
            }
        }

        return false;
    }
}