using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using StackExchange.Redis;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;

/// <summary>
///     Classifies Redis / distributed-cache exceptions for retry decisions.
/// </summary>
internal interface IRedisDistributedExceptionArbiterService
{
    RedisExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Redis-oriented exception arbiter modelled after the Azure exception arbiter:
///     known infrastructure failures may be transient; caller cancel and bad arguments are not.
/// </summary>
internal class RedisDistributedExceptionArbiterService : IRedisDistributedExceptionArbiterService
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

    private static RedisExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new RedisExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static RedisExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new RedisExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    public RedisExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Unwrap
        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException;
        }

        return exception switch
        {
            // Already classified/wrapped by an earlier Distributed layer — do not wrap again.
            WorkerDistributedException workerDistributed =>
                Handled(workerDistributed.IsCritical, workerDistributed.IsTransient),
            WorkerSecretManagerException secretManager =>
                Handled(secretManager.IsCritical, secretManager.IsTransient),
            // Command / connection timeouts from StackExchange.Redis.
            RedisTimeoutException => Fresh(false, true),
            // Connection failures: critical types surface raw, transient types may be retried.
            // remaining known types are non-critical and non-transient.
            RedisConnectionException connection =>
                Fresh(CriticalConnectionFailures.Contains(connection.FailureType),
                    TransientConnectionFailures.Contains(connection.FailureType)),
            // Server-side conditions (e.g. LOADING) are often brief; treat as possibly transient.
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