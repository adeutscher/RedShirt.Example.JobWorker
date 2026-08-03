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

    private static PulsarExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static PulsarExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static PulsarExceptionArbiterReport ClassifyHttpRequestException(HttpRequestException httpRequest)
    {
        // Misconfigured host / DNS failure under an HTTP client call will not clear by retrying.
        if (FindSocketException(httpRequest) is { } socket && DnsSocketErrors.Contains(socket.SocketErrorCode))
        {
            return Fresh(false, false);
        }

        // StatusCode is null when no HTTP response was received (connection drop, TLS blip, etc.).
        var status = httpRequest.StatusCode is { } code ? (int) code : 0;
        return Fresh(false, TransientHttpStatuses.Contains(status));
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
            exception = aggregate.InnerException;
        }

        return exception switch
        {
            WorkerJobSourceException workerJobSource =>
                Handled(workerJobSource.IsCritical, workerJobSource is {IsHandled: false, CouldBeTransient: true}),
            ConnectException
                or LookupException
                or TooManyRequestsException
                or ConsumerBusyException
                or ConsumerAssignException
                or NotConnectedException
                or MetaStoreHandlerNotReadyException
                or RequestTimeoutException
                or TimeoutException => Fresh(false, true),
            AuthenticationException
                or AuthorizationException
                or GettingAuthenticationDataException
                or NotAllowedException
                or UnsupportedVersionException
                or TopicTerminatedException
                or AlreadyClosedException
                or ConsumerNotFoundException
                or InvalidConfigurationException
                or InvalidTopicNameException => Fresh(true, false),
            // HTTP failures (e.g. OAuth token fetch): classify by status / DNS, not as blanket-transient.
            HttpRequestException httpRequest => ClassifyHttpRequestException(httpRequest),
            System.TimeoutException
                or SocketException => Fresh(false, true),
            TaskCanceledException => Fresh(false, true),
            OperationCanceledException => Fresh(false, false),
            ArgumentException => Fresh(false, false),
            _ => Fresh(true, false)
        };
    }
}