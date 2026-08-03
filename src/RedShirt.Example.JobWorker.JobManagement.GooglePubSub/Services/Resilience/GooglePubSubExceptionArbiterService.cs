using Grpc.Core;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services.Resilience;

/// <summary>
///     Classifies Google Pub/Sub client exceptions for retry decisions.
/// </summary>
internal interface IGooglePubSubExceptionArbiterService
{
    GooglePubSubExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Pub/Sub-oriented exception arbiter modelled after the Azure / Distributed / Kafka arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not.
/// </summary>
internal class GooglePubSubExceptionArbiterService : IGooglePubSubExceptionArbiterService
{
    private static readonly HashSet<StatusCode> TransientStatusCodes =
    [
        StatusCode.Unavailable,
        StatusCode.DeadlineExceeded,
        StatusCode.ResourceExhausted,
        StatusCode.Aborted,
        StatusCode.Internal,
        StatusCode.Unknown
    ];

    private static readonly HashSet<StatusCode> PermanentStatusCodes =
    [
        StatusCode.InvalidArgument,
        StatusCode.NotFound,
        StatusCode.AlreadyExists,
        StatusCode.PermissionDenied,
        StatusCode.Unauthenticated,
        StatusCode.FailedPrecondition,
        StatusCode.OutOfRange,
        StatusCode.Unimplemented,
        StatusCode.DataLoss
    ];

    private static GooglePubSubExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static GooglePubSubExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    public GooglePubSubExceptionArbiterReport GetReport(Exception exception)
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
                Handled(workerJobSource.IsCritical, workerJobSource is {IsHandled: false, CouldBeTransient: true}),
            // gRPC status from the Pub/Sub client / emulator.
            RpcException rpc => PermanentStatusCodes.Contains(rpc.StatusCode)
                ? Fresh(false, false)
                : Fresh(false, TransientStatusCodes.Contains(rpc.StatusCode)),
            TimeoutException or SocketException or HttpRequestException => Fresh(false, true),
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