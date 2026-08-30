using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

/// <summary>
///     Classifies NATS / JetStream client exceptions for retry decisions.
/// </summary>
internal interface INatsExceptionArbiterService
{
    NatsExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     NATS-oriented exception arbiter modelled after the Kafka / Azure / Distributed arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not.
/// </summary>
internal class NatsExceptionArbiterService : INatsExceptionArbiterService
{
    private static readonly HashSet<int> TransientStatusCodes =
    [
        408,
        429,
        500,
        502,
        503,
        504
    ];

    private static readonly HashSet<int> ExternallySolvablePermanentStatusCodes =
    [
        401,
        403,
        404
    ];

    private static readonly HashSet<int> PermanentStatusCodes =
    [
        400,
        401,
        403,
        404,
        409
    ];

    private static NatsExceptionArbiterReport Fresh(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new NatsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static NatsExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new NatsExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static NatsExceptionArbiterReport ClassifyStatusCode(int code)
    {
        if (ExternallySolvablePermanentStatusCodes.Contains(code))
        {
            return Fresh(true, false, true);
        }

        if (PermanentStatusCodes.Contains(code))
        {
            return Fresh(true, false, false);
        }

        var isTransient = TransientStatusCodes.Contains(code) || code >= 500;
        return Fresh(true, isTransient, isTransient);
    }

    private static NatsExceptionArbiterReport ClassifyProtocolException(NatsJSProtocolException protocol)
    {
        return protocol.HeaderMessage switch
        {
            // Consume-path stalls / idle timeouts — an infra blip ops can clear.
            NatsHeaders.Messages.RequestTimeout
                or NatsHeaders.Messages.IdleHeartbeat
                or NatsHeaders.Messages.RequestsPending => Fresh(true, true, true),
            // Consumer was deleted — ops can recreate it without a worker restart.
            NatsHeaders.Messages.ConsumerDeleted => Fresh(true, false, true),
            // Local protocol / configuration mismatches — not retryable and not an external fix.
            NatsHeaders.Messages.BadRequest
                or NatsHeaders.Messages.ConsumerIsPushBased
                or NatsHeaders.Messages.MessageSizeExceedsMaxBytes
                or NatsHeaders.Messages.NoMessages => Fresh(true, false, false),
            _ => ClassifyStatusCode(protocol.HeaderCode)
        };
    }

    public NatsExceptionArbiterReport GetReport(Exception exception)
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
            // Secret-manager failures (e.g. credential fetch) — already wrapped; never retry as NATS
            // infrastructure. Propagate externally-solvable so ops can fix secrets without a restart.
            WorkerSecretManagerException workerSecretManager =>
                Handled(true, workerSecretManager.CouldBeTransient, workerSecretManager.CouldBeExternallySolvable),
            // Connection / timeout / no-responder blips — ops can restore the broker or network.
            NatsConnectionFailedException
                or NatsJSConnectionException
                or NatsNoRespondersException
                or NatsNoReplyException
                or NatsTimeoutException
                or NatsJSTimeoutException
                or NatsJSApiNoResponseException
                or NatsJSPublishNoResponseException
                or NatsJSConnectionException => Fresh(true, true, true),
            NatsJSApiException api => ClassifyStatusCode(api.Error.Code),
            NatsJSProtocolException protocol => ClassifyProtocolException(protocol),
            // Duplicate publish ack / oversized payload / protocol mismatch — local, not retryable.
            NatsJSDuplicateMessageException
                or NatsPayloadTooLargeException
                or NatsProtocolViolationException => Fresh(true, false, false),
            // Auth failures — ops can grant credentials / users without a worker restart.
            NatsServerException {IsAuthError: true} => Fresh(true, false, true),
            NatsServerException server when
                server.Error.Contains("permissions violation", StringComparison.OrdinalIgnoreCase) =>
                Fresh(true, false, true),
            // Remaining server errors are typically infra issues that can clear externally.
            NatsServerException => Fresh(true, true, true),
            // Remaining JetStream / NATS client failures are expected but not blindly retried.
            NatsJSException
                or NatsException => Fresh(true, false, false),
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
            // Unrecognized exception type — treat as unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}