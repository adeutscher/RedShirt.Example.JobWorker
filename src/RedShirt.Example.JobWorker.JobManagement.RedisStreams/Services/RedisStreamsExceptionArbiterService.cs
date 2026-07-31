using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using StackExchange.Redis;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services;

internal interface IRedisStreamsExceptionArbiterService
{
    RedisStreamsExceptionArbiterReport GetReport(Exception exception);
}

internal class RedisStreamsExceptionArbiterService : IRedisStreamsExceptionArbiterService
{
    private static readonly HashSet<ConnectionFailureType> CriticalConnectionFailures =
    [
        ConnectionFailureType.AuthenticationFailure,
        ConnectionFailureType.ProtocolFailure,
        ConnectionFailureType.ConnectionDisposed,
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
            WorkerJobSourceException workerJobSource =>
                Handled(workerJobSource.IsCritical,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true}),
            RedisTimeoutException => Fresh(false, true),
            RedisConnectionException connection =>
                Fresh(CriticalConnectionFailures.Contains(connection.FailureType),
                    TransientConnectionFailures.Contains(connection.FailureType)),
            RedisServerException => Fresh(false, true),
            RedisException => Fresh(false, true),
            TimeoutException or SocketException => Fresh(false, true),
            TaskCanceledException => Fresh(false, true),
            OperationCanceledException => Fresh(false, false),
            ArgumentException => Fresh(false, false),
            _ => Fresh(true, false)
        };
    }
}
