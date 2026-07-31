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
    private static readonly HashSet<ConnectionFailureType> TransientConnectionFailures =
    [
        ConnectionFailureType.UnableToConnect,
        ConnectionFailureType.SocketFailure,
        ConnectionFailureType.SocketClosed,
        ConnectionFailureType.Loading,
        ConnectionFailureType.UnableToResolvePhysicalConnection
    ];

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException;
        }

        return exception;
    }

    private static RedisExceptionArbiterReport Fresh(bool isExpected, bool couldBeTransient)
    {
        return new RedisExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient
        };
    }

    private static RedisExceptionArbiterReport Handled(bool isExpected, bool couldBeTransient)
    {
        return new RedisExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient
        };
    }

    public RedisExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        exception = Unwrap(exception);

        return exception switch
        {
            // Already classified/wrapped by an earlier Distributed layer — do not wrap again.
            WorkerDistributedException workerDistributed =>
                Handled(workerDistributed.IsExpected, workerDistributed.IsTransient),
            WorkerSecretManagerException secretManager =>
                Handled(secretManager.IsExpected, secretManager.IsTransient),
            // Command / connection timeouts from StackExchange.Redis.
            RedisTimeoutException => Fresh(true, true),
            // Connection drop / reconnect scenarios; auth and disposed connections are not retryable.
            RedisConnectionException connection =>
                Fresh(true, TransientConnectionFailures.Contains(connection.FailureType)),
            // Server-side conditions (e.g. LOADING) are often brief; treat as possibly transient.
            RedisServerException => Fresh(true, true),
            // Generic Redis failures that were not matched above.
            RedisException => Fresh(true, true),
            TimeoutException or SocketException => Fresh(true, true),
            // HttpClient-style timeouts sometimes surface as TaskCanceledException.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true, true),
            // Explicit CancellationToken cancellation from the caller — do not retry.
            OperationCanceledException => Fresh(true, false),
            // Client-side argument validation — not retryable.
            ArgumentException => Fresh(true, false),
            // Unrecognized exception type — not treated as a known Redis / distributed failure.
            _ => Fresh(false, false)
        };
    }
}