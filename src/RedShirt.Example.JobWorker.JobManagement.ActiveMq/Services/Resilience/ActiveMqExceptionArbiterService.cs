using Apache.NMS;
using Apache.NMS.ActiveMQ;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using System.Net.Sockets;
using ActiveMqIoException = Apache.NMS.ActiveMQ.IOException;
using IOException = System.IO.IOException;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

/// <summary>
///     Classifies ActiveMQ / NMS client exceptions for retry decisions.
/// </summary>
internal interface IActiveMqExceptionArbiterService
{
    /// <summary>
    ///     Get a judgement on an exception.
    /// </summary>
    /// <param name="exception"></param>
    /// <param name="attemptNumber">
    ///     Attempt number. First attempt number starts at 1. This arbiter's partner retry wrapper uses
    ///     Polly pipelines that are zero-based, but I find using non-zero-based more intuitive.
    /// </param>
    /// <returns></returns>
    ActiveMqExceptionArbiterReport GetReport(Exception exception, int attemptNumber);
}

/// <summary>
///     ActiveMQ-oriented exception arbiter modelled after the Kafka / Redis Streams / Pulsar arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not.
/// </summary>
internal class ActiveMqExceptionArbiterService : IActiveMqExceptionArbiterService
{
    private static ActiveMqExceptionArbiterReport Fresh(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new ActiveMqExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static ActiveMqExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new ActiveMqExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    /// <summary>
    ///     Handle the special case of an NMSSecurityException.
    /// </summary>
    /// <param name="exception"></param>
    /// <param name="attemptNumber"></param>
    /// <returns></returns>
    private static ActiveMqExceptionArbiterReport MapSecurityException(NMSSecurityException exception,
        int attemptNumber)
    {
        // Not super-happy about judging off of a message, but Google says that the exit code property is ambiguous.
        // That's a bit of an understatement, as in practice it's blank.
        var firstPasswordOffense = exception.Message.EndsWith(" or password is invalid.") && attemptNumber == 1;
        return Fresh(true, firstPasswordOffense, true);
    }

    public ActiveMqExceptionArbiterReport GetReport(Exception exception, int attemptNumber)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // Already classified/wrapped by an earlier job-source layer — do not wrap again.
            // Only allow further retry when the prior wrapper has not already exhausted retries.
            WorkerJobSourceException workerJobSource =>
                Handled(
                    true,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true},
                    workerJobSource.CouldBeExternallySolvable),
            // Secret-manager failures (e.g. credential fetch) — already wrapped; propagate the
            // secret layer's transient / externally-solvable classification for upstream decisions.
            WorkerSecretManagerException workerSecretManager =>
                Handled(true, workerSecretManager.CouldBeTransient, workerSecretManager.CouldBeExternallySolvable),
            // Queue lookup returned null — ops can create the destination without a worker restart.
            CouldNotLoadQueueException => Fresh(true, false, true),
            // Unsupported / unreadable payload — a local data issue, not retryable.
            CouldNotRetrieveMessageBodyException => Fresh(true, false, false),
            // Auth failures — ops can grant credentials / ACLs externally.
            NMSSecurityException securityException => MapSecurityException(securityException, attemptNumber),
            // Missing / invalid destination — ops can create or restore the queue externally.
            InvalidDestinationException => Fresh(true, false, true),
            // Bad local client identity or selector — requires a config change, not an external fix.
            InvalidClientIDException
                or InvalidSelectorException => Fresh(true, false, false),
            // Payload / cursor issues — not retryable and not externally solvable.
            MessageEOFException
                or MessageFormatException
                or MessageNotReadableException
                or MessageNotWriteableException => Fresh(true, false, false),
            // Nested transaction — a local client-state problem.
            TransactionInProgressException => Fresh(true, false, false),
            // Broker rolled back — a brief conflict that can clear on retry / broker recovery.
            TransactionRolledBackException => Fresh(true, true, true),
            // Timeouts and broker resource pressure — infra blips ops can clear.
            RequestTimedOutException
                or ResourceAllocationException => Fresh(true, true, true),
            // Connection / consumer lifecycle blips — reconnecting or restarting the broker can clear them.
            NMSConnectionException
                or ConnectionClosedException
                or ConnectionFailedException
                or ConsumerClosedException
                or IllegalStateException => Fresh(true, true, true),
            // Transport IO failures from the OpenWire client.
            ActiveMqIoException => Fresh(true, true, true),
            // Remaining NMS failures (including BrokerException) are expected broker/client issues.
            NMSException => Fresh(true, true, true),
            TimeoutException
                or SocketException
                or IOException => Fresh(true, true, true),
            // HttpClient-style timeouts sometimes surface as TaskCanceledException.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true, true, true),
            // Explicit CancellationToken cancellation from the caller — do not retry; not externally solvable.
            OperationCanceledException => Fresh(true, false, false),
            // Client-side argument validation — not retryable and not externally solvable.
            ArgumentException => Fresh(true, false, false),
            // Unrecognized exception type — treat as unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}