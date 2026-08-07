using Pulsar.Client.Api;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using System.Net.Sockets;
using TimeoutException = Pulsar.Client.Api.TimeoutException;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

/// <summary>
///     Classifies Pulsar client exceptions for retry decisions.
/// </summary>
internal interface IPulsarExceptionArbiterService
{
    PulsarExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Pulsar-oriented exception arbiter modelled after the Kafka / Azure / Distributed arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not.
/// </summary>
internal class PulsarExceptionArbiterService : IPulsarExceptionArbiterService
{
    /// <summary>
    ///     HTTP statuses that may clear on retry (aligned with Azure / AWS arbiters).
    ///     Status 0 represents "no HTTP response received" when StatusCode is unset.
    /// </summary>
    private static readonly HashSet<int> TransientHttpStatuses =
    [
        0,
        408,
        429,
        500,
        502,
        503,
        504
    ];

    private static readonly HashSet<SocketError> DnsSocketErrors =
    [
        SocketError.HostNotFound,
        SocketError.NoData,
        SocketError.NoRecovery,
        SocketError.TryAgain
    ];

    private static PulsarExceptionArbiterReport Fresh(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static PulsarExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static PulsarExceptionArbiterReport ClassifyHttpRequestException(HttpRequestException httpRequest)
    {
        // Misconfigured host / DNS failure under an HTTP client call will not clear by retrying,
        // and is a local config problem, not something ops can fix externally.
        if (FindSocketException(httpRequest) is { } socket && DnsSocketErrors.Contains(socket.SocketErrorCode))
        {
            return Fresh(true, false, false);
        }

        // StatusCode is null when no HTTP response was received (connection drop, TLS blip, etc.).
        // Transient HTTP statuses (timeout / throttling / 5xx) are the kind of infra blip ops can clear.
        var status = httpRequest.StatusCode is { } code ? (int) code : 0;
        var isTransient = TransientHttpStatuses.Contains(status);
        return Fresh(true, isTransient, isTransient);
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket)
            {
                return socket;
            }
        }

        return null;
    }

    public PulsarExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // Propagate the inner wrapper's own externally-solvable classification.
            WorkerJobSourceException workerJobSource =>
                Handled(
                    true,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true},
                    workerJobSource.CouldBeExternallySolvable),
            // Connectivity / lookup / broker-busy / timeout blips — ops can fix the broker or network.
            ConnectException
                or LookupException
                or TooManyRequestsException
                or ConsumerBusyException
                or ConsumerAssignException
                or NotConnectedException
                or MetaStoreHandlerNotReadyException
                or RequestTimeoutException
                or TimeoutException => Fresh(true, true, true),
            // Auth / permission failures — ops can grant IAM externally without a worker restart.
            AuthenticationException
                or AuthorizationException
                or GettingAuthenticationDataException
                or NotAllowedException => Fresh(true, false, true),
            // Topic missing / terminated — ops can recreate or restore the topic externally.
            TopicTerminatedException
                or TopicDoesNotExistException => Fresh(true, false, true),
            // Client lifecycle / protocol mismatches — no external infra change fixes these.
            UnsupportedVersionException
                or AlreadyClosedException
                or ConsumerNotFoundException => Fresh(true, false, false),
            // Bad local configuration — requires a config change and restart, not an external fix.
            InvalidConfigurationException
                or InvalidTopicNameException => Fresh(true, false, false),
            // HTTP failures (e.g. OAuth token fetch): classify by status / DNS, not as blanket-transient.
            HttpRequestException httpRequest => ClassifyHttpRequestException(httpRequest),
            System.TimeoutException
                or SocketException => Fresh(true, true, true),
            TaskCanceledException => Fresh(true, true, true),
            // Explicit CancellationToken cancellation from the caller — not externally solvable.
            OperationCanceledException => Fresh(true, false, false),
            // Client-side argument validation — a local bug/config issue, not externally solvable.
            ArgumentException => Fresh(true, false, false),
            // Unrecognized exception type — treat as unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}