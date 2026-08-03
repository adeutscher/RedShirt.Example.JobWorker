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

    private static RedisStreamsExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new RedisStreamsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static RedisStreamsExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new RedisStreamsExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
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
            WorkerJobSourceException workerJobSource =>
                Handled(workerJobSource.IsCritical,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true}),
            // Already classified/wrapped by Common.Distributed (e.g. connection cache) — do not wrap again.
            WorkerDistributedException workerDistributed =>
                Handled(workerDistributed.IsCritical, workerDistributed.IsTransient),
            // Command / connection timeouts from StackExchange.Redis.
            RedisTimeoutException => Fresh(false, true),
            // Connection failures: critical types surface raw, transient types may be retried.
            RedisConnectionException connection =>
                Fresh(CriticalConnectionFailures.Contains(connection.FailureType),
                    TransientConnectionFailures.Contains(connection.FailureType)),
            // NOGROUP: consumer group (or stream) was never created — setup/config, not retryable.
            RedisServerException server when IsNoGroup(server) => Fresh(false, false),
            // Other server-side conditions (e.g. LOADING) are often brief; treat as possibly transient.
            RedisServerException => Fresh(false, true),
            // Generic Redis failures that were not matched above.
            RedisException => Fresh(false, true),
            TimeoutException or SocketException => Fresh(false, true),
            // HttpClient-style timeouts sometimes surface as TaskCanceledException.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(false, true),
            // Explicit CancellationToken cancellation from the caller — do not retry.
            OperationCanceledException => Fresh(false, false),
            // Client-side argument validation — not retryable.
            ArgumentException => Fresh(false, false),
            // Unrecognized exception type — treat as critical so callers surface the raw failure.
            _ => Fresh(true, false)
        };
    }
}
