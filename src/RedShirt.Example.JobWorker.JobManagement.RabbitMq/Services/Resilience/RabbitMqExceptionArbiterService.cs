using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

/// <summary>
///     Classifies RabbitMQ client exceptions for retry decisions.
/// </summary>
internal interface IRabbitMqExceptionArbiterService
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
    RabbitMqExceptionArbiterReport GetReport(Exception exception, int attemptNumber);
}

/// <summary>
///     RabbitMQ-oriented exception arbiter modelled after the Kafka / Azure / Distributed arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not
///     (except auth on the first attempt, which allows a secret-manager refresh).
/// </summary>
internal class RabbitMqExceptionArbiterService : IRabbitMqExceptionArbiterService
{
    private static RabbitMqExceptionArbiterReport Fresh(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new RabbitMqExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static RabbitMqExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new RabbitMqExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static RabbitMqExceptionArbiterReport MapBrokerUnreachableException(BrokerUnreachableException exception,
        int attemptNumber)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (exception.InnerException is AuthenticationFailureException
            or PossibleAuthenticationFailureException)
        {
            return MapAuthenticationException(attemptNumber);
        }

        return Fresh(true, true, true);
    }

    /// <summary>
    ///     Handle the special case of authentication failures.
    /// </summary>
    private static RabbitMqExceptionArbiterReport MapAuthenticationException(int attemptNumber)
    {
        // First offence is treated as transient so a rotated secret can be pulled once.
        return Fresh(true, attemptNumber == 1, true);
    }

    public RabbitMqExceptionArbiterReport GetReport(Exception exception, int attemptNumber)
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
            // Propagate the inner wrapper's own externally-solvable classification.
            WorkerJobSourceException workerJobSource =>
                Handled(
                    true,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true},
                    workerJobSource.CouldBeExternallySolvable),
            // Secret-manager failures (e.g. credential fetch) — already wrapped; propagate the
            // secret layer's transient / externally-solvable classification for upstream decisions.
            WorkerSecretManagerException workerSecretManager =>
                Handled(true, workerSecretManager.CouldBeTransient, workerSecretManager.CouldBeExternallySolvable),
            // Broker / network / channel lifecycle blips — auto-recovery or ops can clear them.
            BrokerUnreachableException brokerUnreachableException => MapBrokerUnreachableException(
                brokerUnreachableException, attemptNumber),
            ConnectFailureException
                or AlreadyClosedException
                or OperationInterruptedException => Fresh(true, true, true),
            // Auth failures — first attempt may refresh secrets; later attempts are permanent.
            AuthenticationFailureException
                or PossibleAuthenticationFailureException => MapAuthenticationException(attemptNumber),
            // Too many channels on this connection — a local client lifecycle issue, not an external fix.
            ChannelAllocationException => Fresh(true, false, false),
            // Wire / protocol mismatches — require a local client or broker version fix.
            ProtocolException
                or ProtocolVersionMismatchException
                or PacketNotRecognizedException
                or UnexpectedFrameException
                or UnexpectedMethodException
                or UnknownClassOrMethodException
                or SyntaxErrorException
                or MalformedFrameException
                or HardProtocolException
                or WireFormattingException => Fresh(true, false, false),
            // Remaining RabbitMQ client failures are expected but not blindly retried.
            RabbitMQClientException => Fresh(true, false, false),
            IOException
                or SocketException
                or TimeoutException => Fresh(true, true, true),
            // HttpClient-style timeouts sometimes surface as TaskCanceledException.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true, true, true),
            // Explicit CancellationToken cancellation from the caller — do not retry; not externally solvable.
            OperationCanceledException => Fresh(true, false, false),
            // Client-side argument validation — not retryable and not externally solvable.
            ArgumentException => Fresh(true, false, false),
            // Disposed connection / channel after a drop — expected; retrying the same instance will not help.
            ObjectDisposedException => Fresh(true, false, false),
            // Unrecognized exception type — treat as unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}