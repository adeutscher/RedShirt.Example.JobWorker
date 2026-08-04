using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using StackExchange.Redis;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services.Resilience;

/// <summary>
///     Classifies Redis Streams client exceptions for retry decisions.
/// </summary>
internal interface IRedisStreamsExceptionArbiterService
{
    RedisStreamsExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Redis Streams-oriented exception arbiter modelled after the Azure / Distributed / Kafka arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not.
/// </summary>
internal class RedisStreamsExceptionArbiterService : IRedisStreamsExceptionArbiterService
{
    private static readonly HashSet<ConnectionFailureType> CriticalConnectionFailures =
    [
        ConnectionFailureType.AuthenticationFailure, // bad password / ACL / AUTH config
        ConnectionFailureType.ProtocolFailure, // protocol / version mismatch
        ConnectionFailureType.ConnectionDisposed, // multiplexer used after dispose
        ConnectionFailureType.InternalFailure
    ];

    private static readonly HashSet<ConnectionFailureType> TransientConnectionFailures =
    [
        ConnectionFailureType.UnableToConnect,
        ConnectionFailureType.SocketFailure,
        ConnectionFailureType.SocketClosed,
        ConnectionFailureType.Loading,
        ConnectionFailureType.UnableToResolvePhysicalConnection
    ];

    private static bool IsNoGroup(RedisServerException exception)
    {
        return exception.Message.Contains("NOGROUP", StringComparison.Ordinal);
    }

    private static RedisStreamsExceptionArbiterReport Fresh(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new RedisStreamsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static RedisStreamsExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new RedisStreamsExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    public RedisStreamsExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException;
        }

        return exception switch
        {
            // Already classified/wrapped by an earlier job-source layer — do not wrap again.
            // Only allow further retry when the prior wrapper has not already exhausted retries.
            WorkerJobSourceException w =>
                Handled(true, w is {IsHandled: false, CouldBeTransient: true}, w.CouldBeExternallySolvable),
            // Already classified/wrapped by Common.Distributed (e.g. connection cache) — do not wrap again.
            WorkerDistributedException w =>
                Handled(true, w is {IsHandled: false, CouldBeTransient: true}, w.CouldBeExternallySolvable),
            // Command / connection timeouts from StackExchange.Redis — a slow/overloaded server can be
            // fixed externally (scaling, restarting) without touching this worker.
            RedisTimeoutException => Fresh(true, true, true),
            // Connection failures: known types are expected. Auth failures point at bad credentials or
            // ACLs, which ops can fix externally; the other critical failure types are non-transient
            // local/client-side conditions (protocol mismatch, disposed multiplexer, internal SDK bug).
            RedisConnectionException connection when CriticalConnectionFailures.Contains(connection.FailureType) =>
                Fresh(true, false, connection.FailureType == ConnectionFailureType.AuthenticationFailure),
            // Remaining known connection failure types (socket/connect issues, server loading) are
            // transient infrastructure conditions that can resolve externally.
            RedisConnectionException connection =>
                Fresh(true, TransientConnectionFailures.Contains(connection.FailureType), true),
            // NOGROUP: consumer group (or stream) was never created — ops can create it externally
            // without restarting the worker, but retrying the same call will not help.
            RedisServerException server when IsNoGroup(server) => Fresh(true, false, true),
            // Other server-side conditions (e.g. LOADING) are often brief and infra-related.
            RedisServerException => Fresh(true, true, true),
            // Generic Redis failures that were not matched above — treat like other server failures.
            RedisException => Fresh(true, true, true),
            TimeoutException
                or SocketException => Fresh(true, true, true),
            // HttpClient-style timeouts sometimes surface as TaskCanceledException.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true, true, true),
            // Explicit CancellationToken cancellation from the caller — do not retry, and there is
            // nothing external to fix.
            OperationCanceledException => Fresh(true, false, false),
            // Client-side argument validation — bad local configuration/arguments, not retryable.
            ArgumentException => Fresh(true, false, false),
            // Unrecognized exception type — treat as unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}