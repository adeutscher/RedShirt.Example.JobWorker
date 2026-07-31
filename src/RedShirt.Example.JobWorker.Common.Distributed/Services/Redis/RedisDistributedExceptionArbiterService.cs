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
///     Redis-oriented exception arbiter modeled after the Azure exception arbiter:
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
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate
               && aggregate.InnerException is not null)
        {
            exception = aggregate.InnerException;
        }

        return exception;
    }

    private static RedisExceptionArbiterReport Fresh(bool couldBeTransient) => new()
    {
        AlreadyHandled = false,
        CouldBeTransient = couldBeTransient
    };

    private static RedisExceptionArbiterReport Handled(bool couldBeTransient) => new()
    {
        AlreadyHandled = true,
        CouldBeTransient = couldBeTransient
    };

    public RedisExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        exception = Unwrap(exception);

        return exception switch
        {
            // Already classified/wrapped by an earlier Distributed layer — do not wrap again.
            WorkerDistributedException workerDistributed => Handled(workerDistributed.IsTransient),
            WorkerSecretManagerException secretManager => Handled(secretManager.IsTransient),
            // Command / connection timeouts from StackExchange.Redis.
            RedisTimeoutException => Fresh(true),
            // Connection drop / reconnect scenarios; auth and disposed connections are not retryable.
            RedisConnectionException connection =>
                Fresh(TransientConnectionFailures.Contains(connection.FailureType)),
            // Server-side conditions (e.g. LOADING) are often brief; treat as possibly transient.
            RedisServerException => Fresh(true),
            // Generic Redis failures that were not matched above.
            RedisException => Fresh(true),
            TimeoutException => Fresh(true),
            SocketException => Fresh(true),
            // HttpClient-style timeouts sometimes surface as TaskCanceledException.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true),
            _ => Fresh(false)
        };
    }
}
